using System.IO;

namespace Emerde.Core;

internal static class AppSessionLogger
{
    private static readonly object SyncRoot = new();
    private static string? currentLogPath;
    private static bool isStarted;

    public static void Start(string reason = "application started")
    {
        if (!Configurations.IsSessionLogEnabled.Get())
        {
            return;
        }

        lock (SyncRoot)
        {
            if (isStarted)
            {
                return;
            }

            Directory.CreateDirectory(AppPaths.LogsDirectory);
            currentLogPath = Path.Combine(AppPaths.LogsDirectory, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            isStarted = true;
            Write(reason);
        }
    }

    public static void StartNow(string reason)
    {
        lock (SyncRoot)
        {
            isStarted = false;
        }

        Start(reason);
    }

    public static void Stop(string reason = "application stopped")
    {
        Write(reason);

        lock (SyncRoot)
        {
            isStarted = false;
            currentLogPath = null;
        }
    }

    public static void Write(string message)
    {
        lock (SyncRoot)
        {
            if (!isStarted || string.IsNullOrWhiteSpace(currentLogPath))
            {
                return;
            }

            File.AppendAllText(currentLogPath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
    }

    public static void WriteException(Exception exception)
    {
        Write($"exception {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception}");
        WriteError(exception);
    }

    private static void WriteError(Exception exception)
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(currentLogPath))
            {
                return;
            }

            string errorPath = Path.Combine(
                Path.GetDirectoryName(currentLogPath)!,
                Path.GetFileNameWithoutExtension(currentLogPath) + ".error.log");

            File.AppendAllText(errorPath, $"{DateTime.Now:O} {exception}{Environment.NewLine}");
        }
    }
}
