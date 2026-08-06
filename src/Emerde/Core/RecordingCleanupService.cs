using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Emerde.Core;

internal static class DataRetentionUnitHelper
{
    public const int MaximumValue = 9999;

    public const int Days = 0;
    public const int Weeks = 1;
    public const int Months = 2;
    public const int Years = 3;

    public static int NormalizeUnit(int unitIndex)
    {
        return unitIndex is Days or Weeks or Months or Years ? unitIndex : Weeks;
    }

    public static TimeSpan ToTimeSpan(int value, int unitIndex)
    {
        int safeValue = Math.Clamp(value, 1, MaximumValue);
        return NormalizeUnit(unitIndex) switch
        {
            Years => TimeSpan.FromDays(safeValue * 365d),
            Months => TimeSpan.FromDays(safeValue * 30d),
            Weeks => TimeSpan.FromDays(safeValue * 7d),
            Days or _ => TimeSpan.FromDays(safeValue),
        };
    }
}

internal static class RecordingCleanupService
{
    private const int CleanupStateVersion = 1;
    private const int MaximumExpirationBatchSize = 32;
    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private static readonly object ScheduleLock = new();
    private static readonly PriorityQueue<ScheduledRecording, long> Schedule = new();
    private static readonly Dictionary<string, ScheduledRecording> ScheduledEntries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ScheduledRecording> TrackedDuringRebuild = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions StateJsonOptions = new() { WriteIndented = true };
    private static readonly System.Threading.Timer ExpirationTimer = new(
        _ => StartExpirationWorker(),
        null,
        Timeout.InfiniteTimeSpan,
        Timeout.InfiniteTimeSpan);
    private static readonly TimeSpan ProtectedRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumTimerDelay = TimeSpan.FromDays(1);
    private static int queuedRunRequested;
    private static int queuedWorkerRunning;
    private static int expirationWorkerRunning;
    private static bool scheduleRebuildActive;
    private static bool stateLoaded;
    private static bool embeddedMetadataMigrationCompleted;
    private static string? stateFilePathOverride;

    public static void QueueRun()
    {
        if (!Configurations.IsDataRetentionEnabled.Get())
        {
            PauseSchedule();
            return;
        }

        Interlocked.Exchange(ref queuedRunRequested, 1);
        StartQueuedWorker();
    }

    public static Task RunAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(GetConfiguredRoots(), cancellationToken);
    }

    internal static async Task RunAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default)
    {
        await RunGate.WaitAsync(cancellationToken);
        bool scheduleReplaced = false;
        bool scheduleRebuildStarted = false;
        try
        {
            if (!EnsureStateLoaded())
            {
                return;
            }
            if (!Configurations.IsDataRetentionEnabled.Get())
            {
                PauseSchedule();
                return;
            }

            BeginScheduleRebuild();
            scheduleRebuildStarted = true;
            CleanupScanResult scanResult = await Task.Run(
                () => ScanScheduledRecordings(roots, cancellationToken),
                cancellationToken);
            CompleteScheduleRebuild(scanResult.Recordings, scanResult.Completed);
            scheduleReplaced = true;
            await Task.Run(() => ProcessExpiredRecordings(cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppSessionLogger.WriteException(exception);
        }
        finally
        {
            if (scheduleRebuildStarted && !scheduleReplaced)
            {
                CancelScheduleRebuild();
            }
            RunGate.Release();
        }
    }

    private static void StartQueuedWorker()
    {
        if (Interlocked.CompareExchange(ref queuedWorkerRunning, 1, 0) == 0)
        {
            _ = Task.Run(ProcessQueuedRunsAsync);
        }
    }

    private static async Task ProcessQueuedRunsAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref queuedRunRequested, 0) != 0)
            {
                await RunAsync();
            }
        }
        finally
        {
            Interlocked.Exchange(ref queuedWorkerRunning, 0);
            if (Volatile.Read(ref queuedRunRequested) != 0)
            {
                StartQueuedWorker();
            }
        }
    }

    internal static void TrackFile(string path, VideoRecordingMetadata metadata)
    {
        if (!Configurations.IsDataRetentionEnabled.Get() || !File.Exists(path))
        {
            return;
        }

        try
        {
            if (!EnsureStateLoaded())
            {
                return;
            }
            FileInfo file = new(path);
            if (TryBuildScheduledRecording(file, metadata, GetConfiguredRetention(), out ScheduledRecording recording))
            {
                AddOrUpdate(recording);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppSessionLogger.WriteException(exception);
        }
    }

    private static CleanupScanResult ScanScheduledRecordings(IEnumerable<string> roots, CancellationToken cancellationToken)
    {
        if (!Configurations.IsDataRetentionEnabled.Get())
        {
            return new CleanupScanResult([], true);
        }

        TimeSpan retention = GetConfiguredRetention();
        bool allowEmbeddedMetadataProbe;
        lock (ScheduleLock)
        {
            allowEmbeddedMetadataProbe = !embeddedMetadataMigrationCompleted;
        }
        List<ScheduledRecording> recordings = [];
        bool completed = true;
        foreach (string root in roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                completed = false;
                continue;
            }

            foreach (string filePath in EnumerateFilesSafe(root, () => completed = false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryCreateScheduledRecording(filePath, retention, allowEmbeddedMetadataProbe, out ScheduledRecording recording))
                {
                    if (TryAcceptRebuiltRecording(recording, out ScheduledRecording accepted))
                    {
                        recordings.Add(accepted);
                    }
                }
            }
        }

        return new CleanupScanResult([.. recordings], completed);
    }

    private static bool TryAcceptRebuiltRecording(ScheduledRecording recording, out ScheduledRecording accepted)
    {
        lock (ScheduleLock)
        {
            if (!ScheduledEntries.TryGetValue(recording.Path, out ScheduledRecording? existing))
            {
                accepted = recording;
                return true;
            }

            bool unchanged = string.Equals(existing.FileIdentity, recording.FileIdentity, StringComparison.Ordinal)
                && existing.Length == recording.Length
                && existing.LastWriteTimeUtcTicks == recording.LastWriteTimeUtcTicks
                && string.Equals(existing.MetadataHash, recording.MetadataHash, StringComparison.Ordinal);
            if (unchanged)
            {
                accepted = existing.RequiresFreshMetadata
                    ? recording with { ExpiresAtUtc = DateTime.MaxValue, RetryCount = existing.RetryCount, RequiresFreshMetadata = true }
                    : recording with { RetryCount = existing.RetryCount };
                return true;
            }

            bool metadataChanged = !string.Equals(existing.MetadataHash, recording.MetadataHash, StringComparison.Ordinal);
            if (metadataChanged || VideoRecordingMetadataStore.HasAttachedMetadata(recording.Path))
            {
                accepted = recording;
                return true;
            }

            accepted = recording with { ExpiresAtUtc = DateTime.MaxValue, RetryCount = 0, RequiresFreshMetadata = true };
            return true;
        }
    }

    private static bool TryCreateScheduledRecording(
        string path,
        TimeSpan retention,
        bool allowEmbeddedMetadataProbe,
        out ScheduledRecording recording)
    {
        recording = null!;
        if (!MediaFileCatalog.IsMediaPath(path) || MediaFileCatalog.IsApplicationTemporaryPath(path))
        {
            return false;
        }

        try
        {
            FileInfo file = new(path);
            if (!file.Exists)
            {
                return false;
            }

            VideoRecordingMetadata metadata;
            if (VideoRecordingMetadataStore.HasValidMetadata(file))
            {
                metadata = VideoRecordingMetadataStore.Load(file);
            }
            else
            {
                if (!allowEmbeddedMetadataProbe
                    || !FfmpegMediaEngine.TryProbe(file.FullName, out FfmpegMediaProbeResult probe, out _)
                    || !probe.HasEmerdeMetadata
                    || !VideoRecordingMetadataStore.HasAnyMetadata(probe.Metadata)
                    || !VideoRecordingMetadataStore.WriteCompletedMetadata(file.FullName, probe.Metadata))
                {
                    return false;
                }
                metadata = probe.Metadata;
                file.Refresh();
            }

            return TryBuildScheduledRecording(file, metadata, retention, out recording);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppSessionLogger.WriteException(exception);
            return false;
        }
    }

    private static bool TryBuildScheduledRecording(
        FileInfo file,
        VideoRecordingMetadata metadata,
        TimeSpan retention,
        out ScheduledRecording recording)
    {
        recording = null!;
        if (!file.Exists || !TryGetFileIdentity(file.FullName, out string fileIdentity))
        {
            return false;
        }

        DateTime recordedAtUtc = metadata.RecordedAt > DateTime.MinValue
            ? metadata.RecordedAt.Kind == DateTimeKind.Utc
                ? metadata.RecordedAt
                : metadata.RecordedAt.ToUniversalTime()
            : file.LastWriteTimeUtc;
        recording = new ScheduledRecording(
            file.FullName,
            CreateRecordingId(file.FullName, metadata, recordedAtUtc),
            fileIdentity,
            file.Length,
            file.LastWriteTimeUtc.Ticks,
            CreateMetadataHash(metadata),
            recordedAtUtc,
            file.LastWriteTimeUtc,
            GetExpirationTime(recordedAtUtc, file.LastWriteTimeUtc, retention),
            0);
        return true;
    }

    private static void BeginScheduleRebuild()
    {
        lock (ScheduleLock)
        {
            scheduleRebuildActive = true;
            TrackedDuringRebuild.Clear();
        }
    }

    private static void CompleteScheduleRebuild(IEnumerable<ScheduledRecording> recordings, bool scanCompleted)
    {
        lock (ScheduleLock)
        {
            ScheduledRecording[] retainedRecordings = scanCompleted ? [] : [.. ScheduledEntries.Values];
            Schedule.Clear();
            ScheduledEntries.Clear();
            foreach (ScheduledRecording recording in retainedRecordings)
            {
                EnqueueLocked(recording);
            }
            foreach (ScheduledRecording recording in recordings)
            {
                EnqueueLocked(recording);
            }
            foreach (ScheduledRecording recording in TrackedDuringRebuild.Values)
            {
                EnqueueLocked(recording);
            }
            TrackedDuringRebuild.Clear();
            scheduleRebuildActive = false;
            embeddedMetadataMigrationCompleted = ResolveEmbeddedMetadataMigrationCompleted(
                embeddedMetadataMigrationCompleted,
                scanCompleted);
            SaveScheduleLocked();
            ScheduleNextTimerLocked();
        }
    }

    private static void CancelScheduleRebuild()
    {
        lock (ScheduleLock)
        {
            TrackedDuringRebuild.Clear();
            scheduleRebuildActive = false;
            ScheduleNextTimerLocked();
        }
    }

    private static void AddOrUpdate(ScheduledRecording recording)
    {
        lock (ScheduleLock)
        {
            AddOrUpdateLocked(recording);
            SaveScheduleLocked();
            ScheduleNextTimerLocked();
        }
    }

    private static void AddOrUpdateLocked(ScheduledRecording recording)
    {
        EnqueueLocked(recording);
        if (scheduleRebuildActive)
        {
            TrackedDuringRebuild[recording.Path] = recording;
        }
    }

    private static void EnqueueLocked(ScheduledRecording recording)
    {
        if (ScheduledEntries.TryGetValue(recording.Path, out ScheduledRecording? current)
            && current == recording)
        {
            return;
        }

        ScheduledEntries[recording.Path] = recording;
        Schedule.Enqueue(recording, recording.ExpiresAtUtc.Ticks);
    }

    private static void PauseSchedule()
    {
        lock (ScheduleLock)
        {
            ExpirationTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private static void StartExpirationWorker()
    {
        if (Interlocked.CompareExchange(ref expirationWorkerRunning, 1, 0) == 0)
        {
            _ = Task.Run(ProcessExpirationTimerAsync);
        }
    }

    private static async Task ProcessExpirationTimerAsync()
    {
        try
        {
            await RunGate.WaitAsync();
            try
            {
                await Task.Run(() => ProcessExpiredRecordings(CancellationToken.None));
            }
            finally
            {
                RunGate.Release();
            }
        }
        catch (Exception exception)
        {
            AppSessionLogger.WriteException(exception);
        }
        finally
        {
            Interlocked.Exchange(ref expirationWorkerRunning, 0);
            lock (ScheduleLock)
            {
                ScheduleNextTimerLocked();
            }
        }
    }

    private static void ProcessExpiredRecordings(CancellationToken cancellationToken)
    {
        if (!Configurations.IsDataRetentionEnabled.Get())
        {
            PauseSchedule();
            return;
        }

        string[] pendingSourcePatterns = RecordingRecoveryService.GetPendingSourcePatterns();
        string[] configuredRoots = GetConfiguredRoots();
        int deletedCount = 0;
        int processedCount = 0;
        bool scheduleChanged = false;
        while (processedCount < MaximumExpirationBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryTakeExpired(DateTime.UtcNow, out ScheduledRecording recording))
            {
                break;
            }

            processedCount++;
            scheduleChanged = true;
            try
            {
                if (!TryGetManagedRoot(recording.Path, configuredRoots, out string managedRoot))
                {
                    continue;
                }

                if (!Directory.Exists(managedRoot))
                {
                    AddOrUpdateWithoutSaving(recording with
                    {
                        ExpiresAtUtc = DateTime.UtcNow + FailureRetryDelay,
                        RetryCount = recording.RetryCount + 1,
                    });
                    continue;
                }

                FileInfo file = new(recording.Path);
                if (!file.Exists || !VideoRecordingMetadataStore.HasValidMetadata(file))
                {
                    continue;
                }

                if (MediaOperationRegistry.IsPathProtected(recording.Path)
                    || RecordingRecoveryService.IsPendingSourcePath(recording.Path, pendingSourcePatterns))
                {
                    AddOrUpdateWithoutSaving(recording with
                    {
                        ExpiresAtUtc = DateTime.UtcNow + ProtectedRetryDelay,
                        RetryCount = recording.RetryCount + 1,
                    });
                    continue;
                }

                if (!TryGetFileIdentity(recording.Path, out string currentIdentity)
                    || !string.Equals(currentIdentity, recording.FileIdentity, StringComparison.Ordinal)
                    || file.Length != recording.Length
                    || file.LastWriteTimeUtc.Ticks != recording.LastWriteTimeUtcTicks)
                {
                    continue;
                }

                VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(file);
                if (!string.Equals(CreateMetadataHash(metadata), recording.MetadataHash, StringComparison.Ordinal))
                {
                    continue;
                }

                DateTime currentExpiration = GetExpirationTime(metadata.RecordedAt, file.LastWriteTimeUtc, GetConfiguredRetention());
                if (currentExpiration > DateTime.UtcNow)
                {
                    AddOrUpdateWithoutSaving(recording with { ExpiresAtUtc = currentExpiration });
                    continue;
                }

                file.Delete();
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(recording.Path);
                RemoveEmptyParentDirectories(recording.Path, configuredRoots);
                deletedCount++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.Write($"cleanup deferred file {recording.Path}: {exception.Message}");
                AddOrUpdateWithoutSaving(recording with
                {
                    ExpiresAtUtc = DateTime.UtcNow + FailureRetryDelay,
                    RetryCount = recording.RetryCount + 1,
                });
            }
        }

        if (scheduleChanged)
        {
            lock (ScheduleLock)
            {
                SaveScheduleLocked();
                ScheduleNextTimerLocked();
            }
        }

        if (deletedCount > 0)
        {
            AppSessionLogger.Write($"cleanup deleted {deletedCount} expired recording files");
        }
    }

    private static void AddOrUpdateWithoutSaving(ScheduledRecording recording)
    {
        lock (ScheduleLock)
        {
            AddOrUpdateLocked(recording);
            ScheduleNextTimerLocked();
        }
    }

    private static bool TryTakeExpired(DateTime nowUtc, out ScheduledRecording recording)
    {
        lock (ScheduleLock)
        {
            RemoveSupersededEntriesLocked();
            if (!Schedule.TryPeek(out ScheduledRecording? candidate, out long priority)
                || candidate == null
                || priority > nowUtc.Ticks)
            {
                recording = null!;
                ScheduleNextTimerLocked();
                return false;
            }

            _ = Schedule.Dequeue();
            ScheduledEntries.Remove(candidate.Path);
            recording = candidate;
            return true;
        }
    }

    private static void RemoveSupersededEntriesLocked()
    {
        while (Schedule.TryPeek(out ScheduledRecording? candidate, out _)
            && candidate != null
            && (!ScheduledEntries.TryGetValue(candidate.Path, out ScheduledRecording? current)
                || current != candidate))
        {
            _ = Schedule.Dequeue();
        }
    }

    private static void ScheduleNextTimerLocked()
    {
        RemoveSupersededEntriesLocked();
        if (!Configurations.IsDataRetentionEnabled.Get()
            || !Schedule.TryPeek(out _, out long priority))
        {
            ExpirationTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        TimeSpan delay = new DateTime(priority, DateTimeKind.Utc) - DateTime.UtcNow;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }
        else if (delay > MaximumTimerDelay)
        {
            delay = MaximumTimerDelay;
        }
        ExpirationTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private static bool EnsureStateLoaded()
    {
        lock (ScheduleLock)
        {
            if (stateLoaded)
            {
                return true;
            }

            CleanupState? state = TryLoadState(GetStateFilePath(), out bool primaryRetryRequired);
            bool backupRetryRequired = false;
            if (!IsSupportedState(state))
            {
                state = TryLoadState(GetStateBackupPath(), out backupRetryRequired);
            }
            if (!IsSupportedState(state) && (primaryRetryRequired || backupRetryRequired))
            {
                return false;
            }

            stateLoaded = true;
            if (IsSupportedState(state))
            {
                embeddedMetadataMigrationCompleted = state!.EmbeddedMetadataMigrationCompleted;
                foreach (ScheduledRecording recording in state.Recordings!
                    .OfType<ScheduledRecording>()
                    .Where(IsValidPersistedRecording))
                {
                    EnqueueLocked(recording);
                }
            }
            ScheduleNextTimerLocked();
            return true;
        }
    }

    private static CleanupState? TryLoadState(string path, out bool retryRequired)
    {
        retryRequired = false;
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<CleanupState>(File.ReadAllText(path), StateJsonOptions)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            retryRequired = true;
            AppSessionLogger.WriteException(exception);
            return null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            AppSessionLogger.WriteException(exception);
            return null;
        }
    }

    private static bool IsValidPersistedRecording(ScheduledRecording recording)
    {
        return !string.IsNullOrWhiteSpace(recording.Path)
            && Path.IsPathFullyQualified(recording.Path)
            && !string.IsNullOrWhiteSpace(recording.RecordingId)
            && !string.IsNullOrWhiteSpace(recording.FileIdentity)
            && recording.ExpiresAtUtc.Kind == DateTimeKind.Utc;
    }

    private static bool IsSupportedState(CleanupState? state)
    {
        return state?.Version == CleanupStateVersion && state.Recordings != null;
    }

    private static void SaveScheduleLocked()
    {
        string statePath = GetStateFilePath();
        string temporaryPath = statePath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath) ?? AppPaths.CacheDirectory);
            CleanupState state = new(
                CleanupStateVersion,
                embeddedMetadataMigrationCompleted,
                ScheduledEntries.Values
                    .OrderBy(recording => recording.ExpiresAtUtc)
                    .ThenBy(recording => recording.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList());
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, StateJsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(statePath))
            {
                File.Replace(temporaryPath, statePath, GetStateBackupPath(), ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, statePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            AppSessionLogger.WriteException(exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(exception);
            }
        }
    }

    private static string GetStateFilePath()
    {
        return stateFilePathOverride ?? AppPaths.RecordingCleanupStateFilePath;
    }

    private static string GetStateBackupPath()
    {
        return GetStateFilePath() + ".bak";
    }

    private static string[] GetConfiguredRoots()
    {
        return MediaFileCatalog.GetConfiguredSaveFolders()
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetManagedRoot(string path, IEnumerable<string> configuredRoots, out string managedRoot)
    {
        managedRoot = configuredRoots
            .Where(root => RecordingRecoveryService.IsPathWithinRoot(path, root))
            .OrderByDescending(root => root.Length)
            .FirstOrDefault() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(managedRoot);
    }

    private static string CreateRecordingId(string path, VideoRecordingMetadata metadata, DateTime recordedAtUtc)
    {
        string value = string.Join(
            "\n",
            metadata.RoomUrl,
            metadata.Platform,
            recordedAtUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Path.GetFileName(path));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string CreateMetadataHash(VideoRecordingMetadata metadata)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(metadata, StateJsonOptions);
        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static bool TryGetFileIdentity(string path, out string identity)
    {
        identity = string.Empty;
        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (GetFileInformationByHandle(handle, out ByHandleFileInformation information))
            {
                ulong fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
                identity = $"{information.VolumeSerialNumber:X8}:{fileIndex:X16}";
                return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppSessionLogger.WriteException(exception);
        }

        return false;
    }

    internal static void ResetStateForTests(string stateFilePath)
    {
        lock (ScheduleLock)
        {
            ExpirationTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Schedule.Clear();
            ScheduledEntries.Clear();
            TrackedDuringRebuild.Clear();
            scheduleRebuildActive = false;
            stateLoaded = false;
            embeddedMetadataMigrationCompleted = false;
            stateFilePathOverride = stateFilePath;
            Interlocked.Exchange(ref queuedRunRequested, 0);
        }
    }

    internal static int GetScheduledEntryCountForTests()
    {
        _ = EnsureStateLoaded();
        lock (ScheduleLock)
        {
            return ScheduledEntries.Count;
        }
    }

    internal static bool IsAwaitingFreshMetadataForTests(string path)
    {
        _ = EnsureStateLoaded();
        lock (ScheduleLock)
        {
            return ScheduledEntries.TryGetValue(Path.GetFullPath(path), out ScheduledRecording? recording)
                && recording.RequiresFreshMetadata;
        }
    }

    private static TimeSpan GetConfiguredRetention()
    {
        return DataRetentionUnitHelper.ToTimeSpan(
            Configurations.DataRetentionValue.Get(),
            Configurations.DataRetentionUnit.Get());
    }

    internal static DateTime GetRetentionCutoff(DateTime now, TimeSpan retention)
    {
        TimeSpan available = now - DateTime.MinValue;
        return retention >= available ? DateTime.MinValue : now - retention;
    }

    internal static DateTime GetExpirationTime(DateTime recordedAt, DateTime lastWriteTimeUtc, TimeSpan retention)
    {
        DateTime baseTimeUtc = recordedAt > DateTime.MinValue
            ? recordedAt.Kind == DateTimeKind.Utc ? recordedAt : recordedAt.ToUniversalTime()
            : lastWriteTimeUtc.Kind == DateTimeKind.Utc ? lastWriteTimeUtc : lastWriteTimeUtc.ToUniversalTime();
        TimeSpan available = DateTime.MaxValue - baseTimeUtc;
        return retention >= available ? DateTime.MaxValue : baseTimeUtc + retention;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, Action markIncomplete)
    {
        Stack<string> directories = new();
        directories.Push(root);

        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            string[] files;
            string[] children;

            try
            {
                files = Directory.GetFiles(directory);
                children = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                markIncomplete();
                AppSessionLogger.Write($"cleanup skipped directory {directory}: {exception.Message}");
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            foreach (string child in children)
            {
                if (!TryResolveDirectoryTraversal(child, out bool shouldTraverse))
                {
                    markIncomplete();
                    continue;
                }

                if (shouldTraverse)
                {
                    directories.Push(child);
                }
            }
        }
    }

    internal static bool ShouldTraverseDirectory(string path)
    {
        return TryResolveDirectoryTraversal(path, out bool shouldTraverse) && shouldTraverse;
    }

    private static bool TryResolveDirectoryTraversal(string path, out bool shouldTraverse)
    {
        try
        {
            shouldTraverse = (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            shouldTraverse = false;
            return false;
        }
    }

    internal static bool ResolveEmbeddedMetadataMigrationCompleted(bool currentValue, bool scanCompleted)
    {
        return currentValue || scanCompleted;
    }

    private static void RemoveEmptyParentDirectories(string filePath, IReadOnlyCollection<string> configuredRoots)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            string? root = configuredRoots
                .Where(candidate => RecordingRecoveryService.IsPathWithinRoot(directory, candidate))
                .OrderByDescending(candidate => candidate.Length)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            string normalizedRoot = Path.TrimEndingDirectorySeparator(root);
            while (!string.Equals(Path.TrimEndingDirectorySeparator(directory), normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    break;
                }

                Directory.Delete(directory);
                directory = Path.GetDirectoryName(directory);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.Write($"cleanup skipped empty parent directory: {exception.Message}");
        }
    }

    private sealed record CleanupState(
        int Version,
        bool EmbeddedMetadataMigrationCompleted,
        List<ScheduledRecording> Recordings);

    private sealed record CleanupScanResult(ScheduledRecording[] Recordings, bool Completed);

    private sealed record ScheduledRecording(
        string Path,
        string RecordingId,
        string FileIdentity,
        long Length,
        long LastWriteTimeUtcTicks,
        string MetadataHash,
        DateTime RecordedAtUtc,
        DateTime FinalizedAtUtc,
        DateTime ExpiresAtUtc,
        int RetryCount,
        bool RequiresFreshMetadata = false);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);
}
