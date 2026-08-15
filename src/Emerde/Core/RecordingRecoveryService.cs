using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Emerde.Extensions;
using Emerde.Plugins;

namespace Emerde.Core;

internal static class RecordingRecoveryService
{
    private const int MaxAutomaticRecoveryFailures = 3;
    private const int CurrentRecoveryPolicyVersion = 1;
    private const int PendingProcessingBatchSize = 8;
    private static readonly TimeSpan PendingDiscoveryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CompletedOutputProbeTimeout = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly ConcurrentDictionary<string, RecoveryProcessingTask> ProcessingTasks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SourceProcessingTask> SourceProcessingTasks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SourceProcessingTasksSync = new();
    private static readonly SemaphoreSlim PendingOptionsUpdateGate = new(1, 1);
    private static readonly SemaphoreSlim PendingDiscoveryGate = new(1, 1);
    private static readonly SemaphoreSlim RecoveryOperationGate = new(2, 2);
    private static readonly object PendingMarkerMutationLock = new();
    private static readonly object StartupMaintenanceLock = new();
    private static readonly DateTime ProcessStartedAtUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private static CancellationTokenSource maintenanceCancellation = new();
    private static bool maintenanceRestartRequested;
    private static Task? startupMaintenanceTask;
    private static Task? periodicDiscoveryTask;
    private static Task? maintenanceRestartTask;

    public static string? Register(string sourcePattern, RoomRecordingOptions options, string roomUrl = "", bool finalizeName = false)
    {
        string? targetFormat = Recorder.GetTargetFormat(options.RecordFormat);
        if (string.IsNullOrWhiteSpace(sourcePattern) || string.IsNullOrWhiteSpace(targetFormat))
        {
            return null;
        }

        return Register(sourcePattern, targetFormat, options.IsRemoveTs, options.IsOptimizeAudio, mergeSessionParts: false, roomUrl, fileNameRule: options.SaveFileNameCustomRule, finalizeName: finalizeName);
    }

    internal static string? RegisterSessionParts(
        string sourcePattern,
        string targetFormat,
        bool removeSource,
        string roomUrl = "",
        bool optimizeAudio = false,
        bool mergeSessionParts = true,
        string segmentReason = "",
        string fileNameRule = "",
        bool finalizeName = false)
    {
        if (string.IsNullOrWhiteSpace(sourcePattern) || string.IsNullOrWhiteSpace(targetFormat))
        {
            return null;
        }

        return Register(sourcePattern, targetFormat, removeSource, optimizeAudio, mergeSessionParts, roomUrl, segmentReason, fileNameRule, finalizeName);
    }

    private static string? Register(
        string sourcePattern,
        string targetFormat,
        bool removeSource,
        bool optimizeAudio,
        bool mergeSessionParts,
        string roomUrl,
        string segmentReason = "",
        string fileNameRule = "",
        bool finalizeName = false)
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(AppPaths.PendingRecordingsDirectory);
            string path = Path.Combine(AppPaths.PendingRecordingsDirectory, $"{Guid.NewGuid():N}.json");
            temporaryPath = path + ".tmp";
            PendingRecording item = new()
            {
                RecoveryPolicyVersion = CurrentRecoveryPolicyVersion,
                SourcePattern = sourcePattern,
                TargetFormat = targetFormat,
                RemoveSource = removeSource,
                OptimizeAudio = optimizeAudio,
                MergeSessionParts = mergeSessionParts,
                FinalizeOnly = !mergeSessionParts
                    && Path.GetExtension(sourcePattern).Equals(targetFormat, StringComparison.OrdinalIgnoreCase),
                RoomUrl = roomUrl ?? string.Empty,
                SegmentReason = segmentReason ?? string.Empty,
                FileNameRule = fileNameRule ?? string.Empty,
                FinalizeName = finalizeName,
            };
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(JsonSerializer.Serialize(item, JsonOptions));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path);
            temporaryPath = null;
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            return null;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                DeleteMarker(temporaryPath);
            }
        }
    }

    internal static bool UpdateOptions(string path, RoomRecordingOptions options, string? roomUrl = null)
    {
        lock (PendingMarkerMutationLock)
        {
            return UpdateOptionsCore(path, options, roomUrl);
        }
    }

    private static bool UpdateOptionsCore(string path, RoomRecordingOptions options, string? roomUrl)
    {
        string? targetFormat = Recorder.GetTargetFormat(options.RecordFormat);
        PendingRecording? item = Load(path, out _, validateAllowedDirectory: false);
        if (item == null)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(targetFormat))
        {
            if (!item.FinalizeOnly && !item.MergeSessionParts && !IsSessionPattern(item.SourcePattern))
            {
                DeleteMarker(path);
                return false;
            }

            targetFormat = Path.GetExtension(item.SourcePattern);
        }

        if (!string.IsNullOrWhiteSpace(roomUrl))
        {
            item.RoomUrl = roomUrl;
        }
        if (IsUsableSource(item.CompletedTargetPath))
        {
            ResetFailureState(item);
            return Save(path, item);
        }
        if (!string.Equals(item.TargetFormat, targetFormat, StringComparison.OrdinalIgnoreCase))
        {
            item.CompletedSources.Clear();
            item.CompletedTargetPath = string.Empty;
            item.ReservedCompletedSources.Clear();
            item.ReservedCompletedTargetPath = string.Empty;
            string sourceFormat = Path.GetExtension(item.SourcePattern);
            if (item.MergeSessionParts
                && sourceFormat.Equals(targetFormat, StringComparison.OrdinalIgnoreCase)
                && IsUsableSource(item.IntermediateTargetPath))
            {
                item.CompletedTargetPath = item.IntermediateTargetPath;
                item.IntermediateTargetPath = string.Empty;
                item.ReservedIntermediateTargetPath = string.Empty;
            }
        }
        item.TargetFormat = targetFormat;
        item.RemoveSource = options.IsRemoveTs;
        item.OptimizeAudio = options.IsOptimizeAudio;
        item.FileNameRule = options.SaveFileNameCustomRule;
        ResetFailureState(item);
        return Save(path, item);
    }

    internal static bool MarkSessionPartsAsStallSegments(string path)
    {
        lock (PendingMarkerMutationLock)
        {
            PendingRecording? item = Load(path, out _, validateAllowedDirectory: false);
            if (item == null || !IsSessionPattern(item.SourcePattern))
            {
                return false;
            }

            item.MergeSessionParts = false;
            item.SegmentReason = VideoRecordingMetadataStore.TimelineStallSegmentReason;
            return Save(path, item);
        }
    }

    public static void QueueRun()
    {
        lock (StartupMaintenanceLock)
        {
            if (maintenanceCancellation.IsCancellationRequested)
            {
                maintenanceRestartRequested = true;
                Task[] stoppingTasks = GetMaintenanceTasksLocked(includeRestartTask: false);
                if (stoppingTasks.Any(task => !task.IsCompleted))
                {
                    maintenanceRestartTask ??= Task.Run(() => RestartMaintenanceWhenStoppedAsync(stoppingTasks));
                    return;
                }

                RestartMaintenanceLocked();
                return;
            }

            StartMaintenanceLocked();
        }
    }

    public static void CancelMaintenance()
    {
        lock (StartupMaintenanceLock)
        {
            maintenanceRestartRequested = false;
            maintenanceCancellation.Cancel();
        }
    }

    public static async Task WaitForMaintenanceAsync(TimeSpan timeout)
    {
        Task[] tasks;
        lock (StartupMaintenanceLock)
        {
            tasks = GetMaintenanceTasksLocked(includeRestartTask: true);
        }
        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(timeout);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
        }
        catch (Exception exception)
        {
            AppSessionLogger.WriteException(exception);
        }
    }

    private static void StartMaintenanceLocked()
    {
        maintenanceRestartRequested = false;
        startupMaintenanceTask ??= Task.Run(() => RunStartupMaintenanceAsync(maintenanceCancellation.Token));
        Task initialMaintenanceTask = startupMaintenanceTask;
        periodicDiscoveryTask ??= Task.Run(() => RunPeriodicDiscoveryAsync(initialMaintenanceTask, maintenanceCancellation.Token));
    }

    private static async Task RestartMaintenanceWhenStoppedAsync(Task[] stoppingTasks)
    {
        try
        {
            await Task.WhenAll(stoppingTasks);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppSessionLogger.WriteException(exception);
        }

        lock (StartupMaintenanceLock)
        {
            maintenanceRestartTask = null;
            if (maintenanceRestartRequested)
            {
                RestartMaintenanceLocked();
            }
        }
    }

    private static void RestartMaintenanceLocked()
    {
        maintenanceCancellation.Dispose();
        maintenanceCancellation = new CancellationTokenSource();
        startupMaintenanceTask = null;
        periodicDiscoveryTask = null;
        StartMaintenanceLocked();
    }

    private static Task[] GetMaintenanceTasksLocked(bool includeRestartTask)
    {
        return (includeRestartTask
                ? new Task?[] { startupMaintenanceTask, periodicDiscoveryTask, maintenanceRestartTask }
                : new Task?[] { startupMaintenanceTask, periodicDiscoveryTask })
            .Where(task => task != null)
            .Cast<Task>()
            .Distinct()
            .ToArray();
    }

    private static async Task RunStartupMaintenanceAsync(CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            DeleteIncompleteMarkers();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }

        try
        {
            DeleteStaleTemporaryMediaFiles(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }

        try
        {
            await ProcessPendingAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }

        try
        {
            await RecordingCleanupService.RunAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    private static async Task RunPeriodicDiscoveryAsync(Task initialMaintenanceTask, CancellationToken token)
    {
        try
        {
            await initialMaintenanceTask.WaitAsync(token);
            while (true)
            {
                await Task.Delay(PendingDiscoveryInterval, token);
                try
                {
                    await ProcessPendingAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    AppSessionLogger.WriteException(exception);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private static void DeleteIncompleteMarkers()
    {
        if (!Directory.Exists(AppPaths.PendingRecordingsDirectory))
        {
            return;
        }

        try
        {
            foreach (string path in Directory.GetFiles(AppPaths.PendingRecordingsDirectory, "*.tmp", SearchOption.TopDirectoryOnly))
            {
                if (IsFromPreviousProcess(path))
                {
                    DeleteMarker(path);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    public static Task ProcessPendingAsync()
    {
        return ProcessPendingAsync(CancellationToken.None);
    }

    public static async Task ProcessPendingAsync(CancellationToken token)
    {
        await PendingDiscoveryGate.WaitAsync(token);
        try
        {
            await Task.Run(() => DiscoverUnregisteredRecordings(token), token);
        }
        finally
        {
            PendingDiscoveryGate.Release();
        }
        await ProcessPendingAsync(GetPendingPaths(), token);
    }

    private static void DiscoverUnregisteredRecordings(CancellationToken token)
    {
        string[] pendingPatterns = GetPendingSourcePatterns();
        EnumerationOptions enumerationOptions = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        Dictionary<string, List<string>> sourceGroups = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in MediaFileCatalog.GetConfiguredSaveFolders().Where(Directory.Exists))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (string source in Directory.EnumerateFiles(root, "*", enumerationOptions))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        string extension = Path.GetExtension(source);
                        if (!extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                            && !extension.Equals(".flv", StringComparison.OrdinalIgnoreCase)
                            || MediaOperationRegistry.IsPathProtected(source)
                            || IsPendingSourcePath(source, pendingPatterns))
                        {
                            continue;
                        }

                        FileInfo file = new(source);
                        if (file.Length <= 0 || !VideoRecordingMetadataStore.HasValidMetadata(file))
                        {
                            continue;
                        }

                        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(file);
                        string sourcePattern = ResolveRecoverySourcePattern(source, metadata);
                        if (!sourceGroups.TryGetValue(sourcePattern, out List<string>? group))
                        {
                            group = [];
                            sourceGroups.Add(sourcePattern, group);
                        }
                        group.Add(source);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        AppSessionLogger.WriteException(exception);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(exception);
            }
        }

        int discovered = 0;
        foreach ((string sourcePattern, List<string> sources) in sourceGroups)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(sources[0]));
                RoomRecordingOptions options = RoomRecordingSettings.GetCurrent(metadata.RoomUrl, RoomRecordingSettings.GetGlobal());
                string? targetFormat = Recorder.GetTargetFormat(options.RecordFormat);
                if (string.IsNullOrWhiteSpace(targetFormat) || HasCompletedOutput(sourcePattern, targetFormat))
                {
                    continue;
                }

                bool sessionPattern = IsSessionPattern(sourcePattern);
                bool isStallSegment = sources.Any(source =>
                    VideoRecordingMetadataStore.Load(new FileInfo(source)).SegmentReason.Equals(
                        VideoRecordingMetadataStore.TimelineStallSegmentReason,
                        StringComparison.Ordinal));
                string? markerPath = sessionPattern
                    ? RegisterSessionParts(
                        sourcePattern,
                        targetFormat,
                        options.IsRemoveTs,
                        metadata.RoomUrl,
                        options.IsOptimizeAudio,
                        mergeSessionParts: !isStallSegment,
                        segmentReason: isStallSegment ? VideoRecordingMetadataStore.TimelineStallSegmentReason : string.Empty)
                    : Register(sourcePattern, options, metadata.RoomUrl);
                if (!string.IsNullOrWhiteSpace(markerPath))
                {
                    discovered++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(exception);
            }
        }

        if (discovered > 0)
        {
            AppSessionLogger.Event("info", "recovery", "orphaned_recordings_discovered", "unprocessed recordings were added to the recovery queue", new { discovered });
        }
    }

    internal static string BuildRecoverySourcePattern(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        string extension = Path.GetExtension(sourcePath);
        string stem = Path.GetFileNameWithoutExtension(sourcePath);
        Match match = Regex.Match(stem, @"^(?<base>.+)_\d{3,}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        return match.Success
            ? Path.Combine(directory, match.Groups["base"].Value + "_%03d" + extension)
            : sourcePath;
    }

    internal static string ResolveRecoverySourcePattern(string sourcePath, VideoRecordingMetadata metadata)
    {
        string sourcePattern = BuildRecoverySourcePattern(sourcePath);
        if (!IsSessionPattern(sourcePattern))
        {
            return sourcePath;
        }

        string metadataFileName = Path.GetFileName(metadata.FileName);
        string sourceFileName = Path.GetFileName(sourcePath);
        string patternFileName = Path.GetFileName(sourcePattern);
        string sessionFileName = patternFileName.Replace("_%03d", string.Empty, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(metadataFileName))
        {
            if (metadataFileName.Equals(sourceFileName, StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath;
            }
            if (metadataFileName.Equals(sessionFileName, StringComparison.OrdinalIgnoreCase))
            {
                return sourcePattern;
            }
        }

        string initialSegment = sourcePattern.Replace("%03d", "000", StringComparison.Ordinal);
        return File.Exists(initialSegment) ? sourcePattern : sourcePath;
    }

    private static bool HasCompletedOutput(string sourcePattern, string targetFormat)
    {
        string expectedPath = IsSessionPattern(sourcePattern)
            ? Converter.BuildSessionTargetPath(sourcePattern, targetFormat)
            : Converter.BuildTargetPath([new FileInfo(sourcePattern)], targetFormat);
        string? directory = Path.GetDirectoryName(expectedPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        string expectedStem = Path.GetFileNameWithoutExtension(expectedPath);
        try
        {
            return Directory.EnumerateFiles(directory, "*" + targetFormat, SearchOption.TopDirectoryOnly)
                .Any(path =>
                {
                    string stem = Path.GetFileNameWithoutExtension(path);
                    return IsCompletedOutputStem(expectedStem, stem)
                        && IsCompletedMediaOutput(path);
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(exception);
            return false;
        }
    }

    internal static bool IsCompletedOutputStem(string expectedStem, string candidateStem)
    {
        if (candidateStem.Equals(expectedStem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string suffix = candidateStem.Length > expectedStem.Length
            ? candidateStem[expectedStem.Length..]
            : string.Empty;
        return suffix.Length > 1
            && suffix[0] == '_'
            && int.TryParse(suffix.AsSpan(1), out int collisionIndex)
            && collisionIndex >= 2
            && suffix.AsSpan(1).SequenceEqual(collisionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task ProcessPendingAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        string[] pendingPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string[] batch in pendingPaths.Chunk(PendingProcessingBatchSize))
        {
            token.ThrowIfCancellationRequested();
            await Task.WhenAll(batch.Select(path => ProcessWithConcurrencyLimitAsync(path, token)));
        }
    }

    private static async Task ProcessWithConcurrencyLimitAsync(string path, CancellationToken token)
    {
        await RecoveryOperationGate.WaitAsync(token);
        try
        {
            await ProcessAsync(path, token);
        }
        finally
        {
            RecoveryOperationGate.Release();
        }
    }

    internal static Task QueueProcessAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        string[] queuedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (queuedPaths.Length == 0)
        {
            return Task.CompletedTask;
        }

        return Task.WhenAll(queuedPaths.Select(path => QueueSingleProcessAsync(path, token)));
    }

    internal static Task QueuePendingProcessingAsync()
    {
        return QueuePendingProcessingBatchesAsync(GetPendingPaths(), GetMaintenanceToken());
    }

    private static async Task QueuePendingProcessingBatchesAsync(
        IEnumerable<string> paths,
        CancellationToken token)
    {
        foreach (string[] batch in paths.Chunk(PendingProcessingBatchSize))
        {
            token.ThrowIfCancellationRequested();
            await QueueProcessAsync(batch, token);
        }
    }

    private static CancellationToken GetMaintenanceToken()
    {
        lock (StartupMaintenanceLock)
        {
            return maintenanceCancellation.Token;
        }
    }

    private static Task QueueSingleProcessAsync(string path, CancellationToken token)
    {
        PendingRecording? item = Load(path, out _, validateAllowedDirectory: false);
        string[] protectedPatterns = item == null
            ? []
            : GetProtectedPaths(item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CancellationTokenSource cancellation = token.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(token)
            : new CancellationTokenSource();
        IDisposable operation = MediaOperationRegistry.Register(
            MediaOperationKind.Conversion,
            () => protectedPatterns,
            cancellation.Cancel);
        return Task.Run(async () =>
        {
            using (cancellation)
            using (operation)
            try
            {
                await ProcessWithConcurrencyLimitAsync(path, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
        });
    }

    private static string[] GetPendingPaths()
    {
        if (!Directory.Exists(AppPaths.PendingRecordingsDirectory))
        {
            return [];
        }

        try
        {
            return Directory.GetFiles(AppPaths.PendingRecordingsDirectory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            return [];
        }
    }

    public static async Task ProcessAsync(string path)
    {
        await ProcessAsync(path, CancellationToken.None);
    }

    private static async Task ProcessAsync(string path, CancellationToken token)
    {
        string lockKey;
        try
        {
            lockKey = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            AppSessionLogger.WriteException(e);
            return;
        }
        RecoveryProcessingTask candidate = new(taskToken => ProcessDeduplicatedAsync(path, taskToken));
        RecoveryProcessingTask processing = ProcessingTasks.GetOrAdd(lockKey, candidate);
        bool ownsProcessing = ReferenceEquals(candidate, processing);
        if (!ownsProcessing)
        {
            candidate.Dispose();
        }
        using CancellationTokenRegistration registration = ownsProcessing && token.CanBeCanceled
            ? token.Register(static state => ((RecoveryProcessingTask)state!).Cancel(), processing)
            : default;
        Task processingTask = processing.Task.Value;
        if (ownsProcessing)
        {
            _ = RemoveProcessingTaskWhenCompletedAsync(lockKey, processing, processingTask);
        }
        try
        {
            await processingTask.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            AppSessionLogger.WriteException(e);
            if (e is IOException or UnauthorizedAccessException)
            {
                RecordFailure(path, $"exception:{e.GetType().Name}");
            }
        }
    }

    private static async Task RemoveProcessingTaskWhenCompletedAsync(
        string lockKey,
        RecoveryProcessingTask processing,
        Task processingTask)
    {
        try
        {
            await processingTask;
        }
        catch
        {
        }
        finally
        {
            if (ProcessingTasks.TryGetValue(lockKey, out RecoveryProcessingTask? current)
                && ReferenceEquals(current, processing))
            {
                _ = ProcessingTasks.TryRemove(lockKey, out _);
            }
            processing.Dispose();
        }
    }

    private static async Task ProcessDeduplicatedAsync(string path, CancellationToken token)
    {
        PendingRecording? item = Load(path, out _);
        string[] sourceFiles = item == null ? [] : GetSourceFiles(item.SourcePattern);
        string[] sourceKeys = CreateSourceProcessingKeys(sourceFiles);
        if (sourceKeys.Length == 0)
        {
            _ = await ProcessCoreAsync(path, token);
            return;
        }

        string semanticKey = CreateRecoverySemanticKey(item!, sourceFiles);
        SourceProcessingTask candidate = new(
            sourceKeys,
            semanticKey,
            taskToken => ProcessCoreAsync(path, taskToken),
            token);
        SourceProcessingTask processing;
        bool ownsProcessing;
        lock (SourceProcessingTasksSync)
        {
            processing = sourceKeys
                .Select(sourceKey => SourceProcessingTasks.GetValueOrDefault(sourceKey))
                .FirstOrDefault(task => task != null)
                ?? candidate;
            ownsProcessing = ReferenceEquals(candidate, processing);
            if (ownsProcessing)
            {
                foreach (string sourceKey in sourceKeys)
                {
                    SourceProcessingTasks[sourceKey] = candidate;
                }
            }
        }
        if (ownsProcessing)
        {
            try
            {
                _ = await processing.Task.Value;
            }
            finally
            {
                lock (SourceProcessingTasksSync)
                {
                    foreach (string sourceKey in sourceKeys)
                    {
                        if (SourceProcessingTasks.TryGetValue(sourceKey, out SourceProcessingTask? current)
                            && ReferenceEquals(current, processing))
                        {
                            _ = SourceProcessingTasks.Remove(sourceKey);
                        }
                    }
                }
            }
            return;
        }

        bool completed = await processing.Task.Value.WaitAsync(token);
        if (completed
            && semanticKey.Equals(processing.SemanticKey, StringComparison.Ordinal)
            && sourceKeys.Order().SequenceEqual(processing.SourceKeys.Order(), StringComparer.OrdinalIgnoreCase))
        {
            DeleteMarker(path);
            AppSessionLogger.Event("info", "recovery", "duplicate_recovery_coalesced", "duplicate recovery marker reused the completed source operation", new
            {
                path,
                sourceFiles,
            });
            return;
        }

        await ProcessDeduplicatedAsync(path, token);
    }

    internal static string[] CreateSourceProcessingKeys(IEnumerable<string> sourceFiles)
    {
        string[] normalizedSources;
        try
        {
            normalizedSources = sourceFiles
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [];
        }
        if (normalizedSources.Length == 0)
        {
            return [];
        }

        return normalizedSources
            .Select(source => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))))
            .ToArray();
    }

    internal static string CreateRecoverySemanticKey(
        IEnumerable<string> sourceFiles,
        string targetFormat,
        bool removeSource,
        bool optimizeAudio,
        bool mergeSessionParts)
    {
        string[] normalizedSources;
        try
        {
            normalizedSources = sourceFiles
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
        string payload = string.Join('\n', normalizedSources)
            + $"\n{targetFormat.ToLowerInvariant()}:{removeSource}:{optimizeAudio}:{mergeSessionParts}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string CreateRecoverySemanticKey(PendingRecording item, IEnumerable<string> sourceFiles)
    {
        return CreateRecoverySemanticKey(
            sourceFiles,
            item.TargetFormat,
            item.RemoveSource,
            item.OptimizeAudio,
            item.MergeSessionParts);
    }

    private sealed class SourceProcessingTask
    {
        public SourceProcessingTask(
            string[] sourceKeys,
            string semanticKey,
            Func<CancellationToken, Task<bool>> taskFactory,
            CancellationToken token)
        {
            SourceKeys = sourceKeys;
            SemanticKey = semanticKey;
            Task = new Lazy<Task<bool>>(
                () => taskFactory(token),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public string[] SourceKeys { get; }

        public string SemanticKey { get; }

        public Lazy<Task<bool>> Task { get; }
    }

    private static async Task<bool> ProcessCoreAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PendingRecording? item = Load(path, out string? invalidReason);
        if (item == null)
        {
            if (!string.IsNullOrWhiteSpace(invalidReason))
            {
                QuarantineInvalidMarker(path, invalidReason);
            }
            return false;
        }

        if (item.RecoveryPolicyVersion < CurrentRecoveryPolicyVersion)
        {
            item.RecoveryPolicyVersion = CurrentRecoveryPolicyVersion;
            ResetFailureState(item);
            if (!Save(path, item))
            {
                return false;
            }
        }

        string[] sourceFiles = GetSourceFiles(item.SourcePattern);
        bool recoveredReservedOutput = HasUsableReservedOutput(item);
        if (ReconcileReservedOutputs(item))
        {
            if (recoveredReservedOutput)
            {
                ResetFailureState(item);
            }

            if (!Save(path, item))
            {
                return false;
            }
        }
        string sourceStateFingerprint = CreateSourceStateFingerprint(sourceFiles);
        if (item.RetryBlocked)
        {
            bool canFinalizeExistingOutput = sourceFiles.Length == 0 && HasUsableOutput(item);
            if (!recoveredReservedOutput
                && !canFinalizeExistingOutput
                && string.Equals(item.BlockedSourceStateFingerprint, sourceStateFingerprint, StringComparison.Ordinal))
            {
                return false;
            }

            ResetFailureState(item);
            if (!Save(path, item))
            {
                return false;
            }
        }

        VideoRecordingMetadata sourceMetadata = LoadFirstMetadata(sourceFiles);
        bool isStallSegment = item.SegmentReason.Equals(VideoRecordingMetadataStore.TimelineStallSegmentReason, StringComparison.Ordinal)
            || sourceMetadata.SegmentReason.Equals(VideoRecordingMetadataStore.TimelineStallSegmentReason, StringComparison.Ordinal);
        if (isStallSegment && item.MergeSessionParts && !IsUsableSource(item.CompletedTargetPath))
        {
            item.MergeSessionParts = false;
            item.SegmentReason = VideoRecordingMetadataStore.TimelineStallSegmentReason;
            if (!Save(path, item))
            {
                return false;
            }
        }
        if (isStallSegment)
        {
            sourceMetadata.SegmentReason = VideoRecordingMetadataStore.TimelineStallSegmentReason;
            PersistSegmentReason(sourceFiles, sourceMetadata);
        }

        if (IsUsableSource(item.CompletedTargetPath))
        {
            if (!item.MergeSessionParts || DeleteSources(item.SourcePattern))
            {
                if (!TryFinalizeOutputs(path, item, sourceFiles, token, out string[] finalizedSourceFiles))
                {
                    return false;
                }
                await PublishFinalizedMediaAsync(path, item, finalizedSourceFiles, sourceMetadata, token);
                DeleteMarker(path);
                return true;
            }
            return false;
        }

        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        string? failureReason = null;
        bool completed = await ProcessSourcePatternAsync(
            item.SourcePattern,
            item.TargetFormat,
            item.RemoveSource,
            item.MergeSessionParts,
            completedTargetPath =>
            {
                item.CompletedTargetPath = completedTargetPath;
                item.ReservedCompletedTargetPath = string.Empty;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording completion state could not be saved");
                }
            },
            item.CompletedSources,
            (sourcePath, completedTargetPath) =>
            {
                item.CompletedSources[sourcePath] = completedTargetPath;
                item.ReservedCompletedSources.Remove(sourcePath);
                if (!Save(path, item))
                {
                    throw new IOException("pending recording source completion state could not be saved");
                }
            },
            item.IntermediateTargetPath,
            intermediateTargetPath =>
            {
                item.IntermediateTargetPath = intermediateTargetPath;
                item.ReservedIntermediateTargetPath = string.Empty;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording intermediate state could not be saved");
                }
            },
            reservedTargetPath =>
            {
                item.ReservedCompletedTargetPath = reservedTargetPath;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording reserved target state could not be saved");
                }
            },
            (sourcePath, reservedTargetPath) =>
            {
                item.ReservedCompletedSources[sourcePath] = reservedTargetPath;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording reserved source target state could not be saved");
                }
            },
            reservedIntermediatePath =>
            {
                item.ReservedIntermediateTargetPath = reservedIntermediatePath;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording reserved intermediate state could not be saved");
                }
            },
            operationCancellation,
            item.OptimizeAudio,
            reason => failureReason = SelectFailureReason(failureReason, reason));
        if (completed)
        {
            if (!TryFinalizeOutputs(path, item, sourceFiles, token, out string[] finalizedSourceFiles))
            {
                return false;
            }
            await PublishFinalizedMediaAsync(path, item, finalizedSourceFiles, sourceMetadata, token);
            DeleteMarker(path);
            return true;
        }

        string[] remainingSources = GetSourceFiles(item.SourcePattern);
        if (remainingSources.Length == 0)
        {
            DeleteMarker(path);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(failureReason))
        {
            item.FailureCount++;
            item.LastFailureReason = failureReason;
            item.LastFailureAtUtc = DateTimeOffset.UtcNow;
            item.RetryBlocked = IsTerminalRecoveryFailure(failureReason)
                || item.FailureCount >= MaxAutomaticRecoveryFailures;
            item.BlockedSourceStateFingerprint = item.RetryBlocked
                ? CreateSourceStateFingerprint(remainingSources)
                : string.Empty;
            if (item.RetryBlocked)
            {
                AppSessionLogger.Event("error", "recovery", "recovery_retry_blocked", "automatic recovery retry was paused after repeated conversion failures", new
                {
                    item.SourcePattern,
                    item.FailureCount,
                    item.LastFailureReason,
                });
            }
            _ = Save(path, item);
        }
        return false;
    }

    private static async Task PublishFinalizedMediaAsync(
        string markerPath,
        PendingRecording item,
        IReadOnlyCollection<string> sourceFiles,
        VideoRecordingMetadata sourceMetadata,
        CancellationToken cancellationToken)
    {
        ExtensionMediaFinalizedEvent[] events = CreateMediaFinalizedEvents(
            markerPath,
            item.SourcePattern,
            item.TargetFormat,
            item.RoomUrl,
            item.MergeSessionParts,
            item.CompletedTargetPath,
            item.CompletedSources,
            sourceFiles,
            sourceMetadata,
            DateTimeOffset.UtcNow);
        foreach (ExtensionMediaFinalizedEvent payload in events)
        {
            await ExtensionHostRuntime.PublishAsync(ExtensionEventNames.MediaFinalized, payload, cancellationToken);
        }
    }

    internal static ExtensionMediaFinalizedEvent[] CreateMediaFinalizedEvents(
        string markerPath,
        string sourcePattern,
        string targetFormat,
        string roomUrl,
        bool mergeSessionParts,
        string? completedTargetPath,
        IReadOnlyDictionary<string, string>? completedSources,
        IEnumerable<string> sourceFiles,
        VideoRecordingMetadata? sourceMetadata,
        DateTimeOffset finalizedAt)
    {
        string[] originals = sourceFiles
            .Where(IsUsableSource)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        List<string> finalPaths = [];
        if (IsUsableSource(completedTargetPath))
        {
            finalPaths.Add(completedTargetPath!);
        }
        if (completedSources != null)
        {
            finalPaths.AddRange(completedSources.Values.Where(IsUsableSource));
        }
        finalPaths.AddRange(originals.Where(path => Path.GetExtension(path).Equals(targetFormat, StringComparison.OrdinalIgnoreCase)));

        string recordingId = Path.GetFileNameWithoutExtension(markerPath);
        string sourceExtension = Path.GetExtension(sourcePattern);
        return finalPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(finalPath =>
            {
                string fullPath = Path.GetFullPath(finalPath);
                FileInfo file = new(fullPath);
                VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Merge(
                    VideoRecordingMetadataStore.Load(file),
                    sourceMetadata);
                string effectiveRoomUrl = string.IsNullOrWhiteSpace(roomUrl) ? metadata.RoomUrl : roomUrl;
                bool wasMerged = mergeSessionParts
                    && !string.IsNullOrWhiteSpace(completedTargetPath)
                    && fullPath.Equals(Path.GetFullPath(completedTargetPath), StringComparison.OrdinalIgnoreCase);
                return new ExtensionMediaFinalizedEvent(
                    CreateFinalizedEventId(recordingId, fullPath),
                    recordingId,
                    effectiveRoomUrl,
                    metadata.NickName,
                    metadata.Platform,
                    metadata.Title,
                    fullPath,
                    file.Length,
                    Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant(),
                    metadata.RecordedAt,
                    finalizedAt,
                    !sourceExtension.Equals(Path.GetExtension(fullPath), StringComparison.OrdinalIgnoreCase),
                    wasMerged);
            })
            .ToArray();
    }

    private static VideoRecordingMetadata LoadFirstMetadata(IEnumerable<string> sourceFiles)
    {
        foreach (string sourceFile in sourceFiles)
        {
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(sourceFile));
            if (VideoRecordingMetadataStore.HasAnyMetadata(metadata))
            {
                return metadata;
            }
        }
        return new VideoRecordingMetadata();
    }

    private static void PersistSegmentReason(IEnumerable<string> sourceFiles, VideoRecordingMetadata sourceMetadata)
    {
        foreach (string sourceFile in sourceFiles)
        {
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Merge(
                VideoRecordingMetadataStore.Load(new FileInfo(sourceFile)),
                sourceMetadata);
            metadata.SegmentReason = sourceMetadata.SegmentReason;
            _ = VideoRecordingMetadataStore.WriteCompletedMetadata(sourceFile, metadata);
        }
    }

    private static string CreateFinalizedEventId(string recordingId, string finalPath)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(finalPath.ToUpperInvariant()));
        return $"{recordingId}:{Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    internal static Task<bool> ProcessSourcePatternAsync(string sourcePattern, string targetFormat, bool removeSource)
    {
        return ProcessSourcePatternAsync(sourcePattern, targetFormat, removeSource, mergeSessionParts: false);
    }

    internal static async Task<bool> ProcessSourcePatternAsync(
        string sourcePattern,
        string targetFormat,
        bool removeSource,
        bool mergeSessionParts,
        Action<string>? onMergeCompleted = null,
        IReadOnlyDictionary<string, string>? completedSources = null,
        Action<string, string>? onSourceCompleted = null,
        string? intermediateTargetPath = null,
        Action<string>? onIntermediateCompleted = null,
        Action<string>? onMergeTargetReserved = null,
        Action<string, string>? onSourceTargetReserved = null,
        Action<string>? onIntermediateTargetReserved = null,
        CancellationTokenSource? tokenSource = null,
        bool optimizeAudio = false,
        Action<string>? onFailure = null)
    {
        string[] sources = GetSourceFiles(sourcePattern);
        if (sources.Length == 0)
        {
            return true;
        }

        if (mergeSessionParts)
        {
            string sourceFormat = Path.GetExtension(sourcePattern);
            bool targetIsSourceFormat = sourceFormat.Equals(targetFormat, StringComparison.OrdinalIgnoreCase);
            if (targetIsSourceFormat || removeSource)
            {
                if (!await new Converter().ExecuteSessionPartsAsync(
                    sourcePattern,
                    sources,
                    new ConverterOptions(targetFormat, optimizeAudio),
                    tokenSource,
                    onCompleted: onMergeCompleted,
                    onTargetReserved: onMergeTargetReserved,
                    onFailed: onFailure))
                {
                    if (targetIsSourceFormat)
                    {
                        return false;
                    }
                    return await ProcessSourcesIndividuallyAsync(
                        sources,
                        targetFormat,
                        removeSource,
                        completedSources,
                        onSourceCompleted,
                        onSourceTargetReserved,
                        tokenSource,
                        optimizeAudio,
                        onFailure,
                        sources.Length == 1 ? Converter.BuildSessionTargetPath(sourcePattern, targetFormat) : null);
                }

                CancellationToken token = tokenSource?.Token ?? CancellationToken.None;
                foreach (string source in sources)
                {
                    token.ThrowIfCancellationRequested();
                    File.Delete(source);
                    VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(source);
                }

                return true;
            }

            string mergedSource = IsUsableSource(intermediateTargetPath)
                ? intermediateTargetPath!
                : string.Empty;
            if (string.IsNullOrWhiteSpace(mergedSource))
            {
                string? createdIntermediate = null;
                bool merged = await new Converter().ExecuteSessionPartsAsync(
                    sourcePattern,
                    sources,
                    new ConverterOptions(sourceFormat, optimizeAudio),
                    tokenSource,
                    onCompleted: completedPath =>
                    {
                        createdIntermediate = completedPath;
                        onIntermediateCompleted?.Invoke(completedPath);
                    },
                    onTargetReserved: onIntermediateTargetReserved,
                    onFailed: onFailure);
                if (!merged || !IsUsableSource(createdIntermediate))
                {
                    return await ProcessSourcesIndividuallyAsync(
                        sources,
                        targetFormat,
                        removeSource,
                        completedSources,
                        onSourceCompleted,
                        onSourceTargetReserved,
                        tokenSource,
                        optimizeAudio,
                        onFailure,
                        sources.Length == 1 ? Converter.BuildSessionTargetPath(sourcePattern, targetFormat) : null);
                }
                mergedSource = createdIntermediate!;
            }

            bool completed = await new Converter().ExecuteWithCompletionAsync(
                mergedSource,
                new ConverterOptions(targetFormat, optimizeAudio),
                onMergeCompleted ?? (_ => { }),
                tokenSource,
                onTargetReserved: onMergeTargetReserved,
                onFailed: onFailure);
            if (completed)
            {
                CancellationToken token = tokenSource?.Token ?? CancellationToken.None;
                foreach (string source in sources)
                {
                    token.ThrowIfCancellationRequested();
                    File.Delete(source);
                    VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(source);
                }
            }
            if (completed)
            {
                return true;
            }
            bool fallbackCompleted = await ProcessSourcesIndividuallyAsync(
                sources,
                targetFormat,
                removeSource,
                completedSources,
                onSourceCompleted,
                onSourceTargetReserved,
                tokenSource,
                optimizeAudio,
                onFailure,
                sources.Length == 1 ? Converter.BuildSessionTargetPath(sourcePattern, targetFormat) : null);
            if (fallbackCompleted && IsUsableSource(mergedSource))
            {
                (tokenSource?.Token ?? CancellationToken.None).ThrowIfCancellationRequested();
                File.Delete(mergedSource);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(mergedSource);
            }
            return fallbackCompleted;
        }

        return await ProcessSourcesIndividuallyAsync(
            sources,
            targetFormat,
            removeSource,
            completedSources,
            onSourceCompleted,
            onSourceTargetReserved,
            tokenSource,
            optimizeAudio,
            onFailure,
            null);
    }

    private sealed class RecoveryProcessingTask : IDisposable
    {
        public RecoveryProcessingTask(Func<CancellationToken, Task> taskFactory)
        {
            Cancellation = new CancellationTokenSource();
            Task = new Lazy<Task>(
                () => taskFactory(Cancellation.Token),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public CancellationTokenSource Cancellation { get; }

        public Lazy<Task> Task { get; }

        public void Cancel()
        {
            Cancellation.Cancel();
        }

        public void Dispose()
        {
            Cancellation.Dispose();
        }
    }

    private static async Task<bool> ProcessSourcesIndividuallyAsync(
        IReadOnlyList<string> sources,
        string targetFormat,
        bool removeSource,
        IReadOnlyDictionary<string, string>? completedSources,
        Action<string, string>? onSourceCompleted,
        Action<string, string>? onSourceTargetReserved,
        CancellationTokenSource? tokenSource,
        bool optimizeAudio,
        Action<string>? onFailure,
        string? singleSourceTargetPath)
    {
        foreach (string source in sources)
        {
            CancellationToken token = tokenSource?.Token ?? CancellationToken.None;
            token.ThrowIfCancellationRequested();
            if (Path.GetExtension(source).Equals(targetFormat, StringComparison.OrdinalIgnoreCase))
            {
                if (!FfmpegMediaEngine.TryProbe(source, out _, out _, token))
                {
                    token.ThrowIfCancellationRequested();
                    onFailure?.Invoke("source_probe_failed");
                    return false;
                }
                continue;
            }
            string? completedTarget = completedSources?
                .FirstOrDefault(item => item.Key.Equals(source, StringComparison.OrdinalIgnoreCase))
                .Value;
            if (!IsUsableSource(completedTarget))
            {
                string? createdTarget = null;
                bool converted = await new Converter().ExecuteWithCompletionAsync(
                    source,
                    new ConverterOptions(targetFormat, optimizeAudio),
                    completedPath =>
                    {
                        onSourceCompleted?.Invoke(source, completedPath);
                        createdTarget = completedPath;
                    },
                    tokenSource,
                    onTargetReserved: reservedPath => onSourceTargetReserved?.Invoke(source, reservedPath),
                    onFailed: onFailure,
                    requestedTargetPath: singleSourceTargetPath);
                if (!converted || !IsUsableSource(createdTarget))
                {
                    createdTarget = await TryRepairSourceAsync(source, targetFormat, token, onFailure);
                    if (!IsUsableSource(createdTarget))
                    {
                        return false;
                    }
                    onSourceCompleted?.Invoke(source, createdTarget!);
                }
                completedTarget = createdTarget!;
            }

            if (removeSource)
            {
                token.ThrowIfCancellationRequested();
                File.Delete(source);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(source);
            }
        }

        return true;
    }

    private static async Task<string?> TryRepairSourceAsync(
        string source,
        string targetFormat,
        CancellationToken token,
        Action<string>? onFailure)
    {
        if (string.IsNullOrEmpty(VideoRepairService.NormalizeTargetExtension(targetFormat)))
        {
            return null;
        }

        VideoRepairResult result = await new VideoRepairService().RepairAsync(source, targetFormat, token);
        token.ThrowIfCancellationRequested();
        if (result.Status is VideoRepairStatus.Repaired or VideoRepairStatus.PartiallyRepaired
            && IsUsableSource(result.OutputPath))
        {
            return result.OutputPath;
        }

        string reason = string.IsNullOrWhiteSpace(result.Error)
            ? $"repair_failed:{result.Status}"
            : $"repair_failed:{result.Error}";
        onFailure?.Invoke(reason);
        return null;
    }

    internal static string[] GetSourceFiles(string sourcePattern)
    {
        if (string.IsNullOrWhiteSpace(sourcePattern))
        {
            return [];
        }

        if (!sourcePattern.Contains("%03d", StringComparison.Ordinal))
        {
            return IsUsableSource(sourcePattern) ? [sourcePattern] : [];
        }

        string? directory = Path.GetDirectoryName(sourcePattern);
        string pattern = Path.GetFileName(sourcePattern);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || string.IsNullOrWhiteSpace(pattern))
        {
            return [];
        }

        try
        {
            Regex regex = new(
                "^" + Regex.Escape(pattern).Replace("%03d", @"\d{3,}") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(250));
            string[] segments = MediaFileCatalog.OrderSegmentPaths(
                    Directory.EnumerateFiles(directory)
                        .Where(file => regex.IsMatch(Path.GetFileName(file)) && IsUsableSource(file)),
                    pattern)
                .ToArray();
            return IsUsableSource(sourcePattern) ? [sourcePattern, .. segments] : segments;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            AppSessionLogger.WriteException(e);
            return [];
        }
    }

    internal static bool IsPendingSourcePath(string path)
    {
        return IsPendingSourcePath(path, GetPendingSourcePatterns());
    }

    internal static string[] GetPendingSourcePatterns()
    {
        return GetPendingPaths()
            .Select(path => Load(path, out _, validateAllowedDirectory: false))
            .Where(item => item != null)
            .SelectMany(item => GetProtectedPaths(item!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> GetProtectedPaths(PendingRecording item)
    {
        yield return item.SourcePattern;
        foreach (string source in GetSourceFiles(item.SourcePattern))
        {
            yield return source;
        }
        if (!string.IsNullOrWhiteSpace(item.IntermediateTargetPath))
        {
            yield return item.IntermediateTargetPath;
        }
        if (!string.IsNullOrWhiteSpace(item.CompletedTargetPath))
        {
            yield return item.CompletedTargetPath;
        }
        if (!string.IsNullOrWhiteSpace(item.ReservedCompletedTargetPath))
        {
            yield return item.ReservedCompletedTargetPath;
        }
        foreach ((string source, string target) in item.CompletedSources)
        {
            yield return source;
            yield return target;
        }
        foreach ((string source, string target) in item.ReservedCompletedSources)
        {
            yield return source;
            yield return target;
        }
        if (!string.IsNullOrWhiteSpace(item.ReservedIntermediateTargetPath))
        {
            yield return item.ReservedIntermediateTargetPath;
        }
    }

    internal static bool IsPendingSourcePath(string path, IReadOnlyCollection<string> sourcePatterns)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return sourcePatterns.Any(pattern => MediaOperationRegistry.PathMatches(fullPath, pattern));
    }

    private static bool IsUsableSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            FileInfo file = new(path);
            return file.Exists && file.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFinalizeOutputs(
        string markerPath,
        PendingRecording item,
        IReadOnlyCollection<string> sourceFiles,
        CancellationToken token,
        out string[] finalizedSourceFiles)
    {
        if (!item.FinalizeName)
        {
            finalizedSourceFiles = sourceFiles.ToArray();
            return true;
        }

        Dictionary<string, string> renamed = new(StringComparer.OrdinalIgnoreCase);
        List<string> outputs = [];
        if (IsUsableSource(item.CompletedTargetPath))
        {
            outputs.Add(item.CompletedTargetPath);
        }
        outputs.AddRange(item.CompletedSources.Values.Where(IsUsableSource));
        outputs.AddRange(sourceFiles.Where(path =>
            IsUsableSource(path)
            && Path.GetExtension(path).Equals(item.TargetFormat, StringComparison.OrdinalIgnoreCase)));

        foreach (string output in outputs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            RecordingFinalizationResult result = RecordingFinalizationService.FinalizeFile(output, item.FileNameRule, token: token);
            if (!result.Success)
            {
                AppSessionLogger.Event("warn", "recovery", "recording_finalization_failed", result.Error, new { markerPath, output });
                finalizedSourceFiles = sourceFiles.ToArray();
                return false;
            }
            renamed[output] = result.Path;
            if (item.CompletedTargetPath.Equals(output, StringComparison.OrdinalIgnoreCase))
            {
                item.CompletedTargetPath = result.Path;
            }
            else
            {
                string? sourceKey = item.CompletedSources.FirstOrDefault(entry => entry.Value.Equals(output, StringComparison.OrdinalIgnoreCase)).Key;
                if (!string.IsNullOrWhiteSpace(sourceKey))
                {
                    item.CompletedSources[sourceKey] = result.Path;
                }
                else
                {
                    item.CompletedSources[output] = result.Path;
                }
            }
            if (!Save(markerPath, item))
            {
                RecordingFinalizationService.RollBackRename(result, output);
                finalizedSourceFiles = sourceFiles.ToArray();
                return false;
            }
        }
        finalizedSourceFiles = sourceFiles.Select(source => renamed.TryGetValue(source, out string? finalized) ? finalized : source).ToArray();
        return true;
    }

    internal static bool IsCompletedMediaOutput(string? path)
    {
        if (!IsUsableSource(path))
        {
            return false;
        }

        using CancellationTokenSource timeout = new(CompletedOutputProbeTimeout);
        try
        {
            if (FfmpegMediaEngine.TryProbe(path!, out FfmpegMediaProbeResult probe, out _, timeout.Token))
            {
                return probe.HasAudio || probe.HasVideo;
            }
            return timeout.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(exception);
            return false;
        }
    }

    internal static async Task<PendingOptionsUpdateResult> UpdatePendingOptionsForGlobalChangeAsync(RoomRecordingOptions options)
    {
        await PendingOptionsUpdateGate.WaitAsync();
        try
        {
            if (!string.Equals(Configurations.RecordFormat.Get(), options.RecordFormat, StringComparison.Ordinal))
            {
                return new PendingOptionsUpdateResult(0, 0, 0);
            }

            Room[] rooms = Configurations.Rooms.Get() ?? [];
            List<(string MarkerPath, string[] SourceFiles, string RoomUrl)> eligible = [];
            foreach (string path in GetPendingPaths())
            {
                PendingRecording? item = Load(path, out _, validateAllowedDirectory: false);
                if (item == null)
                {
                    continue;
                }

                string[] sourceFiles = GetSourceFiles(item.SourcePattern);
                string roomUrl = ResolveRoomUrl(item.RoomUrl, sourceFiles);
                if (ShouldUpdateForGlobalChange(roomUrl, rooms))
                {
                    eligible.Add((path, sourceFiles, roomUrl));
                }
            }

            string[] sourcePaths = eligible.SelectMany(item => item.SourceFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            int cancelled = MediaOperationRegistry.Cancel(MediaOperationKind.Conversion, sourcePaths);
            bool released = await MediaOperationRegistry.WaitForPathReleaseAsync(
                MediaOperationKind.Conversion,
                sourcePaths,
                TimeSpan.FromSeconds(10));
            released = released && await WaitForPendingProcessingAsync(
                eligible.Select(item => item.MarkerPath),
                TimeSpan.FromSeconds(10));
            if (!released)
            {
                AppSessionLogger.Event("warn", "recovery", "pending_options_wait_timeout", "conversion did not release source files before pending options update", new
                {
                    sourcePaths,
                    cancelled,
                });
                return new PendingOptionsUpdateResult(0, cancelled, eligible.Count);
            }

            if (!string.Equals(Configurations.RecordFormat.Get(), options.RecordFormat, StringComparison.Ordinal))
            {
                return new PendingOptionsUpdateResult(0, cancelled, 0);
            }

            int updated = 0;
            foreach ((string markerPath, _, string roomUrl) in eligible)
            {
                bool existed = File.Exists(markerPath);
                if (UpdateOptions(markerPath, options, roomUrl) || existed && !File.Exists(markerPath))
                {
                    updated++;
                }
            }

            return new PendingOptionsUpdateResult(updated, cancelled, 0);
        }
        finally
        {
            PendingOptionsUpdateGate.Release();
        }
    }

    internal static string ResolveRoomUrl(string? markerRoomUrl, IEnumerable<string> sourceFiles)
    {
        if (!string.IsNullOrWhiteSpace(markerRoomUrl))
        {
            return markerRoomUrl;
        }

        foreach (string sourceFile in sourceFiles)
        {
            try
            {
                string roomUrl = VideoRecordingMetadataStore.Load(new FileInfo(sourceFile)).RoomUrl;
                if (!string.IsNullOrWhiteSpace(roomUrl))
                {
                    return roomUrl;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(e);
            }
        }
        return string.Empty;
    }

    private static async Task<bool> WaitForPendingProcessingAsync(IEnumerable<string> markerPaths, TimeSpan timeout)
    {
        Task[] tasks = markerPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => ProcessingTasks.TryGetValue(path, out RecoveryProcessingTask? task) ? task.Task.Value : null)
            .Where(task => task != null)
            .Select(task => task!)
            .ToArray();
        if (tasks.Length == 0)
        {
            return true;
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch
        {
            return tasks.All(task => task.IsCompleted);
        }
    }

    internal static bool ShouldUpdateForGlobalChange(string? roomUrl, IEnumerable<Room> rooms)
    {
        return !string.IsNullOrWhiteSpace(roomUrl)
            && rooms.Any(room => room.IsFollowGlobalSettings
                && string.Equals(room.RoomUrl, roomUrl, StringComparison.OrdinalIgnoreCase));
    }

    private static PendingRecording? Load(string path, out string? invalidReason, bool validateAllowedDirectory = true)
    {
        invalidReason = null;
        try
        {
            PendingRecording? item = File.Exists(path)
                ? JsonSerializer.Deserialize<PendingRecording>(File.ReadAllText(path))
                : null;
            if (item != null)
            {
                item.CompletedSources = new Dictionary<string, string>(
                    item.CompletedSources ?? [],
                    StringComparer.OrdinalIgnoreCase);
                item.ReservedCompletedSources = new Dictionary<string, string>(
                    item.ReservedCompletedSources ?? [],
                    StringComparer.OrdinalIgnoreCase);
            }
            invalidReason = GetValidationError(item, validateAllowedDirectory);
            return invalidReason == null ? item : null;
        }
        catch (JsonException e)
        {
            AppSessionLogger.WriteException(e);
            invalidReason = "RecoveryJsonInvalid".Tr(e.Message);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            return null;
        }
    }

    private static string? GetValidationError(PendingRecording? item, bool validateAllowedDirectory)
    {
        if (item == null)
        {
            return "RecoveryMarkerEmpty".Tr();
        }
        if (string.IsNullOrWhiteSpace(item.SourcePattern) || !Path.IsPathFullyQualified(item.SourcePattern))
        {
            return "RecoverySourcePathAbsoluteInvalid".Tr();
        }
        if (!MediaFileCatalog.IsMediaPath(item.SourcePattern))
        {
            return "RecoverySourceFormatUnsupported".Tr();
        }
        if (item.SourcePattern.Contains('*') || item.SourcePattern.Contains('?'))
        {
            return "RecoverySourceWildcardInvalid".Tr();
        }

        string fileName = Path.GetFileName(item.SourcePattern);
        if (fileName.Replace("%03d", string.Empty, StringComparison.Ordinal).Contains('%'))
        {
            return "RecoverySegmentPlaceholderInvalid".Tr();
        }

        bool targetFormatAllowed = item.FinalizeOnly || item.MergeSessionParts || IsSessionPattern(item.SourcePattern)
            ? item.TargetFormat is ".mp4" or ".mkv" or ".ts" or ".flv"
            : item.TargetFormat is ".mp4" or ".mkv";
        if (!targetFormatAllowed)
        {
            return item.MergeSessionParts || IsSessionPattern(item.SourcePattern)
                ? "RecoveryTargetFormatSessionInvalid".Tr()
                : "RecoveryTargetFormatInvalid".Tr();
        }

        if (!string.IsNullOrWhiteSpace(item.CompletedTargetPath))
        {
            if (!Path.IsPathFullyQualified(item.CompletedTargetPath)
                || !MediaFileCatalog.IsMediaPath(item.CompletedTargetPath)
                || !Path.GetExtension(item.CompletedTargetPath).Equals(item.TargetFormat, StringComparison.OrdinalIgnoreCase))
            {
                return "RecoveryCompletedTargetInvalid".Tr();
            }

            try
            {
                string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(item.SourcePattern)) ?? string.Empty;
                string targetDirectory = Path.GetDirectoryName(Path.GetFullPath(item.CompletedTargetPath)) ?? string.Empty;
                if (!sourceDirectory.Equals(targetDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return "RecoveryCompletedTargetOutsideSource".Tr();
                }
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "RecoveryCompletedTargetPathInvalid".Tr(e.Message);
            }
        }

        string stateDirectory;
        try
        {
            stateDirectory = Path.GetDirectoryName(Path.GetFullPath(item.SourcePattern)) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(item.IntermediateTargetPath)
                && (!Path.IsPathFullyQualified(item.IntermediateTargetPath)
                    || !Path.GetExtension(item.IntermediateTargetPath).Equals(Path.GetExtension(item.SourcePattern), StringComparison.OrdinalIgnoreCase)
                    || !(Path.GetDirectoryName(Path.GetFullPath(item.IntermediateTargetPath)) ?? string.Empty).Equals(stateDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                return "RecoveryIntermediateTargetInvalid".Tr();
            }
            if (!string.IsNullOrWhiteSpace(item.ReservedCompletedTargetPath)
                && !IsValidStatePath(item.ReservedCompletedTargetPath, item.TargetFormat, stateDirectory))
            {
                return "RecoveryCompletedTargetInvalid".Tr();
            }
            if (!string.IsNullOrWhiteSpace(item.ReservedIntermediateTargetPath)
                && !IsValidStatePath(item.ReservedIntermediateTargetPath, Path.GetExtension(item.SourcePattern), stateDirectory))
            {
                return "RecoveryIntermediateTargetInvalid".Tr();
            }

            foreach ((string completedSource, string completedTarget) in item.CompletedSources)
            {
                if (!Path.IsPathFullyQualified(completedSource)
                    || !Path.IsPathFullyQualified(completedTarget)
                    || !(Path.GetDirectoryName(Path.GetFullPath(completedSource)) ?? string.Empty).Equals(stateDirectory, StringComparison.OrdinalIgnoreCase)
                    || !(Path.GetDirectoryName(Path.GetFullPath(completedTarget)) ?? string.Empty).Equals(stateDirectory, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetExtension(completedTarget).Equals(item.TargetFormat, StringComparison.OrdinalIgnoreCase))
                {
                    return "RecoveryCompletedSourceStateInvalid".Tr();
                }
            }
            foreach ((string reservedSource, string reservedTarget) in item.ReservedCompletedSources)
            {
                if (!Path.IsPathFullyQualified(reservedSource)
                    || !Path.GetExtension(reservedSource).Equals(Path.GetExtension(item.SourcePattern), StringComparison.OrdinalIgnoreCase)
                    || !(Path.GetDirectoryName(Path.GetFullPath(reservedSource)) ?? string.Empty).Equals(stateDirectory, StringComparison.OrdinalIgnoreCase)
                    || !IsValidStatePath(reservedTarget, item.TargetFormat, stateDirectory))
                {
                    return "RecoveryCompletedSourceStateInvalid".Tr();
                }
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "RecoveryStatePathInvalid".Tr(e.Message);
        }

        string sourcePath;
        try
        {
            sourcePath = Path.GetFullPath(item.SourcePattern);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "RecoverySourcePathInvalid".Tr(e.Message);
        }

        if (!validateAllowedDirectory)
        {
            return null;
        }

        return IsRecoverySourceAllowed(
            item.SourcePattern,
            sourcePath,
            MediaFileCatalog.GetConfiguredSaveFolders().Concat(SaveFolderHelper.GetFallbackSaveFolders()))
            ? null
            : "RecoverySourceOutsideConfiguredFolders".Tr();
    }

    internal static bool IsRecoverySourceAllowed(string sourcePattern, string sourcePath, IEnumerable<string> allowedRoots)
    {
        return allowedRoots.Any(root => IsPathWithinRoot(sourcePath, root))
            || GetSourceFiles(sourcePattern).Any(path => VideoRecordingMetadataStore.HasValidMetadata(new FileInfo(path)));
    }

    internal static bool IsPathWithinRoot(string path, string root)
    {
        return PathUtility.IsSameOrDescendant(path, root);
    }

    private static void DeleteStaleTemporaryMediaFiles(CancellationToken token)
    {
        EnumerationOptions options = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (string root in MediaFileCatalog.GetConfiguredSaveFolders()
            .Concat(SaveFolderHelper.GetFallbackSaveFolders())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (string path in Directory.EnumerateFiles(root, ".emerde-*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (IsFromPreviousProcess(path) && !MediaOperationRegistry.IsPathProtected(path))
                    {
                        DeleteMarker(path);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(e);
            }

            _ = VideoRecordingMetadataStore.DeleteOrphanedSidecars(root);
        }
    }

    private static bool IsFromPreviousProcess(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path) < ProcessStartedAtUtc;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void DeleteMarker(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    private static bool DeleteSources(string sourcePattern)
    {
        bool deleted = true;
        foreach (string source in GetSourceFiles(sourcePattern))
        {
            try
            {
                File.Delete(source);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(source);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                deleted = false;
                AppSessionLogger.WriteException(e);
            }
        }
        return deleted && GetSourceFiles(sourcePattern).Length == 0;
    }

    private static bool IsSessionPattern(string sourcePattern)
    {
        return sourcePattern.Contains("%03d", StringComparison.Ordinal);
    }

    internal static bool IsTerminalRecoveryFailure(string? failureReason)
    {
        return failureReason?.StartsWith("output_track_timeline_mismatch", StringComparison.Ordinal) == true
            || failureReason?.StartsWith("duration_mismatch", StringComparison.Ordinal) == true;
    }

    private static bool ReconcileReservedOutputs(PendingRecording item)
    {
        bool changed = false;
        if (!string.IsNullOrWhiteSpace(item.ReservedCompletedTargetPath))
        {
            if (IsUsableSource(item.ReservedCompletedTargetPath))
            {
                item.CompletedTargetPath = item.ReservedCompletedTargetPath;
            }

            item.ReservedCompletedTargetPath = string.Empty;
            changed = true;
        }

        foreach ((string source, string target) in item.ReservedCompletedSources.ToArray())
        {
            if (IsUsableSource(target))
            {
                item.CompletedSources[source] = target;
            }

            item.ReservedCompletedSources.Remove(source);
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(item.ReservedIntermediateTargetPath))
        {
            if (IsUsableSource(item.ReservedIntermediateTargetPath))
            {
                item.IntermediateTargetPath = item.ReservedIntermediateTargetPath;
            }

            item.ReservedIntermediateTargetPath = string.Empty;
            changed = true;
        }

        return changed;
    }

    private static bool HasUsableReservedOutput(PendingRecording item)
    {
        return IsUsableSource(item.ReservedCompletedTargetPath)
            || item.ReservedCompletedSources.Values.Any(IsUsableSource)
            || IsUsableSource(item.ReservedIntermediateTargetPath);
    }

    private static bool HasUsableOutput(PendingRecording item)
    {
        return IsUsableSource(item.CompletedTargetPath)
            || item.CompletedSources.Values.Any(IsUsableSource)
            || IsUsableSource(item.IntermediateTargetPath);
    }

    internal static string SelectFailureReason(string? current, string reason)
    {
        if (string.IsNullOrWhiteSpace(current)
            || IsTerminalRecoveryFailure(reason) && !IsTerminalRecoveryFailure(current))
        {
            return reason;
        }

        return current;
    }

    internal static string CreateSourceStateFingerprint(IEnumerable<string> sourceFiles)
    {
        string[] entries = sourceFiles
            .Select(path =>
            {
                try
                {
                    FileInfo file = new(path);
                    return $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return path;
                }
            })
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', entries))));
    }

    private static void RecordFailure(string path, string reason)
    {
        lock (PendingMarkerMutationLock)
        {
            PendingRecording? item = Load(path, out _, validateAllowedDirectory: false);
            if (item == null || item.RetryBlocked)
            {
                return;
            }

            string[] sourceFiles = GetSourceFiles(item.SourcePattern);
            item.FailureCount++;
            item.LastFailureReason = reason;
            item.LastFailureAtUtc = DateTimeOffset.UtcNow;
            item.RetryBlocked = IsTerminalRecoveryFailure(reason)
                || item.FailureCount >= MaxAutomaticRecoveryFailures;
            item.BlockedSourceStateFingerprint = item.RetryBlocked
                ? CreateSourceStateFingerprint(sourceFiles)
                : string.Empty;
            _ = Save(path, item);
        }
    }

    private static bool IsValidStatePath(string path, string extension, string stateDirectory)
    {
        return Path.IsPathFullyQualified(path)
            && MediaFileCatalog.IsMediaPath(path)
            && Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase)
            && (Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty).Equals(stateDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void ResetFailureState(PendingRecording item)
    {
        item.FailureCount = 0;
        item.LastFailureReason = string.Empty;
        item.LastFailureAtUtc = null;
        item.RetryBlocked = false;
        item.BlockedSourceStateFingerprint = string.Empty;
    }

    private static bool Save(string path, PendingRecording item)
    {
        lock (PendingMarkerMutationLock)
        {
            string directory = Path.GetDirectoryName(path) ?? AppPaths.PendingRecordingsDirectory;
            string temporaryPath = Path.Combine(directory, $".emerde-pending-{Guid.NewGuid():N}.tmp");
            try
            {
                using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new(stream, new System.Text.UTF8Encoding(false)))
                {
                    writer.Write(JsonSerializer.Serialize(item, JsonOptions));
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, path, overwrite: true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(e);
                return false;
            }
            finally
            {
                DeleteMarker(temporaryPath);
            }
        }
    }

    private static void QuarantineInvalidMarker(string path, string reason)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            string quarantinePath = path + ".invalid";
            for (int index = 2; File.Exists(quarantinePath); index++)
            {
                quarantinePath = path + $".invalid-{index}";
            }
            File.Move(path, quarantinePath);
            File.WriteAllText(quarantinePath + ".reason.txt", reason, new System.Text.UTF8Encoding(false));
            AppSessionLogger.Event("error", "recovery", "invalid_marker_quarantined", reason, new { path, quarantinePath });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    private sealed class PendingRecording
    {
        public int RecoveryPolicyVersion { get; set; }

        public string SourcePattern { get; set; } = string.Empty;

        public string TargetFormat { get; set; } = string.Empty;

        public bool RemoveSource { get; set; }

        public bool OptimizeAudio { get; set; }

        public bool MergeSessionParts { get; set; }

        public bool FinalizeOnly { get; set; }

        public string RoomUrl { get; set; } = string.Empty;

        public string SegmentReason { get; set; } = string.Empty;

        public string FileNameRule { get; set; } = string.Empty;

        public bool FinalizeName { get; set; }

        public string CompletedTargetPath { get; set; } = string.Empty;

        public Dictionary<string, string> CompletedSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string IntermediateTargetPath { get; set; } = string.Empty;

        public string ReservedCompletedTargetPath { get; set; } = string.Empty;

        public Dictionary<string, string> ReservedCompletedSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string ReservedIntermediateTargetPath { get; set; } = string.Empty;

        public int FailureCount { get; set; }

        public string LastFailureReason { get; set; } = string.Empty;

        public DateTimeOffset? LastFailureAtUtc { get; set; }

        public bool RetryBlocked { get; set; }

        public string BlockedSourceStateFingerprint { get; set; } = string.Empty;
    }
}

internal sealed record PendingOptionsUpdateResult(int Updated, int Cancelled, int Deferred);
