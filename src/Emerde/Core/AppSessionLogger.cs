using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Emerde.Core;

internal static class AppSessionLogger
{
    private const int QueueCapacity = 10000;

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private static readonly object LockObject = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private static StreamWriter? writer;
    private static StreamWriter? errorWriter;
    private static BlockingCollection<LogLine>? queue;
    private static Task? worker;
    private static CancellationTokenSource? workerCancellation;
    private static DateTime currentLogDate = DateTime.MinValue;

    public static string? CurrentFilePath { get; private set; }
    public static string? CurrentErrorFilePath { get; private set; }

    public static void Start(string reason = "application started")
    {
        if (!Configurations.IsSessionLogEnabled.Get())
        {
            return;
        }

        StartNow(reason);
    }

    public static void StartNow(string message)
    {
        if (!Configurations.IsSessionLogEnabled.Get())
        {
            return;
        }

        if (writer is not null)
        {
            return;
        }

        lock (LockObject)
        {
            if (writer is not null)
            {
                return;
            }

            string directory = AppPaths.LogsDirectory;
            Directory.CreateDirectory(directory);
            DeleteExpiredLogs(directory);

            OpenWriters(directory, DateTime.Now);
            queue = new BlockingCollection<LogLine>(new ConcurrentQueue<LogLine>(), QueueCapacity);
            workerCancellation = new CancellationTokenSource();
            worker = Task.Run(() => DrainQueue(workerCancellation.Token));

            Enqueue(BuildEvent("info", "application", "start", message));
        }
    }

    public static void Stop(string reason = "application stopped")
    {
        Task? stoppingWorker;
        CancellationTokenSource? stoppingCancellation;
        lock (LockObject)
        {
            if (writer is null)
            {
                return;
            }

            Enqueue(BuildEvent("info", "application", "stop", reason));
            queue?.CompleteAdding();
            stoppingWorker = worker;
            stoppingCancellation = workerCancellation;
        }

        bool completed = WaitForWorker(stoppingWorker, StopTimeout);
        if (!completed)
        {
            stoppingCancellation?.Cancel();
            completed = WaitForWorker(stoppingWorker, TimeSpan.FromMilliseconds(500));
        }

        if (completed)
        {
            Cleanup(stoppingWorker);
        }
        else if (stoppingWorker != null)
        {
            _ = stoppingWorker.ContinueWith(
                _ => Cleanup(stoppingWorker),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static bool WaitForWorker(Task? stoppingWorker, TimeSpan timeout)
    {
        try
        {
            return stoppingWorker?.Wait(timeout) ?? true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException or AggregateException)
        {
            return true;
        }
    }

    private static void Cleanup(Task? stoppingWorker)
    {
        lock (LockObject)
        {
            if (!ReferenceEquals(worker, stoppingWorker))
            {
                return;
            }

            writer?.Dispose();
            errorWriter?.Dispose();
            queue?.Dispose();
            workerCancellation?.Dispose();
            writer = null;
            errorWriter = null;
            queue = null;
            worker = null;
            workerCancellation = null;
            currentLogDate = DateTime.MinValue;
        }
    }

    public static void Write(string message)
    {
        Event("info", "general", "message", message);
    }

    public static void WriteException(Exception exception)
    {
        Event("error", "exception", exception.GetType().Name, exception.Message, new
        {
            type = exception.GetType().FullName,
            exception.Message,
            stackTrace = exception.ToString(),
        });
    }

    public static void Event(string level, string category, string action, string message = "", object? data = null)
    {
        Enqueue(BuildEvent(level, category, action, message, data));
    }

    private static LogLine BuildEvent(string level, string category, string action, string message = "", object? data = null)
    {
        DateTime timestamp = DateTime.Now;
        (string filePath, _) = GetDailyLogPaths(AppPaths.LogsDirectory, timestamp);
        object payload = new
        {
            timestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            level,
            category,
            action,
            message = LogSanitizer.SanitizeText(message),
            processId = Environment.ProcessId,
            threadId = Environment.CurrentManagedThreadId,
            file = filePath,
            data = LogSanitizer.SanitizeData(data, JsonOptions),
        };

        return new LogLine(timestamp, level, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static void Enqueue(LogLine line)
    {
        BlockingCollection<LogLine>? currentQueue = queue;

        if (currentQueue == null || currentQueue.IsAddingCompleted)
        {
            return;
        }

        try
        {
            if (currentQueue.TryAdd(line))
            {
                return;
            }

            if (IsDiagnosticLevel(line.Level) && currentQueue.TryTake(out _))
            {
                _ = currentQueue.TryAdd(line);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void DrainQueue(CancellationToken token)
    {
        BlockingCollection<LogLine>? currentQueue = queue;
        if (currentQueue == null)
        {
            return;
        }

        try
        {
            foreach (LogLine line in currentQueue.GetConsumingEnumerable(token))
            {
                EnsureLogDate(line.Timestamp);
                writer?.WriteLine(line.Text);

                if (IsDiagnosticLevel(line.Level))
                {
                    errorWriter?.WriteLine(line.Text);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void EnsureLogDate(DateTime timestamp)
    {
        if (currentLogDate == timestamp.Date)
        {
            return;
        }

        writer?.Dispose();
        errorWriter?.Dispose();
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        DeleteExpiredLogs(AppPaths.LogsDirectory);
        OpenWriters(AppPaths.LogsDirectory, timestamp);
    }

    private static void OpenWriters(string directory, DateTime timestamp)
    {
        (string filePath, string errorFilePath) = GetDailyLogPaths(directory, timestamp);
        CurrentFilePath = filePath;
        CurrentErrorFilePath = errorFilePath;
        currentLogDate = timestamp.Date;
        writer = CreateWriter(filePath);
        errorWriter = CreateWriter(errorFilePath);
    }

    private static StreamWriter CreateWriter(string filePath)
    {
        return new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
    }

    internal static (string FilePath, string ErrorFilePath) GetDailyLogPaths(string directory, DateTime timestamp)
    {
        string date = timestamp.ToString("yyyy-MM-dd");
        return (
            Path.Combine(directory, $"{date}.log"),
            Path.Combine(directory, $"{date}.error.log"));
    }

    private static bool IsDiagnosticLevel(string level)
    {
        return level.Equals("warn", StringComparison.OrdinalIgnoreCase)
            || level.Equals("error", StringComparison.OrdinalIgnoreCase)
            || level.Equals("fatal", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteExpiredLogs(string directory)
    {
        DateTime threshold = DateTime.Now.AddDays(-NormalizeRetentionDays(Configurations.SessionLogRetentionDays.Get()));

        foreach (string file in Directory.GetFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTime(file) < threshold)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal static int NormalizeRetentionDays(int days)
    {
        return Math.Clamp(days, 1, 3650);
    }

    private sealed record LogLine(DateTime Timestamp, string Level, string Text);
}
