using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed record ConverterOptions(
    string TargetFormat,
    bool RemoveSource = false);

public sealed class Converter
{
    private const double MaximumTrackTimelineDifferenceSeconds = 2d;
    private const double MaximumRecoverableDurationDriftSeconds = 10d;
    private const double MinimumRecordingDurationToleranceSeconds = 15d;
    private const double RecordingDurationToleranceRatio = 0.0025d;
    private static readonly object TargetReservationLock = new();
    private static readonly HashSet<string> ReservedTargetPaths = new(StringComparer.OrdinalIgnoreCase);
    public static int ActiveConversionCount => MediaOperationRegistry.Count(MediaOperationKind.Conversion);

    public static bool HasActiveConversions => ActiveConversionCount > 0;

    public async Task<bool> ExecuteAsync(string sourceFileName, string targetFormat, CancellationTokenSource? tokenSource = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        ArgumentNullException.ThrowIfNull(targetFormat);

        return await ExecuteAsync([sourceFileName], CreateDefaultOptions(targetFormat), tokenSource);
    }

    public async Task<bool> ExecuteAsync(IReadOnlyList<string> sourceFileNames, string targetFormat, CancellationTokenSource? tokenSource = null)
    {
        return await ExecuteAsync(sourceFileNames, CreateDefaultOptions(targetFormat), tokenSource);
    }

    public async Task<bool> ExecuteAsync(string sourceFileName, ConverterOptions options, CancellationTokenSource? tokenSource = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        return await ExecuteAsync([sourceFileName], options, tokenSource);
    }

    public async Task<bool> ExecuteAsync(IReadOnlyList<string> sourceFileNames, ConverterOptions options, CancellationTokenSource? tokenSource = null)
    {
        return await ExecuteCoreAsync(sourceFileNames, options, tokenSource, sessionSourcePattern: null, allowSameFormat: false, null);
    }

    internal async Task<bool> ExecuteWithCompletionAsync(
        string sourceFileName,
        string targetFormat,
        Action<string> onCompleted,
        CancellationTokenSource? tokenSource = null,
        Action<string>? onTargetReserved = null,
        Action<string>? onFailed = null,
        string? requestedTargetPath = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        ArgumentNullException.ThrowIfNull(onCompleted);
        return await ExecuteWithCompletionAsync(sourceFileName, CreateDefaultOptions(targetFormat), onCompleted, tokenSource, onTargetReserved, onFailed, requestedTargetPath);
    }

    internal async Task<bool> ExecuteWithCompletionAsync(
        string sourceFileName,
        ConverterOptions options,
        Action<string> onCompleted,
        CancellationTokenSource? tokenSource = null,
        Action<string>? onTargetReserved = null,
        Action<string>? onFailed = null,
        string? requestedTargetPath = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        ArgumentNullException.ThrowIfNull(onCompleted);
        return await ExecuteCoreAsync([sourceFileName], options, tokenSource, sessionSourcePattern: null, allowSameFormat: false, onCompleted, onTargetReserved, onFailed, requestedTargetPath);
    }

    internal async Task<bool> ExecuteSessionPartsAsync(
        string sourcePattern,
        IReadOnlyList<string> sourceFileNames,
        string targetFormat,
        CancellationTokenSource? tokenSource = null,
        Action<string>? onCompleted = null,
        Action<string>? onTargetReserved = null,
        Action<string>? onFailed = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePattern);
        return await ExecuteSessionPartsAsync(sourcePattern, sourceFileNames, CreateDefaultOptions(targetFormat), tokenSource, onCompleted, onTargetReserved, onFailed);
    }

    internal async Task<bool> ExecuteSessionPartsAsync(
        string sourcePattern,
        IReadOnlyList<string> sourceFileNames,
        ConverterOptions options,
        CancellationTokenSource? tokenSource = null,
        Action<string>? onCompleted = null,
        Action<string>? onTargetReserved = null,
        Action<string>? onFailed = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePattern);
        return await ExecuteCoreAsync(sourceFileNames, options, tokenSource, sourcePattern, allowSameFormat: true, onCompleted, onTargetReserved, onFailed);
    }

    private async Task<bool> ExecuteCoreAsync(
        IReadOnlyList<string> sourceFileNames,
        ConverterOptions converterOptions,
        CancellationTokenSource? tokenSource,
        string? sessionSourcePattern,
        bool allowSameFormat,
        Action<string>? onCompleted,
        Action<string>? onTargetReserved = null,
        Action<string>? onFailed = null,
        string? requestedTargetPath = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileNames);
        ArgumentNullException.ThrowIfNull(converterOptions);

        string? normalizedTargetFormat = NormalizeTargetFormat(converterOptions.TargetFormat, allowSameFormat);
        if (normalizedTargetFormat == null)
        {
            onFailed?.Invoke("target_format_invalid");
            return false;
        }
        string targetFormat = normalizedTargetFormat;

        if (!FfmpegMediaEngine.IsAvailable)
        {
            AppSessionLogger.Event("error", "converter", "converter_missing", "ffmpeg native libraries were not found", new { sourceFileNames, targetFormat });
            onFailed?.Invoke("converter_missing");
            return false;
        }

        FileInfo[] sourceFileInfos = sourceFileNames
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new FileInfo(path))
            .ToArray();
        if (sourceFileInfos.Length == 0
            || sourceFileInfos.Any(file => !file.Exists)
            || sourceFileInfos.Any(file => !IsSupportedSourceFormat(file.Extension))
            || (!allowSameFormat
                && sourceFileInfos.Any(file => file.Extension.Equals(targetFormat, StringComparison.OrdinalIgnoreCase))))
        {
            onFailed?.Invoke("source_invalid");
            return false;
        }

        string requestedTargetFileName = requestedTargetPath
            ?? (string.IsNullOrWhiteSpace(sessionSourcePattern)
                ? BuildTargetPath(sourceFileInfos, targetFormat)
                : BuildSessionTargetPath(sessionSourcePattern, targetFormat));
        if (!Path.GetExtension(requestedTargetFileName).Equals(targetFormat, StringComparison.OrdinalIgnoreCase))
        {
            onFailed?.Invoke("target_path_invalid");
            return false;
        }
        if (sourceFileInfos.Any(file => file.FullName.Equals(requestedTargetFileName, StringComparison.OrdinalIgnoreCase)))
        {
            onFailed?.Invoke("target_overlaps_source");
            return false;
        }
        string targetFileName = ReserveAvailableTargetPath(requestedTargetFileName);
        string temporaryTargetFileName = GetTemporaryTargetPath(targetFileName);
        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.WithFileName(
            VideoRecordingMetadataStore.Load(sourceFileInfos[0]),
            Path.GetFileName(targetFileName));
        using CancellationTokenSource operationCancellation = tokenSource == null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(tokenSource.Token);
        using IDisposable? operation = MediaOperationRegistry.TryRegister(
            MediaOperationKind.Conversion,
            sourceFileInfos.Select(file => file.FullName).Concat([temporaryTargetFileName, targetFileName]),
            operationCancellation.Cancel,
            static kind => kind != MediaOperationKind.Conversion);
        if (operation == null)
        {
            ReleaseTargetPath(targetFileName);
            onFailed?.Invoke("source_busy");
            return false;
        }
        CancellationToken token = operationCancellation.Token;
        string conversionAttemptId = Guid.NewGuid().ToString("N");
        bool targetCreated = false;
        bool completionAcknowledged = false;
        try
        {
            onTargetReserved?.Invoke(targetFileName);
            Stopwatch queueWait = Stopwatch.StartNew();
            using IDisposable conversionSlot = await ConversionWorkScheduler.EnterAsync(token);
            queueWait.Stop();
            if (queueWait.ElapsedMilliseconds > 250)
            {
                AppSessionLogger.Event("info", "converter", "conversion_queue_released", "recording conversion left the work queue", new
                {
                    sourceFileNames,
                    targetFileName,
                    hasActiveRecorders = GlobalMonitor.HasActiveRecorders,
                    waitMilliseconds = queueWait.ElapsedMilliseconds,
                });
            }
            SourceProbeBatch probeBatch = await Task.Run(() => ProbeSources(sourceFileInfos, token), token);
            if (!probeBatch.Success)
            {
                MediaDiagnostics.LogProbeFailure(
                    "source_" + Path.GetExtension(probeBatch.InvalidSourcePath).TrimStart('.').ToLowerInvariant(),
                    probeBatch.InvalidSourcePath,
                    probeBatch.Error,
                    metadata,
                    metadata.RoomUrl,
                    targetFileName,
                    conversionAttemptId);
                AppSessionLogger.Event("error", "converter", "conversion_source_invalid", probeBatch.Error, new { sourceFileName = probeBatch.InvalidSourcePath });
                onFailed?.Invoke($"source_probe_failed:{probeBatch.Error}");
                return false;
            }
            FfmpegMediaProbeResult[] sourceProbes = probeBatch.Probes;
            for (int sourceIndex = 0; sourceIndex < sourceProbes.Length; sourceIndex++)
            {
                MediaDiagnostics.LogProbe(
                    MediaDiagnostics.GetFileStage(sourceFileInfos[sourceIndex].FullName),
                    sourceFileInfos[sourceIndex].FullName,
                    sourceProbes[sourceIndex],
                    metadata,
                    metadata.RoomUrl,
                    targetFileName,
                    segmentIndex: sourceIndex + 1,
                    conversionAttemptId: conversionAttemptId);
            }
            double[] sourceTimelineEndSeconds = sourceProbes
                .Select(GetSourceTimelineEndSeconds)
                .ToArray();
            double probedSourceDuration = sourceProbes.Sum(probe => Math.Max(0d, probe.DurationSeconds));
            double sourceAudioStartSeconds = sourceProbes.Sum(probe => Math.Max(0d, probe.AudioStartSeconds));
            double sourceVideoStartSeconds = sourceProbes.Sum(probe => Math.Max(0d, probe.VideoStartSeconds));
            double sourceStartMismatchSeconds = sourceProbes
                .Where(probe => probe.HasAudio && probe.HasVideo)
                .Select(probe => Math.Abs(probe.AudioStartSeconds - probe.VideoStartSeconds))
                .DefaultIfEmpty(0d)
                .Max();
            double sourceAudioEndSeconds = sourceProbes.Sum(GetAudioDurationSeconds);
            double sourceVideoEndSeconds = sourceProbes.Sum(GetVideoDurationSeconds);
            MergeSourceIssueMetadata(metadata, sourceFileInfos, sourceProbes);
            int sourceEndMismatchIndex = FindFirstSourceTimelineMismatchIndex(sourceProbes);
            bool sourceEndMismatch = sourceEndMismatchIndex >= 0;
            if (sourceStartMismatchSeconds > MaximumTrackTimelineDifferenceSeconds)
            {
                AppSessionLogger.Event("info", "converter", "conversion_source_start_offset_normalizing", "source stream start offsets will be normalized during remux and checked on the completed output", new
                {
                    sourceFileNames,
                    targetFileName,
                    sourceAudioStartSeconds,
                    sourceVideoStartSeconds,
                    sourceStartMismatchSeconds,
                    recordingSessionId = metadata.RecordingSessionId,
                });
            }
            if (sourceEndMismatch)
            {
                FfmpegMediaProbeResult mismatchedSourceProbe = sourceProbes[sourceEndMismatchIndex];
                double mismatchStart = GetTimelineIssueStartSeconds(mismatchedSourceProbe.AudioEndSeconds, mismatchedSourceProbe.VideoEndSeconds);
                double mismatchEnd = Math.Max(mismatchedSourceProbe.AudioEndSeconds, mismatchedSourceProbe.VideoEndSeconds);
                const string mismatchKind = "source_track_end_mismatch";
                VideoRecordingMetadataStore.AddIssueInterval(
                    metadata,
                    "timeline_mismatch",
                    mismatchStart,
                    mismatchEnd,
                    mismatchKind);
                AppSessionLogger.Event("error", "converter", "conversion_source_timeline_mismatch", "source audio and video timelines differ by more than the accepted tolerance; original recording segments were preserved", new
                {
                    sourceFileNames,
                    targetFileName,
                    conversionAttemptId,
                    sourceStartMismatchSeconds,
                    sourceAudioEndSeconds,
                    sourceVideoEndSeconds,
                    sourceSegmentIndex = sourceEndMismatchIndex + 1,
                    mismatchKind,
                    recordingSessionId = metadata.RecordingSessionId,
                });
                onFailed?.Invoke($"source_track_timeline_mismatch:{mismatchKind}");
                return false;
            }
            (bool preflightSucceeded, int failedPreflightIndex, string preflightReason) = await PreflightSourcesAsync(
                sourceFileInfos,
                sourceProbes,
                metadata,
                targetFormat,
                conversionAttemptId,
                token);
            if (!preflightSucceeded)
            {
                AppSessionLogger.Event("error", "converter", "conversion_source_preflight_failed", "a source did not pass the individual remux preflight", new
                {
                    sourceFileName = sourceFileInfos[failedPreflightIndex].FullName,
                    sourceIndex = failedPreflightIndex + 1,
                    targetFormat,
                    reason = preflightReason,
                    conversionAttemptId,
                    recordingSessionId = metadata.RecordingSessionId,
                });
                onFailed?.Invoke($"source_preflight_failed:{failedPreflightIndex + 1}:{preflightReason}");
                return false;
            }
            double recordingExpectedDuration = NormalizeRecordingExpectedDuration(
                GetRecordingExpectedDuration(metadata, sourceFileInfos),
                probedSourceDuration);
            string[] sourcePaths = sourceFileInfos.Select(file => file.FullName).ToArray();
            AppSessionLogger.Event("info", "converter", "conversion_starting", "recording conversion is starting", new
            {
                conversionAttemptId,
                sourceFileNames = sourcePaths,
                targetFileName,
                activeConversions = ActiveConversionCount,
                sourceTimelines = sourceProbes.Select((probe, index) => new
                {
                    segmentIndex = index + 1,
                    sourceFileName = sourcePaths[index],
                    probe.AudioStartSeconds,
                    probe.VideoStartSeconds,
                    probe.AudioEndSeconds,
                    probe.VideoEndSeconds,
                    timelineDifferenceSeconds = Math.Abs(probe.AudioEndSeconds - probe.VideoEndSeconds),
                }).ToArray(),
            });
            FfmpegMediaRunResult result = await Task.Run(
                () => FfmpegMediaEngine.RemuxFiles(
                    sourcePaths,
                    temporaryTargetFileName,
                    metadata,
                    token,
                    sourceTimelineEndSeconds: sourceTimelineEndSeconds),
                token);
            if (result.WasCanceled)
            {
                throw new OperationCanceledException(token);
            }

            ReportStorageFailureIfNeeded(result, sourcePaths);
            MediaDiagnostics.LogRun(
                "conversion",
                string.Join(";", sourcePaths),
                targetFileName,
                result,
                metadata.RoomUrl,
                "source_remux_attempt",
                metadata,
                conversionAttemptId);

            (bool succeeded, string validationError) = await ValidateConversionAsync(result, temporaryTargetFileName, sourceProbes, recordingExpectedDuration, token);
            ApplyValidationIssue(metadata, validationError);
            await Task.Run(
                () => MediaDiagnostics.LogProbeOrFailure(
                    "final_output",
                    temporaryTargetFileName,
                    metadata,
                    metadata.RoomUrl,
                    targetFileName,
                    succeeded ? string.Empty : validationError,
                    conversionAttemptId),
                token);
            await Task.Run(
                () => MediaDiagnostics.LogAudioContent(
                    "final_output",
                    temporaryTargetFileName,
                    metadata,
                    metadata.RoomUrl,
                    targetFileName),
                token);
            if (succeeded)
            {
                File.Move(temporaryTargetFileName, targetFileName, false);
                targetCreated = true;
                completionAcknowledged = true;
                _ = RecordingCoverStore.TryCopyOrCreateFinalizedCover(
                    sourcePaths,
                    targetFileName,
                    metadata,
                    recordingExpectedDuration,
                    token);
                if (!VideoRecordingMetadataStore.WriteCompletedMetadata(targetFileName, metadata))
                {
                    AppSessionLogger.Event("warn", "converter", "conversion_metadata_fallback_failed", "converted video metadata could not be stored", new { targetFileName });
                }
                onCompleted?.Invoke(targetFileName);
                if (converterOptions.RemoveSource)
                {
                    _ = TryDeleteSourceFiles(sourcePaths);
                }
            }
            AppSessionLogger.Event(succeeded ? "info" : "error", "converter", "conversion_finished", "recording conversion finished", new
            {
                sourceFileNames = sourceFileInfos.Select(file => file.FullName).ToArray(),
                targetFileName,
                conversionAttemptId,
                result.ExitCode,
                succeeded,
                result.ProcessedDurationSeconds,
                validationError = succeeded ? string.Empty : validationError,
                errorOutput = succeeded ? string.Empty : result.ErrorOutput,
            });
            if (!succeeded)
            {
                onFailed?.Invoke(string.IsNullOrWhiteSpace(validationError)
                    ? $"native_exit_code:{result.ExitCode}"
                    : validationError);
            }
            return succeeded;
        }
        catch (OperationCanceledException)
        {
            AppSessionLogger.Event("warn", "converter", "conversion_cancelled", "recording conversion was cancelled", new { sourceFileNames, targetFileName });
            throw;
        }
        catch (Exception e)
        {
            if (targetCreated && !completionAcknowledged)
            {
                DeleteTemporaryOutput(targetFileName);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFileName);
            }
            AppSessionLogger.WriteException(e);
            AppSessionLogger.Event("error", "converter", "conversion_failed", e.Message, new { sourceFileNames, targetFileName });
            onFailed?.Invoke($"exception:{e.GetType().Name}");
            return false;
        }
        finally
        {
            DeleteTemporaryOutput(temporaryTargetFileName);
            ReleaseTargetPath(targetFileName);
        }
    }

    internal static string? NormalizeTargetFormat(string targetFormat, bool allowSourceContainerFormats)
    {
        string normalized = targetFormat.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }
        if (normalized[0] != '.')
        {
            normalized = "." + normalized;
        }
        normalized = normalized.ToLowerInvariant();
        return normalized is ".mp4" or ".mkv"
            || allowSourceContainerFormats && normalized is ".ts" or ".flv"
                ? normalized
                : null;
    }

    internal static ConverterOptions CreateDefaultOptions(string targetFormat)
    {
        return new ConverterOptions(targetFormat);
    }

    private static bool IsCompletedVideoFormat(string extension)
    {
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryDeleteSourceFiles(IEnumerable<string> sourcePaths)
    {
        bool deleted = true;
        foreach (string sourcePath in sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                File.Delete(sourcePath);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(sourcePath);
                RecordingAssociatedAssets.Delete(sourcePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                deleted = false;
                AppSessionLogger.WriteException(exception);
            }
        }

        return deleted;
    }

    private static async Task<(bool Succeeded, string ValidationError)> ValidateConversionAsync(
        FfmpegMediaRunResult result,
        string temporaryTargetFileName,
        IReadOnlyList<FfmpegMediaProbeResult> sourceProbes,
        double recordingExpectedDuration,
        CancellationToken token,
        double maximumTimelineEndSeconds = 0d)
    {
        if (result.ExitCode != 0)
        {
            return (false, $"native_exit_code:{result.ExitCode}");
        }
        if (!result.HadMediaProgress)
        {
            return (false, "no_media_progress");
        }

        string validationError = await Task.Run(
            () => GetOutputValidationError(
                temporaryTargetFileName,
                sourceProbes,
                result.ProcessedDurationSeconds,
                recordingExpectedDuration,
                token,
                maximumTimelineEndSeconds,
                result.AudioEndSeconds,
                result.VideoEndSeconds),
            token);
        if (validationError.StartsWith("duration_mismatch_recoverable:", StringComparison.Ordinal))
        {
            return (true, validationError);
        }

        return (string.IsNullOrEmpty(validationError), validationError);
    }

    private static void MergeSourceIssueMetadata(
        VideoRecordingMetadata metadata,
        IReadOnlyList<FileInfo> sourceFileInfos,
        IReadOnlyList<FfmpegMediaProbeResult> sourceProbes)
    {
        RemoveProvisionalTimelineWarning(metadata);
        List<VideoRecordingIssueInterval> mergedIntervals = VideoRecordingMetadataStore.CloneIssueIntervals(metadata.IssueIntervals);
        string mergedIssues = metadata.MediaIssue ?? string.Empty;
        double offset = sourceProbes.Count > 0 ? GetSourceTimelineEndSeconds(sourceProbes[0]) : 0d;
        for (int sourceIndex = 1; sourceIndex < sourceFileInfos.Count; sourceIndex++)
        {
            VideoRecordingMetadata sourceMetadata = VideoRecordingMetadataStore.Load(sourceFileInfos[sourceIndex]);
            RemoveProvisionalTimelineWarning(sourceMetadata);
            mergedIssues = VideoRecordingMetadataStore.AddMediaIssue(mergedIssues, sourceMetadata.MediaIssue);
            mergedIntervals = VideoRecordingMetadataStore.MergeIssueIntervals(
                mergedIntervals,
                VideoRecordingMetadataStore.OffsetIssueIntervals(sourceMetadata.IssueIntervals, offset));
            offset += sourceIndex < sourceProbes.Count ? GetSourceTimelineEndSeconds(sourceProbes[sourceIndex]) : 0d;
        }
        metadata.MediaIssue = mergedIssues;
        metadata.IssueIntervals = mergedIntervals;
    }

    internal static void RemoveProvisionalTimelineWarning(VideoRecordingMetadata metadata)
    {
        VideoRecordingIssueInterval[] intervals = (metadata.IssueIntervals ?? []).ToArray();
        bool hasProvisionalWarning = intervals.Any(interval =>
            interval.Kind.Equals("timeline_mismatch", StringComparison.OrdinalIgnoreCase)
            && interval.Detail.Equals("output_timestamp_normalization_mismatch", StringComparison.OrdinalIgnoreCase));
        if (!hasProvisionalWarning)
        {
            return;
        }

        metadata.IssueIntervals = intervals
            .Where(interval => !(interval.Kind.Equals("timeline_mismatch", StringComparison.OrdinalIgnoreCase)
                && interval.Detail.Equals("output_timestamp_normalization_mismatch", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (!metadata.IssueIntervals.Any(interval => interval.Kind.Equals("timeline_mismatch", StringComparison.OrdinalIgnoreCase)))
        {
            metadata.MediaIssue = string.Join(';', (metadata.MediaIssue ?? string.Empty)
                .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(issue => !issue.Equals("timeline_mismatch", StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static SourceProbeBatch ProbeSources(IReadOnlyList<FileInfo> sourceFileInfos, CancellationToken token)
    {
        FfmpegMediaProbeResult[] probes = new FfmpegMediaProbeResult[sourceFileInfos.Count];
        for (int index = 0; index < sourceFileInfos.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (!FfmpegMediaEngine.TryProbe(sourceFileInfos[index].FullName, out probes[index], out string error, token, includePacketDiagnostics: true))
            {
                token.ThrowIfCancellationRequested();
                return new SourceProbeBatch(false, probes, sourceFileInfos[index].FullName, error);
            }
            token.ThrowIfCancellationRequested();
        }
        return new SourceProbeBatch(true, probes, string.Empty, string.Empty);
    }

    private static async Task<(bool Success, int FailedIndex, string Reason)> PreflightSourcesAsync(
        IReadOnlyList<FileInfo> sourceFileInfos,
        IReadOnlyList<FfmpegMediaProbeResult> sourceProbes,
        VideoRecordingMetadata metadata,
        string targetFormat,
        string conversionAttemptId,
        CancellationToken token)
    {
        for (int index = 0; index < sourceFileInfos.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            FileInfo source = sourceFileInfos[index];
            string requestedPath = Path.Combine(source.DirectoryName ?? string.Empty, "preflight" + targetFormat);
            string temporaryPath = MediaFileCatalog.CreateTemporaryPath(requestedPath, "preflight");
            try
            {
                VideoRecordingMetadata sourceMetadata = VideoRecordingMetadataStore.WithFileName(metadata, source.Name);
                FfmpegMediaRunResult result = await Task.Run(
                    () => FfmpegMediaEngine.RemuxFiles(
                        [source.FullName],
                        temporaryPath,
                        sourceMetadata,
                        token,
                        sourceTimelineEndSeconds: [GetSourceTimelineEndSeconds(sourceProbes[index])],
                        maximumTimelineEndSeconds: GetSourceTimelineEndSeconds(sourceProbes[index]),
                        strictTimelineLimit: true),
                    token);
                string validationError = result.ExitCode == 0 && result.HadMediaProgress
                    ? GetOutputValidationError(
                        temporaryPath,
                        [sourceProbes[index]],
                        result.ProcessedDurationSeconds,
                        sourceProbes[index].DurationSeconds,
                        token,
                        GetSourceTimelineEndSeconds(sourceProbes[index]),
                        result.AudioEndSeconds,
                        result.VideoEndSeconds)
                    : string.IsNullOrWhiteSpace(result.ErrorOutput)
                        ? "preflight_no_media_progress"
                        : result.ErrorOutput;
                AppSessionLogger.Event(
                    string.IsNullOrEmpty(validationError) ? "info" : "warn",
                    "media_diagnostic",
                    "conversion_source_preflight",
                    "individual source remux preflight completed",
                    new
                    {
                        conversionAttemptId,
                        sourceIndex = index + 1,
                        sourceFileName = source.FullName,
                        targetFormat,
                        outcome = string.IsNullOrEmpty(validationError) ? "ok" : "failed",
                        reason = validationError,
                        exitCode = result.ExitCode,
                        processedDurationSeconds = result.ProcessedDurationSeconds,
                        audioEndSeconds = result.AudioEndSeconds,
                        videoEndSeconds = result.VideoEndSeconds,
                        recoveredReadErrors = result.RecoveredReadErrors,
                        discardedPackets = result.DiscardedPackets,
                    });
                if (!string.IsNullOrEmpty(validationError))
                {
                    return (false, index, validationError);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
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

        return (true, -1, string.Empty);
    }

    private static bool IsSupportedSourceFormat(string extension)
    {
        return extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flv", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildTargetPath(IReadOnlyList<FileInfo> sourceFileInfos, string targetFormat)
    {
        FileInfo firstSource = sourceFileInfos[0];
        string directory = firstSource.DirectoryName ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(firstSource.Name);
        if (sourceFileInfos.Count > 1)
        {
            stem = Regex.Replace(stem, @"_\d{3,}$", string.Empty, RegexOptions.CultureInvariant);
        }

        return Path.Combine(directory, stem + targetFormat);
    }

    internal static string BuildSessionTargetPath(string sourcePattern, string targetFormat)
    {
        string directory = Path.GetDirectoryName(sourcePattern) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(sourcePattern);
        stem = stem.Replace("_%03d", string.Empty, StringComparison.Ordinal)
            .Replace("%03d", string.Empty, StringComparison.Ordinal)
            .TrimEnd('_', '-', '.', ' ');
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = Path.GetFileNameWithoutExtension(sourcePattern);
        }

        return Path.Combine(directory, stem + targetFormat);
    }

    internal static string GetAvailableTargetPath(string requestedPath)
    {
        lock (TargetReservationLock)
        {
            return GetAvailableTargetPathCore(requestedPath);
        }
    }

    private static string GetAvailableTargetPathCore(string requestedPath)
    {
        string fullRequestedPath = Path.GetFullPath(requestedPath);
        if (!File.Exists(fullRequestedPath) && !ReservedTargetPaths.Contains(fullRequestedPath))
        {
            return fullRequestedPath;
        }
        string directory = Path.GetDirectoryName(requestedPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(requestedPath);
        string extension = Path.GetExtension(requestedPath);
        for (int index = 2; ; index++)
        {
            string candidate = Path.Combine(directory, $"{stem}_{index}{extension}");
            string fullCandidate = Path.GetFullPath(candidate);
            if (!File.Exists(fullCandidate) && !ReservedTargetPaths.Contains(fullCandidate))
            {
                return fullCandidate;
            }
        }
    }

    private static string ReserveAvailableTargetPath(string requestedPath)
    {
        lock (TargetReservationLock)
        {
            string targetPath = GetAvailableTargetPathCore(requestedPath);
            ReservedTargetPaths.Add(targetPath);
            return targetPath;
        }
    }

    private static void ReleaseTargetPath(string targetPath)
    {
        lock (TargetReservationLock)
        {
            ReservedTargetPaths.Remove(targetPath);
        }
    }

    private static string GetTemporaryTargetPath(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);
        return MediaFileCatalog.CreateTemporaryPath(Path.Combine(directory, stem + extension), "convert");
    }

    internal static bool IsUsableOutput(
        string path,
        IReadOnlyList<FfmpegMediaProbeResult> sourceProbes,
        CancellationToken token = default)
    {
        return string.IsNullOrEmpty(GetOutputValidationError(path, sourceProbes, 0d, 0d, token));
    }

    private static string GetOutputValidationError(
        string path,
        IReadOnlyList<FfmpegMediaProbeResult> sourceProbes,
        double processedDurationSeconds,
        double recordingExpectedDuration,
        CancellationToken token,
        double maximumTimelineEndSeconds = 0d,
        double packetAudioEndSeconds = 0d,
        double packetVideoEndSeconds = 0d)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.Length <= 0)
            {
                return "output_missing_or_empty";
            }
            if (!FfmpegMediaEngine.TryProbe(path, out FfmpegMediaProbeResult output, out string probeError, token, includePacketDiagnostics: true))
            {
                return string.IsNullOrWhiteSpace(probeError) ? "output_probe_failed" : $"output_probe_failed:{probeError}";
            }

            int sourceAudioStreamCount = sourceProbes.Max(probe => probe.AudioStreamCount);
            bool sourceHasAudio = sourceAudioStreamCount > 0;
            bool sourceHasVideo = sourceProbes.Any(probe => probe.HasVideo);
            if (sourceHasAudio && !output.HasAudio)
            {
                return "output_audio_missing";
            }
            if (sourceHasVideo && !output.HasVideo)
            {
                return "output_video_missing";
            }
            if (output.PacketDiagnosticsComplete)
            {
                string packetDiagnosticsError = GetPacketDiagnosticsValidationError(output, sourceHasAudio, sourceHasVideo);
                if (!string.IsNullOrEmpty(packetDiagnosticsError))
                {
                    return packetDiagnosticsError;
                }
            }
            if (sourceHasAudio
                && (!FfmpegMediaEngine.TryAnalyzeAudioContent(path, out FfmpegAudioContentDiagnostic audioDiagnostic)
                    || audioDiagnostic.DecodeErrorCount > 0))
            {
                return !string.IsNullOrWhiteSpace(audioDiagnostic.Error)
                    ? $"output_audio_decode_failed:{audioDiagnostic.Error}"
                    : $"output_audio_decode_errors:{audioDiagnostic.DecodeErrorCount}";
            }
            double sourceAudioEndSeconds = sourceProbes.Sum(GetAudioDurationSeconds);
            double sourceVideoEndSeconds = sourceProbes.Sum(GetVideoDurationSeconds);
            string outputTimelineError = GetTrackTimelineValidationError(
                sourceHasAudio && output.HasAudio,
                sourceHasVideo && output.HasVideo,
                output.AudioStartSeconds,
                output.VideoStartSeconds,
                ResolveTrackEndSeconds(output.AudioEndSeconds, packetAudioEndSeconds),
                ResolveTrackEndSeconds(output.VideoEndSeconds, packetVideoEndSeconds),
                sourceAudioEndSeconds,
                sourceVideoEndSeconds,
                output.HasAudioStartTimestamp,
                output.HasVideoStartTimestamp);
            if (!string.IsNullOrEmpty(outputTimelineError))
            {
                return $"{outputTimelineError}:audio_start={output.AudioStartSeconds:F3},video_start={output.VideoStartSeconds:F3},audio={ResolveTrackEndSeconds(output.AudioEndSeconds, packetAudioEndSeconds):F3},video={ResolveTrackEndSeconds(output.VideoEndSeconds, packetVideoEndSeconds):F3},packet_audio={packetAudioEndSeconds:F3},packet_video={packetVideoEndSeconds:F3}";
            }

            double probedDuration = sourceProbes.Sum(probe => Math.Max(0, probe.DurationSeconds));
            double expectedDuration = SelectExpectedDuration(probedDuration, processedDurationSeconds);
            if (maximumTimelineEndSeconds > 0d)
            {
                expectedDuration = Math.Min(expectedDuration, maximumTimelineEndSeconds);
            }
            if (!IsDurationWithinTolerance(expectedDuration, output.DurationSeconds))
            {
                if (IsRecoverableDurationMismatch(expectedDuration, output.DurationSeconds, probedDuration))
                {
                    return $"duration_mismatch_recoverable:expected={expectedDuration:F3},actual={output.DurationSeconds:F3},probed={probedDuration:F3},processed={processedDurationSeconds:F3}";
                }
                return $"duration_mismatch:expected={expectedDuration:F3},actual={output.DurationSeconds:F3},probed={probedDuration:F3},processed={processedDurationSeconds:F3}";
            }
            return string.Empty;
        }
        catch (Exception e)
        {
            return $"output_validation_failed:{e.GetType().Name}:{e.Message}";
        }
    }

    internal static bool IsDurationWithinTolerance(double expectedDuration, double actualDuration)
    {
        if (expectedDuration <= 0)
        {
            return true;
        }
        if (actualDuration <= 0)
        {
            return false;
        }
        return Math.Abs(actualDuration - expectedDuration) <= 2d;
    }

    internal static bool IsRecoverableDurationMismatch(double expectedDuration, double actualDuration, double probedDuration)
    {
        if (expectedDuration <= 0d || actualDuration <= 0d)
        {
            return false;
        }

        double lowerBound = expectedDuration - 2d;
        double upperBound = Math.Max(expectedDuration, probedDuration) + MaximumRecoverableDurationDriftSeconds;
        return actualDuration >= lowerBound && actualDuration <= upperBound;
    }

    private static void ApplyValidationIssue(VideoRecordingMetadata metadata, string validationError)
    {
        if (validationError.StartsWith("output_track_timeline_mismatch:", StringComparison.Ordinal)
            || validationError.StartsWith("output_track_timeline_start_mismatch:", StringComparison.Ordinal))
        {
            VideoRecordingMetadataStore.AddIssueInterval(metadata, "timeline_mismatch", -1d, -1d, validationError);
            return;
        }

        if (!validationError.StartsWith("duration_mismatch_recoverable:", StringComparison.Ordinal))
        {
            return;
        }

        VideoRecordingMetadataStore.AddIssueInterval(metadata, "timeline_mismatch", -1d, -1d, validationError);
        AppSessionLogger.Event("info", "converter", "conversion_duration_drift_preserved", "conversion preserved a complete output with recoverable duration drift", new { validationError });
    }

    internal static double SelectExpectedDuration(double probedDuration, double processedDuration)
    {
        return processedDuration > 0d ? processedDuration : Math.Max(0d, probedDuration);
    }

    internal static double GetSourceTimelineEndSeconds(FfmpegMediaProbeResult probe)
    {
        return Math.Max(
            Math.Max(GetAudioDurationSeconds(probe), GetVideoDurationSeconds(probe)),
            Math.Max(0d, probe.DurationSeconds));
    }

    internal static double GetAudioDurationSeconds(FfmpegMediaProbeResult probe)
    {
        return GetTrackDurationSeconds(probe.AudioStartSeconds, probe.AudioEndSeconds);
    }

    internal static double GetVideoDurationSeconds(FfmpegMediaProbeResult probe)
    {
        return GetTrackDurationSeconds(probe.VideoStartSeconds, probe.VideoEndSeconds);
    }

    private static double GetTrackDurationSeconds(double startSeconds, double endSeconds)
    {
        if (endSeconds <= 0d)
        {
            return 0d;
        }

        if (startSeconds > 0d && endSeconds > startSeconds)
        {
            return endSeconds - startSeconds;
        }

        return endSeconds;
    }

    internal static bool TryGetAlignedTimelineEnd(
        IReadOnlyList<FfmpegMediaProbeResult> sourceProbes,
        out double alignedTimelineEndSeconds)
    {
        double audioEndSeconds = sourceProbes
            .Select(GetAudioDurationSeconds)
            .Sum();
        double videoEndSeconds = sourceProbes
            .Select(GetVideoDurationSeconds)
            .Sum();
        alignedTimelineEndSeconds = Math.Min(audioEndSeconds, videoEndSeconds);
        return audioEndSeconds > 0d
            && videoEndSeconds > 0d
            && alignedTimelineEndSeconds > 0d;
    }

    private static void ReportStorageFailureIfNeeded(FfmpegMediaRunResult result, IReadOnlyList<string> sourcePaths)
    {
        if (result.ExitCode != -28
            && !result.ErrorOutput.Contains("no space left", StringComparison.OrdinalIgnoreCase)
            && !result.ErrorOutput.Contains("not enough space", StringComparison.OrdinalIgnoreCase)
            && !result.ErrorOutput.Contains("disk full", StringComparison.OrdinalIgnoreCase)
            && !result.ErrorOutput.Contains("errno=28", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? path = sourcePaths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        GlobalMonitor.ReportStorageExhausted(path, "conversion");
    }

    internal static bool IsRecordingDurationComplete(double expectedDuration, double actualDuration)
    {
        if (expectedDuration <= 0d)
        {
            return true;
        }
        if (actualDuration <= 0d)
        {
            return false;
        }
        double tolerance = Math.Max(MinimumRecordingDurationToleranceSeconds, expectedDuration * RecordingDurationToleranceRatio);
        return actualDuration + tolerance >= expectedDuration;
    }

    internal static double GetRecordingExpectedDuration(VideoRecordingMetadata metadata, IReadOnlyList<FileInfo> sources)
    {
        if (metadata.RecordedAt <= DateTime.MinValue || sources.Count == 0)
        {
            return 0d;
        }
        DateTime recordedAt = metadata.RecordedAt.Kind == DateTimeKind.Utc
            ? metadata.RecordedAt.ToLocalTime()
            : metadata.RecordedAt;
        DateTime completedAt = sources.Max(source => source.LastWriteTime);
        double duration = (completedAt - recordedAt).TotalSeconds;
        return duration > 0d ? duration : 0d;
    }

    internal static double NormalizeRecordingExpectedDuration(double wallClockDuration, double mediaDuration)
    {
        if (wallClockDuration <= 0d || mediaDuration <= 0d)
        {
            return wallClockDuration;
        }
        double maximumPlausibleGap = Math.Max(300d, mediaDuration * 0.1d);
        return wallClockDuration - mediaDuration <= maximumPlausibleGap ? wallClockDuration : 0d;
    }

    internal static bool IsTrackTimelineWithinTolerance(double audioEndSeconds, double videoEndSeconds)
    {
        return audioEndSeconds <= 0d
            || videoEndSeconds <= 0d
            || Math.Abs(audioEndSeconds - videoEndSeconds) <= MaximumTrackTimelineDifferenceSeconds;
    }

    internal static bool IsSourceTimelineWithinTolerance(FfmpegMediaProbeResult probe)
    {
        if (!probe.HasAudio
            || !probe.HasVideo
            || !probe.HasAudioStartTimestamp
            || !probe.HasVideoStartTimestamp
            || Math.Abs(probe.AudioStartSeconds - probe.VideoStartSeconds) > MaximumTrackTimelineDifferenceSeconds)
        {
            return true;
        }

        double audioDuration = probe.AudioEndSeconds - probe.AudioStartSeconds;
        double videoDuration = probe.VideoEndSeconds - probe.VideoStartSeconds;
        return audioDuration <= 0d
            || videoDuration <= 0d
            || IsTrackTimelineWithinTolerance(audioDuration, videoDuration);
    }

    internal static double ResolveTrackEndSeconds(double probedEndSeconds, double packetEndSeconds)
    {
        return probedEndSeconds > 0d ? probedEndSeconds : Math.Max(0d, packetEndSeconds);
    }

    internal static string GetTrackTimelineValidationError(
        bool hasAudio,
        bool hasVideo,
        double audioEndSeconds,
        double videoEndSeconds)
    {
        return GetTrackTimelineValidationError(hasAudio, hasVideo, 0d, 0d, audioEndSeconds, videoEndSeconds);
    }

    internal static string GetTrackTimelineValidationError(
        bool hasAudio,
        bool hasVideo,
        double audioStartSeconds,
        double videoStartSeconds,
        double audioEndSeconds,
        double videoEndSeconds,
        bool hasAudioStartTimestamp = true,
        bool hasVideoStartTimestamp = true)
    {
        return GetTrackTimelineValidationError(
            hasAudio,
            hasVideo,
            audioStartSeconds,
            videoStartSeconds,
            audioEndSeconds,
            videoEndSeconds,
            0d,
            0d,
            hasAudioStartTimestamp,
            hasVideoStartTimestamp);
    }

    internal static string GetTrackTimelineValidationError(
        bool hasAudio,
        bool hasVideo,
        double audioStartSeconds,
        double videoStartSeconds,
        double audioEndSeconds,
        double videoEndSeconds,
        double sourceAudioEndSeconds,
        double sourceVideoEndSeconds,
        bool hasAudioStartTimestamp = true,
        bool hasVideoStartTimestamp = true)
    {
        if (!hasAudio || !hasVideo)
        {
            return string.Empty;
        }
        if (hasAudioStartTimestamp
            && hasVideoStartTimestamp
            && Math.Abs(audioStartSeconds - videoStartSeconds) > MaximumTrackTimelineDifferenceSeconds)
        {
            return "output_track_timeline_start_mismatch";
        }
        if (audioEndSeconds <= 0d || videoEndSeconds <= 0d)
        {
            return "output_track_timeline_unknown";
        }
        return IsTrackTimelineWithinTolerance(audioEndSeconds, videoEndSeconds, sourceAudioEndSeconds, sourceVideoEndSeconds)
            ? string.Empty
            : "output_track_timeline_mismatch";
    }

    internal static string GetPacketDiagnosticsValidationError(
        FfmpegMediaProbeResult output,
        bool requireAudio,
        bool requireVideo)
    {
        if (!output.PacketDiagnosticsComplete)
        {
            return string.Empty;
        }
        if (requireAudio && output.AudioPacketCount == 0)
        {
            return "output_audio_packets_missing";
        }
        if (requireVideo && (output.VideoPacketCount == 0 || output.VideoKeyframeCount == 0))
        {
            return "output_video_keyframe_missing";
        }
        if (output.TimestampDiscontinuityCount > 0)
        {
            return $"output_timestamp_discontinuity:{output.TimestampDiscontinuityCount}";
        }
        return output.InterPacketGapCount > 0
            ? $"output_internal_pause:count={output.InterPacketGapCount},maximum={output.MaximumInterPacketGapSeconds:F3}"
            : string.Empty;
    }

    internal static int FindFirstSourceTimelineMismatchIndex(IReadOnlyList<FfmpegMediaProbeResult> probes)
    {
        double cumulativeAudioDurationSeconds = 0d;
        double cumulativeVideoDurationSeconds = 0d;
        for (int index = 0; index < probes.Count; index++)
        {
            FfmpegMediaProbeResult probe = probes[index];
            if (!probe.HasAudio
                || !probe.HasVideo
                || !probe.HasAudioStartTimestamp
                || !probe.HasVideoStartTimestamp
                || Math.Abs(probe.AudioStartSeconds - probe.VideoStartSeconds) > MaximumTrackTimelineDifferenceSeconds)
            {
                continue;
            }

            double audioDuration = probe.AudioEndSeconds - probe.AudioStartSeconds;
            double videoDuration = probe.VideoEndSeconds - probe.VideoStartSeconds;
            if (audioDuration <= 0d || videoDuration <= 0d)
            {
                continue;
            }

            cumulativeAudioDurationSeconds += audioDuration;
            cumulativeVideoDurationSeconds += videoDuration;
            if (!IsTrackTimelineWithinTolerance(audioDuration, videoDuration)
                || !IsTrackTimelineWithinTolerance(cumulativeAudioDurationSeconds, cumulativeVideoDurationSeconds))
            {
                return index;
            }
        }

        return -1;
    }

    internal static double GetTimelineIssueStartSeconds(double audioEndSeconds, double videoEndSeconds)
    {
        return Math.Abs(audioEndSeconds - videoEndSeconds) > MaximumTrackTimelineDifferenceSeconds
            ? 0d
            : Math.Min(Math.Max(0d, audioEndSeconds), Math.Max(0d, videoEndSeconds));
    }

    internal static bool IsTrackTimelineWithinTolerance(
        double audioEndSeconds,
        double videoEndSeconds,
        double sourceAudioEndSeconds,
        double sourceVideoEndSeconds)
    {
        if (IsTrackTimelineWithinTolerance(audioEndSeconds, videoEndSeconds))
        {
            return true;
        }
        if (sourceAudioEndSeconds <= 0d || sourceVideoEndSeconds <= 0d)
        {
            return false;
        }

        double outputDifference = audioEndSeconds - videoEndSeconds;
        double sourceDifference = sourceAudioEndSeconds - sourceVideoEndSeconds;
        return Math.Sign(outputDifference) == Math.Sign(sourceDifference)
            && Math.Abs(outputDifference) <= Math.Abs(sourceDifference) + MaximumTrackTimelineDifferenceSeconds;
    }

    private static void DeleteTemporaryOutput(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record SourceProbeBatch(
        bool Success,
        FfmpegMediaProbeResult[] Probes,
        string InvalidSourcePath,
        string Error);

}
