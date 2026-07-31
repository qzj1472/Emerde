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
    private static readonly object QueueLock = new();
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
    private static string sessionId = string.Empty;
    private static DateTime currentLogDate = DateTime.MinValue;
    private static DateTime retryLogAfter = DateTime.MinValue;
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

        lock (LockObject)
        {
            if (worker is not null || writer is not null || queue is not null)
            {
                return;
            }

            retryLogAfter = DateTime.MinValue;
            sessionStartedAt = DateTime.Now;
            sessionProcessId = Environment.ProcessId;
            sessionId = Guid.NewGuid().ToString("N");
            ContextCompactor.Reset(sessionStartedAt.Date);
            string directory = AppPaths.LogsDirectory;
            if (!TryOpenWriters(directory, sessionStartedAt))
            {
                return;
            }
            lock (QueueLock)
            {
                queue = new BlockingCollection<LogLine>(new ConcurrentQueue<LogLine>(), QueueCapacity);
            }
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
            lock (QueueLock)
            {
                queue?.CompleteAdding();
            }
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
        StreamWriter? stoppingWriter;
        StreamWriter? stoppingErrorWriter;
        BlockingCollection<LogLine>? stoppingQueue;
        CancellationTokenSource? stoppingCancellation;
        lock (LockObject)
        {
            if (!ReferenceEquals(worker, stoppingWorker))
            {
                return;
            }

            stoppingWriter = writer;
            stoppingErrorWriter = errorWriter;
            lock (QueueLock)
            {
                stoppingQueue = queue;
                queue = null;
            }
            stoppingCancellation = workerCancellation;
            writer = null;
            errorWriter = null;
            worker = null;
            workerCancellation = null;
            sessionStartedAt = DateTime.MinValue;
            sessionProcessId = 0;
            sessionId = string.Empty;
            currentLogDate = DateTime.MinValue;
            retryLogAfter = DateTime.MinValue;
            CurrentFilePath = null;
            CurrentErrorFilePath = null;
            isAvailable = false;
        }

        DisposeSafely(stoppingWriter);
        DisposeSafely(stoppingErrorWriter);
        DisposeSafely(stoppingQueue);
        DisposeSafely(stoppingCancellation);
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
        try
        {
            Enqueue(BuildEvent(level, category, action, message, data));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            Debug.WriteLine(e);
        }
    }

    private static LogLine BuildEvent(string level, string category, string action, string message = "", object? data = null)
    {
        DateTime timestamp = DateTime.Now;
        JsonNode? dataNode = LogSanitizer.SanitizeData(data, JsonOptions);
        JsonObject payload = new()
        {
            ["timestamp"] = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            ["level"] = level,
            ["category"] = category,
            ["action"] = action,
            ["message"] = LogSanitizer.SanitizeText(message),
            ["threadId"] = Environment.CurrentManagedThreadId,
            ["data"] = dataNode,
        };
        return new LogLine(timestamp, level, payload);
    }

    private static void Enqueue(LogLine line)
    {
        lock (QueueLock)
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

                if (IsDiagnosticLevel(line.Level) && TryMakeRoomForDiagnostic(currentQueue))
                {
                    _ = currentQueue.TryAdd(line);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static bool TryMakeRoomForDiagnostic(BlockingCollection<LogLine> currentQueue)
    {
        List<LogLine> diagnosticLines = [];
        bool removedNormal = false;
        while (diagnosticLines.Count < 128 && currentQueue.TryTake(out LogLine? queuedLine))
        {
            if (!IsDiagnosticLevel(queuedLine.Level))
            {
                removedNormal = true;
                break;
            }
            diagnosticLines.Add(queuedLine);
        }

        foreach (LogLine diagnosticLine in diagnosticLines)
        {
            _ = currentQueue.TryAdd(diagnosticLine);
        }
        return removedNormal;
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
                    ContextCompactor.CompactPayload(line.Payload, line.Level, line.Timestamp.Date);
                    string text = JsonSerializer.Serialize(line.Payload, JsonOptions);
                    writer?.WriteLine(text);

                    if (ShouldWriteToErrorLog(line.Level))
                    {
                        errorWriter?.WriteLine(text);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    DisableTemporarily(line.Timestamp, e);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool EnsureLogDate(DateTime timestamp)
    {
        if (retryLogAfter != DateTime.MinValue && DateTime.Now < retryLogAfter)
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

        string fallbackDirectory = FallbackLogsDirectory;
        if (!string.Equals(directory, fallbackDirectory, StringComparison.OrdinalIgnoreCase)
            && TryOpenWritersCore(fallbackDirectory, timestamp, out Exception? fallbackError))
        {
            return true;
        }

        DisableTemporarily(timestamp, primaryError ?? new IOException("No writable log directory is available."));
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
                sessionId,
                filePath,
                errorFilePath);
            newWriter.WriteLine(sessionHeader);
            newErrorWriter.WriteLine(sessionHeader);
        }
        catch
        {
            DisposeSafely(newWriter);
            DisposeSafely(newErrorWriter);
            throw;
        }

        StreamWriter? oldWriter = writer;
        StreamWriter? oldErrorWriter = errorWriter;
        writer = newWriter;
        errorWriter = newErrorWriter;
        CurrentFilePath = filePath;
        CurrentErrorFilePath = errorFilePath;
        currentLogDate = timestamp.Date;
        ContextCompactor.Reset(timestamp.Date);
        retryLogAfter = DateTime.MinValue;
        lastFailureMessage = null;
        isAvailable = true;
        DisposeSafely(oldWriter);
        DisposeSafely(oldErrorWriter);
    }

    internal static string BuildSessionHeader(
        DateTime startedAt,
        DateTime logDate,
        int processId,
        string sessionIdentifier,
        string filePath,
        string errorFilePath)
    {
        object payload = new
        {
            type = "session",
            schemaVersion = 5,
            application = AppConfig.PackName,
            version = AppConfig.Version,
            buildId = AppConfig.BuildId,
            buildConfiguration = AppConfig.BuildConfiguration,
            sessionId = sessionIdentifier,
            startedAt = startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            logDate = logDate.ToString("yyyy-MM-dd"),
            processId,
            file = filePath,
            errorFile = errorFilePath,
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static void DisableTemporarily(DateTime timestamp, Exception error)
    {
        Debug.WriteLine(error);
        StreamWriter? failedWriter = writer;
        StreamWriter? failedErrorWriter = errorWriter;
        writer = null;
        errorWriter = null;
        currentLogDate = DateTime.MinValue;
        retryLogAfter = DateTime.Now.AddSeconds(30);
        lastFailureMessage = $"{timestamp:yyyy-MM-dd HH:mm:ss} {error.GetType().Name}: {error.Message}";
        isAvailable = false;
        DisposeSafely(failedWriter);
        DisposeSafely(failedErrorWriter);
    }

    private static void DisposeSafely(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
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
        string sessionName = $"{startedAt:yyyyMMdd_HHmmss_fff}_{processId}";
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

    internal static string FallbackLogsDirectory => Path.Combine(Path.GetTempPath(), AppConfig.PackName, "logs");

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

    private sealed record LogLine(DateTime Timestamp, string Level, JsonObject Payload);
}

internal sealed class LogContextCompactor
{
    private const int MaximumTextReferences = 2048;
    private const int MaximumEventReferences = 1024;
    private const int MaximumDataShapeReferences = 2048;
    private readonly object syncRoot = new();
    private readonly Dictionary<string, RoomReference> roomReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextReference> textReferences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventReference> eventReferences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DataShapeReference> dataShapeReferences = new(StringComparer.Ordinal);
    private DateTime currentDate = DateTime.MinValue;
    private int nextRoomId;
    private int nextTextId;
    private int nextEventId;
    private int nextDataShapeId;

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

    public void CompactPayload(JsonObject payload, string level, DateTime logDate)
    {
        lock (syncRoot)
        {
            if (currentDate != logDate.Date)
            {
                ResetCore(logDate.Date);
            }

            bool isErrorLevel = AppSessionLogger.ShouldWriteToErrorLog(level);
            CompactEvent(payload, isErrorLevel);
            if (payload["data"] is JsonNode data)
            {
                CompactNode(data, isErrorLevel);
            }
            else
            {
                payload.Remove("data");
            }
            CompactDataShape(payload, isErrorLevel);
        }
    }

    private void ResetCore(DateTime logDate)
    {
        currentDate = logDate;
        roomReferences.Clear();
        textReferences.Clear();
        eventReferences.Clear();
        dataShapeReferences.Clear();
        nextRoomId = 0;
        nextTextId = 0;
        nextEventId = 0;
        nextDataShapeId = 0;
    }

    private void CompactEvent(JsonObject payload, bool isErrorLevel)
    {
        if (!TryGetString(payload["level"], out string level)
            || !TryGetString(payload["category"], out string category)
            || !TryGetString(payload["action"], out string action))
        {
            CompactRepeatedText(payload, "message", isErrorLevel);
            return;
        }

        _ = TryGetString(payload["message"], out string message);
        string key = string.Join('\u001f', level, category, action, message);
        if (!eventReferences.TryGetValue(key, out EventReference? reference))
        {
            if (eventReferences.Count >= MaximumEventReferences)
            {
                CompactRepeatedText(payload, "message", isErrorLevel);
                return;
            }

            reference = new EventReference($"v{++nextEventId}", level, category, action, message);
            eventReferences.Add(key, reference);
        }

        bool needsDefinition = !reference.DefinedInMain || isErrorLevel && !reference.DefinedInError;
        reference.DefinedInMain = true;
        if (isErrorLevel)
        {
            reference.DefinedInError = true;
        }

        payload.Remove("level");
        payload.Remove("category");
        payload.Remove("action");
        payload.Remove("message");
        payload["eventRef"] = reference.Id;
        if (needsDefinition)
        {
            JsonObject context = new()
            {
                ["level"] = reference.Level,
                ["category"] = reference.Category,
                ["action"] = reference.Action,
            };
            if (!string.IsNullOrWhiteSpace(reference.Message))
            {
                context["message"] = reference.Message;
            }
            payload["eventContext"] = context;
        }
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

        foreach (string nullProperty in jsonObject
            .Where(property => property.Value == null)
            .Select(property => property.Key)
            .ToArray())
        {
            jsonObject.Remove(nullProperty);
        }

        CompactRoom(jsonObject, isErrorLevel);
        CompactRepeatedText(jsonObject, "Message", isErrorLevel);
        CompactRepeatedText(jsonObject, "errorOutput", isErrorLevel);
        CompactRepeatedText(jsonObject, "stackTrace", isErrorLevel);
        CompactRepeatedText(jsonObject, "innerException", isErrorLevel);
        CompactRepeatedText(jsonObject, "resolverError", isErrorLevel);
        CompactRepeatedText(jsonObject, "FileName", isErrorLevel);
        CompactRepeatedText(jsonObject, "outputFileName", isErrorLevel);
        CompactRepeatedText(jsonObject, "sourceFileName", isErrorLevel);
        CompactRepeatedText(jsonObject, "targetFileName", isErrorLevel);
        CompactRepeatedText(jsonObject, "path", isErrorLevel);
        CompactRepeatedText(jsonObject, "quarantinePath", isErrorLevel);
        CompactRepeatedText(jsonObject, "configuredFolder", isErrorLevel);

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

    private void CompactDataShape(JsonObject payload, bool isErrorLevel)
    {
        if (!TryGetString(payload["eventRef"], out string eventRef)
            || payload["data"] is not JsonObject data
            || data.Count == 0)
        {
            return;
        }

        string[] fields = data.Select(property => property.Key).ToArray();
        string key = string.Join('\u001f', [eventRef, .. fields]);
        if (!dataShapeReferences.TryGetValue(key, out DataShapeReference? reference))
        {
            if (dataShapeReferences.Count >= MaximumDataShapeReferences)
            {
                return;
            }

            reference = new DataShapeReference($"d{++nextDataShapeId}", fields);
            dataShapeReferences.Add(key, reference);
        }

        bool needsDefinition = !reference.DefinedInMain || isErrorLevel && !reference.DefinedInError;
        reference.DefinedInMain = true;
        if (isErrorLevel)
        {
            reference.DefinedInError = true;
        }

        payload["dataRef"] = reference.Id;
        if (needsDefinition)
        {
            return;
        }

        JsonArray values = [];
        foreach (string field in reference.Fields)
        {
            JsonNode? value = data[field];
            data.Remove(field);
            values.Add(value);
        }
        payload["data"] = values;
    }

    private sealed class EventReference(string id, string level, string category, string action, string message)
    {
        public string Id { get; } = id;
        public string Level { get; } = level;
        public string Category { get; } = category;
        public string Action { get; } = action;
        public string Message { get; } = message;
        public bool DefinedInMain { get; set; }
        public bool DefinedInError { get; set; }
    }

    private sealed class DataShapeReference(string id, string[] fields)
    {
        public string Id { get; } = id;
        public string[] Fields { get; } = fields;
        public bool DefinedInMain { get; set; }
        public bool DefinedInError { get; set; }
    }
}
