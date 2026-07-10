using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Emerde.Core;

internal static class AppSessionLogger
{
    private static readonly object LockObject = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private static StreamWriter? writer;
    private static StreamWriter? errorWriter;
    private static BlockingCollection<LogLine>? queue;
    private static Task? worker;

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

            string sessionName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}";
            CurrentFilePath = Path.Combine(directory, $"{sessionName}.log");
            CurrentErrorFilePath = Path.Combine(directory, $"{sessionName}.error.log");
            writer = new StreamWriter(new FileStream(CurrentFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
            errorWriter = new StreamWriter(new FileStream(CurrentErrorFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
            queue = new BlockingCollection<LogLine>(new ConcurrentQueue<LogLine>());
            worker = Task.Run(DrainQueue);

            Enqueue(BuildEvent("info", "application", "start", message));
        }
    }

    public static void Stop(string reason = "application stopped")
    {
        lock (LockObject)
        {
            if (writer is null)
            {
                return;
            }

            Enqueue(BuildEvent("info", "application", "stop", reason));
            queue?.CompleteAdding();
            worker?.Wait(TimeSpan.FromSeconds(2));
            writer.Dispose();
            errorWriter?.Dispose();
            writer = null;
            errorWriter = null;
            queue = null;
            worker = null;
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
        object payload = new
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            level,
            category,
            action,
            message,
            processId = Environment.ProcessId,
            threadId = Environment.CurrentManagedThreadId,
            file = CurrentFilePath,
            data,
        };

        return new LogLine(level, JsonSerializer.Serialize(payload, JsonOptions));
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
            currentQueue.Add(line);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void DrainQueue()
    {
        BlockingCollection<LogLine>? currentQueue = queue;
        if (currentQueue == null)
        {
            return;
        }

        foreach (LogLine line in currentQueue.GetConsumingEnumerable())
        {
            writer?.WriteLine(line.Text);

            if (IsDiagnosticLevel(line.Level))
            {
                errorWriter?.WriteLine(line.Text);
            }
        }
    }

    private static bool IsDiagnosticLevel(string level)
    {
        return level.Equals("warn", StringComparison.OrdinalIgnoreCase)
            || level.Equals("error", StringComparison.OrdinalIgnoreCase)
            || level.Equals("fatal", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteExpiredLogs(string directory)
    {
        DateTime threshold = DateTime.Now.AddDays(-7);

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

    private sealed record LogLine(string Level, string Text);
}
