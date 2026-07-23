using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    private static readonly LogContextCompactor ContextCompactor = new();

    private static StreamWriter? writer;
    private static StreamWriter? errorWriter;
    private static BlockingCollection<LogLine>? queue;
    private static Task? worker;
    private static CancellationTokenSource? workerCancellation;
    private static DateTime sessionStartedAt = DateTime.MinValue;
    private static int sessionProcessId;
    private static DateTime currentLogDate = DateTime.MinValue;
    private static DateTime disabledLogDate = DateTime.MinValue;
    private static volatile bool isAvailable;
    private static string? lastFailureMessage;

    public static string? CurrentFilePath { get; private set; }
    public static string? CurrentErrorFilePath { get; private set; }
    public static bool IsAvailable => isAvailable;
    public static string? LastFailureMessage => lastFailureMessage;

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
            if (worker is not null)
            {
                return;
            }

            disabledLogDate = DateTime.MinValue;
            sessionStartedAt = DateTime.Now;
            sessionProcessId = Environment.ProcessId;
            ContextCompactor.Reset(sessionStartedAt.Date);
            string directory = AppPaths.LogsDirectory;
            if (!TryOpenWriters(directory, sessionStartedAt))
            {
                return;
            }
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
            if (worker is null)
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
            sessionStartedAt = DateTime.MinValue;
            sessionProcessId = 0;
            currentLogDate = DateTime.MinValue;
            isAvailable = false;
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
            stackTrace = exception.StackTrace,
            innerException = exception.InnerException?.ToString(),
        });
    }

    public static void Event(string level, string category, string action, string message = "", object? data = null)
    {
        Enqueue(BuildEvent(level, category, action, message, data));
    }

    private static LogLine BuildEvent(string level, string category, string action, string message = "", object? data = null)
    {
        DateTime timestamp = DateTime.Now;
        JsonNode? dataNode = LogSanitizer.SanitizeData(data, JsonOptions);
        dataNode = ContextCompactor.Compact(dataNode, level, timestamp.Date);
        object payload = new
        {
            timestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            level,
            category,
            action,
            message = LogSanitizer.SanitizeText(message),
            threadId = Environment.CurrentManagedThreadId,
            data = dataNode,
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
                try
                {
                    if (!EnsureLogDate(line.Timestamp))
                    {
                        continue;
                    }
                    writer?.WriteLine(line.Text);

                    if (ShouldWriteToErrorLog(line.Level))
                    {
                        errorWriter?.WriteLine(line.Text);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    DisableForDate(line.Timestamp, e);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool EnsureLogDate(DateTime timestamp)
    {
        if (IsDisabledForDate(timestamp, disabledLogDate))
        {
            return false;
        }

        if (currentLogDate == timestamp.Date && writer != null && errorWriter != null)
        {
            return true;
        }

        return TryOpenWriters(AppPaths.LogsDirectory, timestamp);
    }

    private static bool TryOpenWriters(string directory, DateTime timestamp)
    {
        if (TryOpenWritersCore(directory, timestamp, out Exception? primaryError))
        {
            return true;
        }

        string fallbackDirectory = Path.Combine(Path.GetTempPath(), AppConfig.PackName, "logs");
        if (!string.Equals(directory, fallbackDirectory, StringComparison.OrdinalIgnoreCase)
            && TryOpenWritersCore(fallbackDirectory, timestamp, out Exception? fallbackError))
        {
            return true;
        }

        DisableForDate(timestamp, primaryError ?? new IOException("No writable log directory is available."));
        return false;
    }

    private static bool TryOpenWritersCore(string directory, DateTime timestamp, out Exception? error)
    {
        try
        {
            Directory.CreateDirectory(directory);
            DeleteExpiredLogs(directory);
            OpenWriters(directory, timestamp);
            error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
            error = e;
            return false;
        }
    }

    private static void OpenWriters(string directory, DateTime timestamp)
    {
        (string filePath, string errorFilePath) = GetSessionLogPaths(
            directory,
            sessionStartedAt == DateTime.MinValue ? timestamp : sessionStartedAt,
            timestamp,
            sessionProcessId == 0 ? Environment.ProcessId : sessionProcessId);
        StreamWriter newWriter = CreateWriter(filePath);
        StreamWriter? newErrorWriter = null;
        try
        {
            newErrorWriter = CreateWriter(errorFilePath);
            string sessionHeader = BuildSessionHeader(
                sessionStartedAt == DateTime.MinValue ? timestamp : sessionStartedAt,
                timestamp,
                sessionProcessId == 0 ? Environment.ProcessId : sessionProcessId,
                filePath,
                errorFilePath);
            newWriter.WriteLine(sessionHeader);
            newErrorWriter.WriteLine(sessionHeader);
        }
        catch
        {
            newWriter.Dispose();
            newErrorWriter?.Dispose();
            throw;
        }

        writer?.Dispose();
        errorWriter?.Dispose();
        CurrentFilePath = filePath;
        CurrentErrorFilePath = errorFilePath;
        currentLogDate = timestamp.Date;
        disabledLogDate = DateTime.MinValue;
        lastFailureMessage = null;
        isAvailable = true;
        writer = newWriter;
        errorWriter = newErrorWriter;
    }

    internal static string BuildSessionHeader(
        DateTime startedAt,
        DateTime logDate,
        int processId,
        string filePath,
        string errorFilePath)
    {
        object payload = new
        {
            type = "session",
            schemaVersion = 3,
            application = AppConfig.PackName,
            version = AppConfig.Version,
            startedAt = startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            logDate = logDate.ToString("yyyy-MM-dd"),
            processId,
            file = filePath,
            errorFile = errorFilePath,
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static void DisableForDate(DateTime timestamp, Exception error)
    {
        Debug.WriteLine(error);
        try
        {
            writer?.Dispose();
            errorWriter?.Dispose();
        }
        catch (Exception disposeError) when (disposeError is IOException or ObjectDisposedException)
        {
            Debug.WriteLine(disposeError);
        }

        writer = null;
        errorWriter = null;
        currentLogDate = DateTime.MinValue;
        disabledLogDate = timestamp.Date;
        lastFailureMessage = $"{timestamp:yyyy-MM-dd HH:mm:ss} {error.GetType().Name}: {error.Message}";
        isAvailable = false;
    }

    internal static bool IsDisabledForDate(DateTime timestamp, DateTime disabledDate)
    {
        return disabledDate != DateTime.MinValue && timestamp.Date == disabledDate.Date;
    }

    private static StreamWriter CreateWriter(string filePath)
    {
        return new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
    }

    internal static (string FilePath, string ErrorFilePath) GetSessionLogPaths(
        string directory,
        DateTime startedAt,
        DateTime timestamp,
        int processId)
    {
        string sessionName = $"{startedAt:yyyyMMdd_HHmmss}_{processId}";
        if (timestamp.Date != startedAt.Date)
        {
            sessionName += $"_{timestamp:yyyyMMdd}";
        }

        return (
            Path.Combine(directory, $"{sessionName}.log"),
            Path.Combine(directory, $"{sessionName}.error.log"));
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

    internal static bool ShouldWriteToErrorLog(string level)
    {
        return level.Equals("error", StringComparison.OrdinalIgnoreCase)
            || level.Equals("fatal", StringComparison.OrdinalIgnoreCase);
    }

    internal static int NormalizeRetentionDays(int days)
    {
        return Math.Clamp(days, 1, 3650);
    }

    private sealed record LogLine(DateTime Timestamp, string Level, string Text);
}

internal sealed class LogContextCompactor
{
    private const int MaximumTextReferences = 2048;
    private readonly object syncRoot = new();
    private readonly Dictionary<string, RoomReference> roomReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextReference> textReferences = new(StringComparer.Ordinal);
    private DateTime currentDate = DateTime.MinValue;
    private int nextRoomId;
    private int nextTextId;

    public void Reset(DateTime logDate)
    {
        lock (syncRoot)
        {
            ResetCore(logDate.Date);
        }
    }

    public JsonNode? Compact(JsonNode? data, string level, DateTime logDate)
    {
        if (data == null)
        {
            return null;
        }

        lock (syncRoot)
        {
            if (currentDate != logDate.Date)
            {
                ResetCore(logDate.Date);
            }

            CompactNode(data, AppSessionLogger.ShouldWriteToErrorLog(level));
            return data;
        }
    }

    private void ResetCore(DateTime logDate)
    {
        currentDate = logDate;
        roomReferences.Clear();
        textReferences.Clear();
        nextRoomId = 0;
        nextTextId = 0;
    }

    private void CompactNode(JsonNode node, bool isErrorLevel)
    {
        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array.ToArray())
            {
                if (item != null)
                {
                    CompactNode(item, isErrorLevel);
                }
            }
            return;
        }

        if (node is not JsonObject jsonObject)
        {
            return;
        }

        CompactRoom(jsonObject, isErrorLevel);
        CompactRepeatedText(jsonObject, "errorOutput", isErrorLevel);
        CompactRepeatedText(jsonObject, "stackTrace", isErrorLevel);

        foreach (KeyValuePair<string, JsonNode?> property in jsonObject.ToArray())
        {
            if (property.Value != null && property.Key is not "roomContext")
            {
                CompactNode(property.Value, isErrorLevel);
            }
        }
    }

    private void CompactRoom(JsonObject jsonObject, bool isErrorLevel)
    {
        string? roomUrlKey = FindPropertyName(jsonObject, "RoomUrl");
        if (roomUrlKey == null || !TryGetString(jsonObject[roomUrlKey], out string roomUrl) || string.IsNullOrWhiteSpace(roomUrl))
        {
            return;
        }

        string? nickNameKey = FindPropertyName(jsonObject, "NickName");
        string nickName = nickNameKey != null && TryGetString(jsonObject[nickNameKey], out string value) ? value : string.Empty;
        if (!roomReferences.TryGetValue(roomUrl, out RoomReference? reference))
        {
            reference = new RoomReference($"r{++nextRoomId}", nickName);
            roomReferences.Add(roomUrl, reference);
        }

        bool nameChanged = !string.IsNullOrWhiteSpace(nickName) && !string.Equals(reference.NickName, nickName, StringComparison.Ordinal);
        if (nameChanged)
        {
            reference.NickName = nickName;
        }
        bool needsDefinition = !reference.DefinedInMain || isErrorLevel && !reference.DefinedInError || nameChanged;
        reference.DefinedInMain = true;
        if (isErrorLevel)
        {
            reference.DefinedInError = true;
        }

        jsonObject.Remove(roomUrlKey);
        if (nickNameKey != null)
        {
            jsonObject.Remove(nickNameKey);
        }
        jsonObject["roomRef"] = reference.Id;
        if (needsDefinition)
        {
            jsonObject["roomContext"] = new JsonObject
            {
                ["url"] = roomUrl,
                ["name"] = reference.NickName,
            };
        }
    }

    private void CompactRepeatedText(JsonObject jsonObject, string propertyName, bool isErrorLevel)
    {
        string? actualPropertyName = FindPropertyName(jsonObject, propertyName);
        if (actualPropertyName == null || !TryGetString(jsonObject[actualPropertyName], out string text) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!textReferences.TryGetValue(text, out TextReference? reference))
        {
            if (textReferences.Count >= MaximumTextReferences)
            {
                return;
            }
            reference = new TextReference($"e{++nextTextId}");
            textReferences.Add(text, reference);
        }

        bool needsDefinition = !reference.DefinedInMain || isErrorLevel && !reference.DefinedInError;
        reference.DefinedInMain = true;
        if (isErrorLevel)
        {
            reference.DefinedInError = true;
        }

        jsonObject[$"{actualPropertyName}Ref"] = reference.Id;
        if (!needsDefinition)
        {
            jsonObject.Remove(actualPropertyName);
        }
    }

    private static string? FindPropertyName(JsonObject jsonObject, string propertyName)
    {
        return jsonObject.Select(property => property.Key)
            .FirstOrDefault(key => key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetString(JsonNode? node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue(out string? text))
        {
            value = text ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private sealed class RoomReference(string id, string nickName)
    {
        public string Id { get; } = id;
        public string NickName { get; set; } = nickName;
        public bool DefinedInMain { get; set; }
        public bool DefinedInError { get; set; }
    }

    private sealed class TextReference(string id)
    {
        public string Id { get; } = id;
        public bool DefinedInMain { get; set; }
        public bool DefinedInError { get; set; }
    }
}
