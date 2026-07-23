using Fischless.Configuration;

namespace Emerde.Core;

internal static class ConfigurationSaveScheduler
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly object SyncRoot = new();
    private static readonly System.Threading.Timer Timer = new(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    private static bool pending;

    public static void Request()
    {
        lock (SyncRoot)
        {
            pending = true;
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

    public static T ExecuteExclusive<T>(Func<T> action)
    {
        lock (SyncRoot)
        {
            return action();
        }
    }

    private static void SaveLocked(bool force)
    {
        if (!pending && !force)
        {
            return;
        }

        Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            ConfigurationManager.Save();
            pending = false;
        }
        catch (Exception e)
        {
            pending = true;
            Timer.Change(RetryDelay, Timeout.InfiniteTimeSpan);
            AppSessionLogger.WriteException(e);
            if (force)
            {
                throw;
            }
        }
    }
}
