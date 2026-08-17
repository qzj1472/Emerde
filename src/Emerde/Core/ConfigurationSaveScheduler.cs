using Fischless.Configuration;

namespace Emerde.Core;

internal static class ConfigurationSaveScheduler
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PersistentRetryDelay = TimeSpan.FromSeconds(30);
    private const int MaximumRetryCount = 5;
    private static readonly object SyncRoot = new();
    private static readonly System.Threading.Timer Timer = new(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    private static bool pending;
    private static int retryCount;
    private static bool savesSuppressed;
    private static long requestedRevision;
    private static long savedRevision;
    private static long notifiedFailureRevision;

    public static Exception? LastSaveError { get; private set; }

    public static event EventHandler<ConfigurationSaveStateChangedEventArgs>? SaveStateChanged;

    public static void Request()
    {
        lock (SyncRoot)
        {
            if (savesSuppressed)
            {
                return;
            }
            requestedRevision++;
            pending = true;
            retryCount = 0;
            Timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public static void Flush()
    {
        ConfigurationSaveStateChangedEventArgs? stateChanged;
        lock (SyncRoot)
        {
            stateChanged = SaveLocked(force: false);
        }
        RaiseSaveStateChanged(stateChanged);
    }

    public static void SaveNow()
    {
        ConfigurationSaveStateChangedEventArgs? stateChanged;
        lock (SyncRoot)
        {
            stateChanged = SaveLocked(force: true);
        }
        RaiseSaveStateChanged(stateChanged);
    }

    public static bool TrySaveNow()
    {
        try
        {
            SaveNow();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static T ExecuteExclusive<T>(Func<T> action)
    {
        lock (SyncRoot)
        {
            return action();
        }
    }

    public static void SuppressUntilRestart()
    {
        lock (SyncRoot)
        {
            savesSuppressed = true;
            pending = false;
            retryCount = 0;
            Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public static void ResumeAfterCancelledRestart()
    {
        lock (SyncRoot)
        {
            savesSuppressed = false;
            pending = false;
            retryCount = 0;
            savedRevision = requestedRevision;
            notifiedFailureRevision = 0;
            LastSaveError = null;
            Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private static ConfigurationSaveStateChangedEventArgs? SaveLocked(bool force)
    {
        if (savesSuppressed)
        {
            return null;
        }

        if (!pending && !force)
        {
            return null;
        }

        Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            ConfigurationManager.Save();
            pending = false;
            retryCount = 0;
            LastSaveError = null;
            bool recovered = notifiedFailureRevision > savedRevision;
            savedRevision = requestedRevision;
            notifiedFailureRevision = 0;
            return recovered
                ? new ConfigurationSaveStateChangedEventArgs(savedRevision, true, null)
                : null;
        }
        catch (Exception e)
        {
            pending = true;
            LastSaveError = e;
            bool retryable = e is IOException or UnauthorizedAccessException;
            bool transientRetryScheduled = false;
            if (!force && retryable && retryCount < MaximumRetryCount)
            {
                retryCount++;
                TimeSpan delay = TimeSpan.FromMilliseconds(RetryDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1));
                Timer.Change(delay, Timeout.InfiniteTimeSpan);
                transientRetryScheduled = true;
            }
            else if (!force)
            {
                Timer.Change(PersistentRetryDelay, Timeout.InfiniteTimeSpan);
            }
            AppSessionLogger.WriteException(e);
            if (force)
            {
                throw;
            }
            if (transientRetryScheduled || notifiedFailureRevision == requestedRevision)
            {
                return null;
            }
            notifiedFailureRevision = requestedRevision;
            return new ConfigurationSaveStateChangedEventArgs(requestedRevision, false, e);
        }
    }

    private static void RaiseSaveStateChanged(ConfigurationSaveStateChangedEventArgs? stateChanged)
    {
        if (stateChanged != null)
        {
            try
            {
                SaveStateChanged?.Invoke(null, stateChanged);
            }
            catch (Exception exception)
            {
                AppSessionLogger.WriteException(exception);
            }
        }
    }
}

internal sealed record ConfigurationSaveStateChangedEventArgs(long Revision, bool IsSaved, Exception? Error);
