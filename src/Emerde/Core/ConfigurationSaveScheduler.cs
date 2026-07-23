using Fischless.Configuration;

namespace Emerde.Core;

internal static class ConfigurationSaveScheduler
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private const int MaximumRetryCount = 5;
    private static readonly object SyncRoot = new();
    private static readonly System.Threading.Timer Timer = new(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    private static bool pending;
    private static int retryCount;
    private static bool savesSuppressed;

    public static Exception? LastSaveError { get; private set; }

    public static void Request()
    {
        lock (SyncRoot)
        {
            if (savesSuppressed)
            {
                return;
            }
            pending = true;
            retryCount = 0;
            Timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public static void Flush()
    {
        lock (SyncRoot)
        {
            SaveLocked(force: false);
        }
    }

    public static void SaveNow()
    {
        lock (SyncRoot)
        {
            SaveLocked(force: true);
        }
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

    private static void SaveLocked(bool force)
    {
        if (savesSuppressed)
        {
            return;
        }

        if (!pending && !force)
        {
            return;
        }

        Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            ConfigurationManager.Save();
            pending = false;
            retryCount = 0;
            LastSaveError = null;
        }
        catch (Exception e)
        {
            pending = true;
            LastSaveError = e;
            bool retryable = e is IOException or UnauthorizedAccessException;
            if (!force && retryable && retryCount < MaximumRetryCount)
            {
                retryCount++;
                TimeSpan delay = TimeSpan.FromMilliseconds(RetryDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1));
                Timer.Change(delay, Timeout.InfiniteTimeSpan);
            }
            AppSessionLogger.WriteException(e);
            if (force)
            {
                throw;
            }
        }
    }
}
