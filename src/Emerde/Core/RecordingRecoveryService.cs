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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly ConcurrentDictionary<string, RecoveryProcessingTask> ProcessingTasks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim PendingOptionsUpdateGate = new(1, 1);
    private static readonly SemaphoreSlim RecoveryOperationGate = new(2, 2);
    private static readonly object PendingMarkerMutationLock = new();
    private static readonly object StartupMaintenanceLock = new();
    private static readonly DateTime ProcessStartedAtUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private static Task? startupMaintenanceTask;

    public static string? Register(string sourcePattern, RoomRecordingOptions options, string roomUrl = "")
    {
        string? targetFormat = Recorder.GetTargetFormat(options.RecordFormat);
        if (string.IsNullOrWhiteSpace(sourcePattern) || string.IsNullOrWhiteSpace(targetFormat))
        {
            return null;
        }

        return Register(sourcePattern, targetFormat, options.IsRemoveTs, options.IsOptimizeAudio, mergeSessionParts: false, roomUrl);
    }

    internal static string? RegisterSessionParts(
        string sourcePattern,
        string targetFormat,
        bool removeSource,
        string roomUrl = "",
        bool optimizeAudio = false,
        bool mergeSessionParts = true,
        string segmentReason = "")
    {
        if (string.IsNullOrWhiteSpace(sourcePattern) || string.IsNullOrWhiteSpace(targetFormat))
        {
            return null;
        }

        return Register(sourcePattern, targetFormat, removeSource, optimizeAudio, mergeSessionParts, roomUrl, segmentReason);
    }

    private static string? Register(
        string sourcePattern,
        string targetFormat,
        bool removeSource,
        bool optimizeAudio,
        bool mergeSessionParts,
        string roomUrl,
        string segmentReason = "")
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(AppPaths.PendingRecordingsDirectory);
            string path = Path.Combine(AppPaths.PendingRecordingsDirectory, $"{Guid.NewGuid():N}.json");
            temporaryPath = path + ".tmp";
            PendingRecording item = new()
            {
                SourcePattern = sourcePattern,
                TargetFormat = targetFormat,
                RemoveSource = removeSource,
                OptimizeAudio = optimizeAudio,
                MergeSessionParts = mergeSessionParts,
                RoomUrl = roomUrl ?? string.Empty,
                SegmentReason = segmentReason ?? string.Empty,
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
            if (!item.MergeSessionParts && !IsSessionPattern(item.SourcePattern))
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
            return Save(path, item);
        }
        if (!string.Equals(item.TargetFormat, targetFormat, StringComparison.OrdinalIgnoreCase))
        {
            item.CompletedSources.Clear();
            item.CompletedTargetPath = string.Empty;
            string sourceFormat = Path.GetExtension(item.SourcePattern);
            if (item.MergeSessionParts
                && sourceFormat.Equals(targetFormat, StringComparison.OrdinalIgnoreCase)
                && IsUsableSource(item.IntermediateTargetPath))
            {
                item.CompletedTargetPath = item.IntermediateTargetPath;
                item.IntermediateTargetPath = string.Empty;
            }
        }
        item.TargetFormat = targetFormat;
        item.RemoveSource = options.IsRemoveTs;
        item.OptimizeAudio = options.IsOptimizeAudio;
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
            startupMaintenanceTask ??= Task.Run(RunStartupMaintenanceAsync);
        }
    }

    private static async Task RunStartupMaintenanceAsync()
    {
        try
        {
            DeleteIncompleteMarkers();
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }

        try
        {
            DeleteStaleTemporaryMediaFiles();
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }

        try
        {
            await ProcessPendingAsync(GetPendingPaths());
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }

        try
        {
            await RecordingCleanupService.RunAsync();
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
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

    public static async Task ProcessPendingAsync()
    {
        await ProcessPendingAsync(GetPendingPaths());
    }

    private static async Task ProcessPendingAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        Task[] tasks = paths.Select(path => ProcessWithConcurrencyLimitAsync(path, token)).ToArray();
        await Task.WhenAll(tasks);
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

    internal static Task QueueProcessAsync(IEnumerable<string> paths)
    {
        string[] queuedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (queuedPaths.Length == 0)
        {
            return Task.CompletedTask;
        }

        return Task.WhenAll(queuedPaths.Select(QueueSingleProcessAsync));
    }

    private static Task QueueSingleProcessAsync(string path)
    {
        PendingRecording? item = Load(path, out _, validateAllowedDirectory: false);
        string[] protectedPatterns = item == null
            ? []
            : GetProtectedPaths(item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CancellationTokenSource cancellation = new();
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
        RecoveryProcessingTask processing = ProcessingTasks.GetOrAdd(
            lockKey,
            _ => new RecoveryProcessingTask(taskToken => ProcessCoreAsync(path, taskToken)));
        using CancellationTokenRegistration registration = token.CanBeCanceled
            ? token.Register(static state => ((CancellationTokenSource)state!).Cancel(), processing.Cancellation)
            : default;
        Task processingTask = processing.Task.Value;
        try
        {
            await processingTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            AppSessionLogger.WriteException(e);
        }
        finally
        {
            if (ProcessingTasks.TryGetValue(lockKey, out RecoveryProcessingTask? current) && ReferenceEquals(current, processing))
            {
                _ = ProcessingTasks.TryRemove(lockKey, out _);
            }
        }
    }

    private static async Task ProcessCoreAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PendingRecording? item = Load(path, out string? invalidReason);
        if (item == null)
        {
            if (!string.IsNullOrWhiteSpace(invalidReason))
            {
                QuarantineInvalidMarker(path, invalidReason);
            }
            return;
        }

        string[] sourceFiles = GetSourceFiles(item.SourcePattern);
        VideoRecordingMetadata sourceMetadata = LoadFirstMetadata(sourceFiles);
        bool isStallSegment = item.SegmentReason.Equals(VideoRecordingMetadataStore.TimelineStallSegmentReason, StringComparison.Ordinal)
            || sourceMetadata.SegmentReason.Equals(VideoRecordingMetadataStore.TimelineStallSegmentReason, StringComparison.Ordinal);
        if (isStallSegment && item.MergeSessionParts && !IsUsableSource(item.CompletedTargetPath))
        {
            item.MergeSessionParts = false;
            item.SegmentReason = VideoRecordingMetadataStore.TimelineStallSegmentReason;
            if (!Save(path, item))
            {
                return;
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
                await PublishFinalizedMediaAsync(path, item, sourceFiles, sourceMetadata, token);
                DeleteMarker(path);
            }
            return;
        }

        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        bool completed = await ProcessSourcePatternAsync(
            item.SourcePattern,
            item.TargetFormat,
            item.RemoveSource,
            item.MergeSessionParts,
            completedTargetPath =>
            {
                item.CompletedTargetPath = completedTargetPath;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording completion state could not be saved");
                }
            },
            item.CompletedSources,
            (sourcePath, completedTargetPath) =>
            {
                item.CompletedSources[sourcePath] = completedTargetPath;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording source completion state could not be saved");
                }
            },
            item.IntermediateTargetPath,
            intermediateTargetPath =>
            {
                item.IntermediateTargetPath = intermediateTargetPath;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording intermediate state could not be saved");
                }
            },
            reservedTargetPath =>
            {
                item.CompletedTargetPath = reservedTargetPath;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording reserved target state could not be saved");
                }
            },
            (sourcePath, reservedTargetPath) =>
            {
                item.CompletedSources[sourcePath] = reservedTargetPath;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording reserved source target state could not be saved");
                }
            },
            reservedIntermediatePath =>
            {
                item.IntermediateTargetPath = reservedIntermediatePath;
                if (!Save(path, item))
                {
                    throw new IOException("pending recording reserved intermediate state could not be saved");
                }
            },
            operationCancellation,
            item.OptimizeAudio);
        if (completed)
        {
            await PublishFinalizedMediaAsync(path, item, sourceFiles, sourceMetadata, token);
            DeleteMarker(path);
            return;
        }

        if (GetSourceFiles(item.SourcePattern).Length == 0)
        {
            DeleteMarker(path);
        }
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
        bool optimizeAudio = false)
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
                    onTargetReserved: onMergeTargetReserved))
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
                        optimizeAudio);
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
                    onTargetReserved: onIntermediateTargetReserved);
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
                        optimizeAudio);
                }
                mergedSource = createdIntermediate!;
            }

            bool completed = await new Converter().ExecuteWithCompletionAsync(
                mergedSource,
                new ConverterOptions(targetFormat, optimizeAudio),
                onMergeCompleted ?? (_ => { }),
                tokenSource,
                onTargetReserved: onMergeTargetReserved);
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
                optimizeAudio);
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
            optimizeAudio);
    }

    private sealed class RecoveryProcessingTask
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
    }

    private static async Task<bool> ProcessSourcesIndividuallyAsync(
        IReadOnlyList<string> sources,
        string targetFormat,
        bool removeSource,
        IReadOnlyDictionary<string, string>? completedSources,
        Action<string, string>? onSourceCompleted,
        Action<string, string>? onSourceTargetReserved,
        CancellationTokenSource? tokenSource,
        bool optimizeAudio)
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
                    onTargetReserved: reservedPath => onSourceTargetReserved?.Invoke(source, reservedPath));
                if (!converted || !IsUsableSource(createdTarget))
                {
                    return false;
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
        foreach ((string source, string target) in item.CompletedSources)
        {
            yield return source;
            yield return target;
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

        bool targetFormatAllowed = item.MergeSessionParts || IsSessionPattern(item.SourcePattern)
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

    private static void DeleteStaleTemporaryMediaFiles()
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
            try
            {
                foreach (string path in Directory.EnumerateFiles(root, ".emerde-*", options))
                {
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
        public string SourcePattern { get; set; } = string.Empty;

        public string TargetFormat { get; set; } = string.Empty;

        public bool RemoveSource { get; set; }

        public bool OptimizeAudio { get; set; }

        public bool MergeSessionParts { get; set; }

        public string RoomUrl { get; set; } = string.Empty;

        public string SegmentReason { get; set; } = string.Empty;

        public string CompletedTargetPath { get; set; } = string.Empty;

        public Dictionary<string, string> CompletedSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string IntermediateTargetPath { get; set; } = string.Empty;
    }
}

internal sealed record PendingOptionsUpdateResult(int Updated, int Cancelled, int Deferred);
