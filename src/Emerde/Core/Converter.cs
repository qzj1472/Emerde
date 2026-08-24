using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed record ConverterOptions(
    string TargetFormat,
    bool OptimizeAudio = false,
    bool RemoveSource = false);

public sealed class Converter
{
    private const double MaximumTrackTimelineDifferenceSeconds = 5d;
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
        bool optimizeAudio = targetFormat == ".mp4" && converterOptions.OptimizeAudio;

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
            || (!allowSameFormat && sourceFileInfos.Any(file => file.Extension.Equals(targetFormat, StringComparison.OrdinalIgnoreCase))))
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
        bool targetCreated = false;
        bool completionAcknowledged = false;
        try
        {
            onTargetReserved?.Invoke(targetFileName);
            Stopwatch queueWait = Stopwatch.StartNew();
            using IDisposable conversionSlot = await ConversionWorkScheduler.EnterAsync(optimizeAudio, token);
            queueWait.Stop();
            if (queueWait.ElapsedMilliseconds > 250)
            {
                AppSessionLogger.Event("info", "converter", "conversion_queue_released", "recording conversion left the work queue", new
                {
                    sourceFileNames,
                    targetFileName,
                    optimizeAudio,
                    hasActiveRecorders = GlobalMonitor.HasActiveRecorders,
                    waitMilliseconds = queueWait.ElapsedMilliseconds,
                });
            }
            SourceProbeBatch probeBatch = await Task.Run(() => ProbeSources(sourceFileInfos, token), token);
            if (!probeBatch.Success)
            {
                AppSessionLogger.Event("error", "converter", "conversion_source_invalid", probeBatch.Error, new { sourceFileName = probeBatch.InvalidSourcePath });
                onFailed?.Invoke($"source_probe_failed:{probeBatch.Error}");
                return false;
            }
            FfmpegMediaProbeResult[] sourceProbes = probeBatch.Probes;
            double[] sourceTimelineEndSeconds = sourceProbes
                .Select(GetSourceTimelineEndSeconds)
                .ToArray();
            double probedSourceDuration = sourceProbes.Sum(probe => Math.Max(0d, probe.DurationSeconds));
            double sourceAudioEndSeconds = sourceProbes.Sum(probe => Math.Max(0d, probe.AudioEndSeconds));
            double sourceVideoEndSeconds = sourceProbes.Sum(probe => Math.Max(0d, probe.VideoEndSeconds));
            if (!IsTrackTimelineWithinTolerance(sourceAudioEndSeconds, sourceVideoEndSeconds))
            {
                metadata.MediaIssue = "timeline_mismatch";
            }
            double recordingExpectedDuration = NormalizeRecordingExpectedDuration(
                GetRecordingExpectedDuration(metadata, sourceFileInfos),
                probedSourceDuration);
            bool optimizedAudioRequested = optimizeAudio;
            optimizeAudio = optimizedAudioRequested && sourceProbes.All(probe => probe.HasAudio);
            string[] sourcePaths = sourceFileInfos.Select(file => file.FullName).ToArray();
            AppSessionLogger.Event("info", "converter", "conversion_starting", "recording conversion is starting", new
            {
                sourceFileNames = sourcePaths,
                targetFileName,
                activeConversions = ActiveConversionCount,
                optimizeAudio,
                optimizedAudioRequested,
            });
            bool optimizedAudioFallback = false;
            FfmpegMediaRunResult result = await Task.Run(
                () => optimizeAudio
                    ? FfmpegMediaEngine.RemuxFilesWithOptimizedAudio(
                        sourcePaths,
                        temporaryTargetFileName,
                        metadata,
                        token,
                        parallelizePreparation: sourcePaths.Length > 1 && !GlobalMonitor.HasActiveRecorders,
                        sourceTimelineEndSeconds: sourceTimelineEndSeconds)
                    : FfmpegMediaEngine.RemuxFiles(
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

            (bool succeeded, string validationError) = await ValidateConversionAsync(result, temporaryTargetFileName, sourceProbes, optimizeAudio, recordingExpectedDuration, token);
            if (!succeeded && optimizeAudio)
            {
                AppSessionLogger.Event("warn", "converter", "optimized_audio_fallback", "optimized audio conversion failed and plain remux will be attempted", new
                {
                    sourceFileNames = sourcePaths,
                    targetFileName,
                    result.ExitCode,
                    result.ErrorOutput,
                    validationError,
                });
                DeleteTemporaryOutput(temporaryTargetFileName);
                optimizedAudioFallback = true;
                optimizeAudio = false;
                result = await Task.Run(
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
                (succeeded, validationError) = await ValidateConversionAsync(result, temporaryTargetFileName, sourceProbes, optimizedAudioExpected: false, recordingExpectedDuration, token);
            }
            if (succeeded)
            {
                File.Move(temporaryTargetFileName, targetFileName, false);
                targetCreated = true;
                metadata.HasOptimizedAudio = optimizeAudio;
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
                completionAcknowledged = true;
                if (converterOptions.RemoveSource)
                {
                    _ = TryDeleteSourceFiles(sourcePaths);
                }
            }
            AppSessionLogger.Event(succeeded ? "info" : "error", "converter", "conversion_finished", "recording conversion finished", new
            {
                sourceFileNames = sourceFileInfos.Select(file => file.FullName).ToArray(),
                targetFileName,
                result.ExitCode,
                succeeded,
                optimizeAudio,
                optimizedAudioRequested,
                optimizedAudioFallback,
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
        return new ConverterOptions(targetFormat, false);
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
        bool optimizedAudioExpected,
        double recordingExpectedDuration,
        CancellationToken token)
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
                optimizedAudioExpected,
                result.ProcessedDurationSeconds,
                recordingExpectedDuration,
                token),
            token);
        return (string.IsNullOrEmpty(validationError), validationError);
    }

    private static SourceProbeBatch ProbeSources(IReadOnlyList<FileInfo> sourceFileInfos, CancellationToken token)
    {
        FfmpegMediaProbeResult[] probes = new FfmpegMediaProbeResult[sourceFileInfos.Count];
        for (int index = 0; index < sourceFileInfos.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (!FfmpegMediaEngine.TryProbe(sourceFileInfos[index].FullName, out probes[index], out string error, token))
            {
                token.ThrowIfCancellationRequested();
                return new SourceProbeBatch(false, probes, sourceFileInfos[index].FullName, error);
            }
            token.ThrowIfCancellationRequested();
        }
        return new SourceProbeBatch(true, probes, string.Empty, string.Empty);
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
        bool optimizedAudioExpected,
        CancellationToken token = default)
    {
        return string.IsNullOrEmpty(GetOutputValidationError(path, sourceProbes, optimizedAudioExpected, 0d, 0d, token));
    }

    private static string GetOutputValidationError(
        string path,
        IReadOnlyList<FfmpegMediaProbeResult> sourceProbes,
        bool optimizedAudioExpected,
        double processedDurationSeconds,
        double recordingExpectedDuration,
        CancellationToken token)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.Length <= 0)
            {
                return "output_missing_or_empty";
            }
            if (!FfmpegMediaEngine.TryProbe(path, out FfmpegMediaProbeResult output, out string probeError, token))
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
            if (optimizedAudioExpected && sourceHasAudio && output.AudioStreamCount < sourceAudioStreamCount + 1)
            {
                return $"optimized_audio_track_missing:expected={sourceAudioStreamCount + 1},actual={output.AudioStreamCount}";
            }
            double sourceAudioEndSeconds = sourceProbes.Sum(probe => Math.Max(0d, probe.AudioEndSeconds));
            double sourceVideoEndSeconds = sourceProbes.Sum(probe => Math.Max(0d, probe.VideoEndSeconds));
            if (!IsTrackTimelineWithinTolerance(
                    output.AudioEndSeconds,
                    output.VideoEndSeconds,
                    sourceAudioEndSeconds,
                    sourceVideoEndSeconds))
            {
                return $"output_track_timeline_mismatch:audio={output.AudioEndSeconds:F3},video={output.VideoEndSeconds:F3}";
            }

            double probedDuration = sourceProbes.Sum(probe => Math.Max(0, probe.DurationSeconds));
            double expectedDuration = SelectExpectedDuration(probedDuration, processedDurationSeconds);
            if (!IsDurationWithinTolerance(expectedDuration, output.DurationSeconds))
            {
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

    internal static double SelectExpectedDuration(double probedDuration, double processedDuration)
    {
        return processedDuration > 0d ? processedDuration : Math.Max(0d, probedDuration);
    }

    internal static double GetSourceTimelineEndSeconds(FfmpegMediaProbeResult probe)
    {
        return Math.Max(
            Math.Max(0d, probe.AudioEndSeconds),
            Math.Max(Math.Max(0d, probe.VideoEndSeconds), Math.Max(0d, probe.DurationSeconds)));
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
            || Math.Abs(audioEndSeconds - videoEndSeconds) <= 2d;
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
            return Math.Abs(audioEndSeconds - videoEndSeconds) <= MaximumTrackTimelineDifferenceSeconds;
        }

        double outputDifference = audioEndSeconds - videoEndSeconds;
        double sourceDifference = sourceAudioEndSeconds - sourceVideoEndSeconds;
        if (Math.Sign(outputDifference) != Math.Sign(sourceDifference))
        {
            return false;
        }

        return Math.Abs(outputDifference) <= Math.Abs(sourceDifference) + 2d;
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
