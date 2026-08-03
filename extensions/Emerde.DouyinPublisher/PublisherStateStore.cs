using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Emerde.Plugins;

namespace Emerde.DouyinPublisher;

internal sealed class PublisherStateStore
{
    private const int MaximumHandledEvents = 10000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
    private readonly string statePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateLock = new();
    private PublisherState state;

    public PublisherStateStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        statePath = Path.Combine(dataDirectory, "publisher-state.json");
        state = Load();
    }

    public event EventHandler? Changed;

    public PublisherState Snapshot()
    {
        lock (stateLock)
        {
            return state.Clone();
        }
    }

    public async Task SetRoomSelectedAsync(string roomUrl, bool selected, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            PublisherState next = Snapshot();
            if (selected)
            {
                next.SelectedRoomUrls.Add(roomUrl);
            }
            else
            {
                next.SelectedRoomUrls.Remove(roomUrl);
            }
            await SaveAsync(next, cancellationToken);
            lock (stateLock)
            {
                state = next;
            }
        }
        finally
        {
            gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask EnqueueAsync(ExtensionMediaFinalizedEvent payload, CancellationToken cancellationToken)
    {
        await EnqueueAsync(payload, null, cancellationToken);
    }

    public async ValueTask EnqueueAsync(
        ExtensionMediaFinalizedEvent payload,
        PublisherTaskOptions? taskOptions,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            PublisherState next = Snapshot();
            if (!next.HandledEventIds.Add(payload.EventId))
            {
                return;
            }
            TrimHandledEvents(next);
            if (next.SelectedRoomUrls.Contains(payload.RoomUrl)
                && File.Exists(payload.FilePath))
            {
                next.Queue.Add(PublisherQueueItem.From(payload, taskOptions));
            }
            await SaveAsync(next, cancellationToken);
            lock (stateLock)
            {
                state = next;
            }
        }
        finally
        {
            gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ManualEnqueueResult> EnqueueManualAsync(
        IReadOnlyList<ExtensionVideoFileInfo> files,
        CancellationToken cancellationToken = default,
        PublisherTaskOptions? taskOptions = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        int queued = 0;
        int duplicate = 0;
        int missing = 0;
        await gate.WaitAsync(cancellationToken);
        try
        {
            PublisherState next = Snapshot();
            foreach (ExtensionVideoFileInfo selectedFile in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    FileInfo file = new(Path.GetFullPath(selectedFile.FilePath));
                    if (!file.Exists)
                    {
                        missing++;
                        continue;
                    }
                    string eventId = CreateManualEventId(file);
                    if (next.HandledEventIds.Contains(eventId)
                        || next.Queue.Any(item => string.Equals(item.FilePath, file.FullName, StringComparison.OrdinalIgnoreCase)))
                    {
                        duplicate++;
                        continue;
                    }
                    next.HandledEventIds.Add(eventId);
                    next.Queue.Add(PublisherQueueItem.From(selectedFile, file, eventId, taskOptions));
                    queued++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    missing++;
                }
            }
            if (queued > 0)
            {
                TrimHandledEvents(next);
                await SaveAsync(next, cancellationToken);
                lock (stateLock)
                {
                    state = next;
                }
            }
        }
        finally
        {
            gate.Release();
        }
        if (queued > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return new ManualEnqueueResult(files.Count, queued, duplicate, missing);
    }

    public async Task RestoreInterruptedAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        bool changed = false;
        try
        {
            PublisherState next = Snapshot();
            for (int index = 0; index < next.Queue.Count; index++)
            {
                PublisherQueueItem item = next.Queue[index];
                if (item.Status is not (PublisherQueueStatus.Preparing or PublisherQueueStatus.Uploading))
                {
                    continue;
                }
                next.Queue[index] = item with
                {
                    Status = PublisherQueueStatus.Pending,
                    LastError = string.Empty,
                    NextAttemptAt = null,
                };
                changed = true;
            }
            if (changed)
            {
                await SaveAndReplaceAsync(next, cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<PublisherQueueItem?> TryStartNextAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        PublisherQueueItem? started = null;
        try
        {
            PublisherState next = Snapshot();
            if (next.Queue.Any(item => PublisherQueueStatus.IsWaiting(item.Status)))
            {
                return null;
            }
            int index = next.Queue.FindIndex(item => item.Status == PublisherQueueStatus.Pending
                || item.Status == PublisherQueueStatus.Retry
                    && (!item.NextAttemptAt.HasValue || item.NextAttemptAt.Value <= now));
            if (index < 0)
            {
                return null;
            }
            started = next.Queue[index] with
            {
                Status = PublisherQueueStatus.Preparing,
                Attempts = next.Queue[index].Attempts + 1,
                LastError = string.Empty,
                NextAttemptAt = null,
            };
            next.Queue[index] = started;
            await SaveAndReplaceAsync(next, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return started;
    }

    public Task MarkUploadingAsync(string eventId, CancellationToken cancellationToken = default)
    {
        return UpdateItemAsync(eventId, item => item with
        {
            Status = PublisherQueueStatus.Uploading,
            LastError = string.Empty,
        }, cancellationToken);
    }

    public Task MarkPublishedAsync(string eventId, string publishedUrl, CancellationToken cancellationToken = default)
    {
        return UpdateItemAsync(eventId, item => item with
        {
            Status = PublisherQueueStatus.Published,
            LastError = string.Empty,
            NextAttemptAt = null,
            PublishedAt = DateTimeOffset.UtcNow,
            PublishedUrl = publishedUrl,
        }, cancellationToken);
    }

    public Task MarkWaitingAsync(string eventId, string status, string message, CancellationToken cancellationToken = default)
    {
        if (!PublisherQueueStatus.IsWaiting(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        return UpdateItemAsync(eventId, item => item with
        {
            Status = status,
            LastError = message,
            NextAttemptAt = null,
        }, cancellationToken);
    }

    public Task MarkFailedAsync(string eventId, string message, CancellationToken cancellationToken = default)
    {
        return UpdateItemAsync(eventId, item => item with
        {
            Status = PublisherQueueStatus.Failed,
            LastError = message,
            NextAttemptAt = null,
        }, cancellationToken);
    }

    public Task MarkRetryAsync(
        string eventId,
        string message,
        int maximumAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return UpdateItemAsync(eventId, item => item.Attempts >= maximumAttempts
            ? item with
            {
                Status = PublisherQueueStatus.Failed,
                LastError = message,
                NextAttemptAt = null,
            }
            : item with
            {
                Status = PublisherQueueStatus.Retry,
                LastError = message,
                NextAttemptAt = now + PublisherRetryPolicy.GetDelay(item.Attempts),
            }, cancellationToken);
    }

    public async Task<int> ResumeBlockedAsync(CancellationToken cancellationToken = default)
    {
        int resumed = 0;
        await gate.WaitAsync(cancellationToken);
        try
        {
            PublisherState next = Snapshot();
            for (int index = 0; index < next.Queue.Count; index++)
            {
                PublisherQueueItem item = next.Queue[index];
                if (!PublisherQueueStatus.IsWaiting(item.Status) && item.Status != PublisherQueueStatus.Failed)
                {
                    continue;
                }
                next.Queue[index] = item with
                {
                    Status = PublisherQueueStatus.Pending,
                    Attempts = item.Status == PublisherQueueStatus.Failed ? 0 : item.Attempts,
                    LastError = string.Empty,
                    NextAttemptAt = null,
                };
                resumed++;
            }
            if (resumed > 0)
            {
                await SaveAndReplaceAsync(next, cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
        if (resumed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return resumed;
    }

    public TimeSpan GetNextWakeDelay(DateTimeOffset now)
    {
        PublisherState snapshot = Snapshot();
        DateTimeOffset? nextAttempt = snapshot.Queue
            .Where(item => item.Status == PublisherQueueStatus.Retry && item.NextAttemptAt.HasValue)
            .Select(item => item.NextAttemptAt)
            .Min();
        if (!nextAttempt.HasValue || nextAttempt <= now)
        {
            return TimeSpan.FromSeconds(30);
        }
        return nextAttempt.Value - now;
    }

    private async Task UpdateItemAsync(
        string eventId,
        Func<PublisherQueueItem, PublisherQueueItem> update,
        CancellationToken cancellationToken)
    {
        bool changed = false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            PublisherState next = Snapshot();
            int index = next.Queue.FindIndex(item => string.Equals(item.EventId, eventId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }
            next.Queue[index] = update(next.Queue[index]);
            await SaveAndReplaceAsync(next, cancellationToken);
            changed = true;
        }
        finally
        {
            gate.Release();
        }
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private PublisherState Load()
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return new PublisherState();
            }
            PublisherState loaded = JsonSerializer.Deserialize<PublisherState>(File.ReadAllText(statePath), JsonOptions) ?? new PublisherState();
            loaded.Normalize();
            return loaded;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            string invalidPath = statePath + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try
            {
                File.Move(statePath, invalidPath, overwrite: true);
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
            }
            return new PublisherState();
        }
    }

    private async Task SaveAsync(PublisherState value, CancellationToken cancellationToken)
    {
        string temporaryPath = statePath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, statePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private async Task SaveAndReplaceAsync(PublisherState value, CancellationToken cancellationToken)
    {
        await SaveAsync(value, cancellationToken);
        lock (stateLock)
        {
            state = value;
        }
    }

    private static void TrimHandledEvents(PublisherState value)
    {
        if (value.HandledEventIds.Count <= MaximumHandledEvents)
        {
            return;
        }
        HashSet<string> retained = value.Queue
            .Select(item => item.EventId)
            .Concat(value.HandledEventIds.Skip(value.HandledEventIds.Count - MaximumHandledEvents))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        value.HandledEventIds = retained;
    }

    private static string CreateManualEventId(FileInfo file)
    {
        string identity = $"{file.FullName.ToUpperInvariant()}\n{file.Length}\n{file.LastWriteTimeUtc.Ticks}";
        return $"manual:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()}";
    }
}

internal sealed record ManualEnqueueResult(int Requested, int Queued, int Duplicate, int Missing)
{
    public int Skipped => Duplicate + Missing;
}

internal sealed class PublisherState
{
    public HashSet<string> SelectedRoomUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> HandledEventIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PublisherQueueItem> Queue { get; set; } = [];

    public void Normalize()
    {
        SelectedRoomUrls = new HashSet<string>(SelectedRoomUrls ?? [], StringComparer.OrdinalIgnoreCase);
        HandledEventIds = new HashSet<string>(HandledEventIds ?? [], StringComparer.OrdinalIgnoreCase);
        Queue = (Queue ?? [])
            .Select(item => item with
            {
                Source = string.IsNullOrWhiteSpace(item.Source) ? "automatic" : item.Source,
                Status = PublisherQueueStatus.Normalize(item.Status),
                LastError = item.LastError ?? string.Empty,
                PublishedUrl = item.PublishedUrl ?? string.Empty,
                TaskOptions = item.TaskOptions?.Normalize(),
            })
            .ToList();
    }

    public PublisherState Clone()
    {
        return new PublisherState
        {
            SelectedRoomUrls = new HashSet<string>(SelectedRoomUrls, StringComparer.OrdinalIgnoreCase),
            HandledEventIds = new HashSet<string>(HandledEventIds, StringComparer.OrdinalIgnoreCase),
            Queue = Queue.Select(item => item with { }).ToList(),
        };
    }
}

internal sealed record PublisherQueueItem(
    string EventId,
    string RoomUrl,
    string NickName,
    string Title,
    string FilePath,
    long FileSize,
    DateTimeOffset QueuedAt,
    string Status,
    string Source = "automatic",
    int Attempts = 0,
    string LastError = "",
    DateTimeOffset? NextAttemptAt = null,
    DateTimeOffset? PublishedAt = null,
    string PublishedUrl = "",
    PublisherTaskOptions? TaskOptions = null)
{
    public static PublisherQueueItem From(
        ExtensionMediaFinalizedEvent payload,
        PublisherTaskOptions? taskOptions = null)
    {
        return new PublisherQueueItem(
            payload.EventId,
            payload.RoomUrl,
            payload.NickName,
            payload.Title,
            payload.FilePath,
            payload.FileSize,
            DateTimeOffset.UtcNow,
            PublisherQueueStatus.Pending,
            "automatic",
            TaskOptions: taskOptions?.Normalize());
    }

    public static PublisherQueueItem From(
        ExtensionVideoFileInfo selectedFile,
        FileInfo file,
        string eventId,
        PublisherTaskOptions? taskOptions)
    {
        return new PublisherQueueItem(
            eventId,
            selectedFile.RoomUrl,
            selectedFile.NickName,
            selectedFile.Title,
            file.FullName,
            file.Length,
            DateTimeOffset.UtcNow,
            PublisherQueueStatus.Pending,
            "manual",
            TaskOptions: taskOptions?.Normalize());
    }
}

internal static class PublisherQueueStatus
{
    public const string Pending = "pending";
    public const string Preparing = "preparing";
    public const string Uploading = "uploading";
    public const string WaitingLogin = "waiting_login";
    public const string WaitingUser = "waiting_user";
    public const string Published = "published";
    public const string Retry = "retry";
    public const string Failed = "failed";

    public static bool IsWaiting(string status)
    {
        return status is WaitingLogin or WaitingUser;
    }

    public static string Normalize(string? status)
    {
        return status is Pending or Preparing or Uploading or WaitingLogin or WaitingUser or Published or Retry or Failed
            ? status
            : Pending;
    }
}

internal static class PublisherRetryPolicy
{
    public static TimeSpan GetDelay(int attempts)
    {
        return attempts switch
        {
            <= 1 => TimeSpan.FromSeconds(15),
            2 => TimeSpan.FromMinutes(1),
            _ => TimeSpan.FromMinutes(5),
        };
    }
}
