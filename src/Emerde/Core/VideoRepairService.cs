using System.Text.Json;

namespace Emerde.Core;

internal enum VideoRepairStatus
{
    Failed,
    Repaired,
    PartiallyRepaired,
    Canceled,
}

internal sealed record VideoRepairResult(
    VideoRepairStatus Status,
    string OutputPath,
    string ReportPath,
    string Error,
    double SourceAudioEndSeconds,
    double SourceVideoEndSeconds,
    double OutputAudioEndSeconds,
    double OutputVideoEndSeconds,
    int RecoveredReadErrors,
    int DiscardedPackets);

internal sealed class VideoRepairService
{
    internal const string RepairReportSuffix = ".repair.json";

    private static readonly object TargetReservationLock = new();
    private static readonly HashSet<string> ReservedTargetPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        WriteIndented = true,
    };

    public async Task<VideoRepairResult> RepairAsync(
        string sourcePath,
        string targetExtension = ".mkv",
        CancellationToken cancellationToken = default)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        string normalizedTargetExtension = NormalizeTargetExtension(targetExtension);
        if (!File.Exists(fullSourcePath) || !IsSupportedSource(fullSourcePath) || string.IsNullOrEmpty(normalizedTargetExtension))
        {
            return Failed("source_invalid");
        }
        if (!FfmpegMediaEngine.IsAvailable)
        {
            return Failed("converter_missing");
        }

        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string targetPath = ReserveTargetPath(fullSourcePath, normalizedTargetExtension);
        string temporaryPath = MediaFileCatalog.CreateTemporaryPath(targetPath, "repair");
        using IDisposable? operation = MediaOperationRegistry.TryRegister(
            MediaOperationKind.Repair,
            [fullSourcePath, temporaryPath, targetPath],
            operationCancellation.Cancel);
        if (operation == null)
        {
            ReleaseTargetPath(targetPath);
            return Failed("source_busy");
        }
        try
        {
            CancellationToken token = operationCancellation.Token;
            FfmpegMediaProbeResult sourceProbe = await ProbeAsync(fullSourcePath, token);
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.WithFileName(
                VideoRecordingMetadataStore.Load(new FileInfo(fullSourcePath)),
                Path.GetFileName(targetPath));
            using IDisposable workSlot = await ConversionWorkScheduler.EnterAsync(false, token);
            double maximumTimelineEndSeconds = Math.Max(
                Math.Max(0d, sourceProbe.AudioEndSeconds),
                Math.Max(0d, sourceProbe.VideoEndSeconds));
            FfmpegMediaRunResult runResult = await Task.Run(
                () => FfmpegMediaEngine.RepairFile(
                    fullSourcePath,
                    temporaryPath,
                    metadata,
                    token,
                    maximumTimelineEndSeconds: maximumTimelineEndSeconds),
                token);
            if (runResult.WasCanceled)
            {
                return Canceled();
            }
            if (runResult.ExitCode != 0 || !runResult.HadMediaProgress)
            {
                return Failed(string.IsNullOrWhiteSpace(runResult.ErrorOutput) ? "repair_no_media_progress" : runResult.ErrorOutput);
            }

            FfmpegMediaProbeResult outputProbe = await ProbeAsync(temporaryPath, token);
            string validationError = GetValidationError(sourceProbe, outputProbe);
            if (!string.IsNullOrEmpty(validationError))
            {
                return Failed(validationError);
            }

            bool timelineAligned = Converter.IsTrackTimelineWithinTolerance(
                outputProbe.AudioEndSeconds,
                outputProbe.VideoEndSeconds);
            VideoRepairStatus status = timelineAligned
                ? VideoRepairStatus.Repaired
                : VideoRepairStatus.PartiallyRepaired;
            metadata.SchemaVersion = 4;
            metadata.MediaIssue = timelineAligned ? string.Empty : "timeline_mismatch";
            metadata.WasRepaired = timelineAligned;
            File.Move(temporaryPath, targetPath, false);
            _ = RecordingCoverStore.TryCopyOrCreateFinalizedCover(
                [fullSourcePath],
                targetPath,
                metadata,
                outputProbe.DurationSeconds,
                token);
            _ = VideoRecordingMetadataStore.WriteCompletedMetadata(targetPath, metadata);
            string reportPath = targetPath + RepairReportSuffix;
            VideoRepairResult result = new(
                status,
                targetPath,
                reportPath,
                string.Empty,
                sourceProbe.AudioEndSeconds,
                sourceProbe.VideoEndSeconds,
                outputProbe.AudioEndSeconds,
                outputProbe.VideoEndSeconds,
                runResult.RecoveredReadErrors,
                runResult.DiscardedPackets);
            try
            {
                await AtomicFile.WriteJsonAsync(reportPath, result, ReportOptions, CancellationToken.None);
            }
            catch (Exception exception)
            {
                AppSessionLogger.Event("warn", "video_repair", "video_repair_report_failed", exception.Message, new { sourcePath = fullSourcePath, targetPath, reportPath });
                result = result with { ReportPath = string.Empty };
            }
            AppSessionLogger.Event("info", "video_repair", "video_repair_finished", "damaged recording repair finished", result);
            return result;
        }
        catch (OperationCanceledException)
        {
            return Canceled();
        }
        catch (Exception exception)
        {
            AppSessionLogger.Event("error", "video_repair", "video_repair_failed", exception.Message, new { sourcePath = fullSourcePath });
            return Failed(exception.Message);
        }
        finally
        {
            TryDelete(temporaryPath);
            ReleaseTargetPath(targetPath);
        }
    }

    internal static string BuildRequestedTargetPath(string sourcePath, string targetExtension = ".mkv")
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        string normalizedExtension = NormalizeTargetExtension(targetExtension);
        if (string.IsNullOrEmpty(normalizedExtension))
        {
            throw new ArgumentOutOfRangeException(nameof(targetExtension));
        }
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(sourcePath) + normalizedExtension);
    }

    internal static bool IsOrphanedRepairReport(string path)
    {
        if (!path.EndsWith(RepairReportSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string mediaPath = path[..^RepairReportSuffix.Length];
        return MediaFileCatalog.IsMediaPath(mediaPath) && !File.Exists(mediaPath);
    }

    internal static void TryDeleteRepairReport(string mediaPath)
    {
        TryDelete(mediaPath + RepairReportSuffix);
    }

    internal static bool TryCopyRepairReport(string sourcePath, string targetPath)
    {
        string sourceReport = sourcePath + RepairReportSuffix;
        if (!File.Exists(sourceReport))
        {
            return true;
        }
        try
        {
            File.Copy(sourceReport, targetPath + RepairReportSuffix, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppSessionLogger.WriteException(exception);
            return false;
        }
    }

    internal static bool TryMoveRepairReport(string sourcePath, string targetPath)
    {
        if (!TryCopyRepairReport(sourcePath, targetPath))
        {
            return false;
        }
        TryDeleteRepairReport(sourcePath);
        return true;
    }

    internal static bool IsSupportedSource(string sourcePath)
    {
        string extension = Path.GetExtension(sourcePath);
        return extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flv", StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeTargetExtension(string targetExtension)
    {
        string extension = targetExtension.Trim().ToLowerInvariant();
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }
        return extension is ".mkv" or ".mp4" ? extension : string.Empty;
    }

    private static string ReserveTargetPath(string sourcePath, string targetExtension)
    {
        lock (TargetReservationLock)
        {
            string requestedPath = BuildRequestedTargetPath(sourcePath, targetExtension);
            string availablePath = Converter.GetAvailableTargetPath(requestedPath);
            if (!ReservedTargetPaths.Contains(availablePath))
            {
                ReservedTargetPaths.Add(availablePath);
                return availablePath;
            }

            string directory = Path.GetDirectoryName(requestedPath) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(requestedPath);
            for (int index = 2; ; index++)
            {
                availablePath = Converter.GetAvailableTargetPath(Path.Combine(directory, $"{stem}_{index}{targetExtension}"));
                if (!ReservedTargetPaths.Contains(availablePath))
                {
                    ReservedTargetPaths.Add(availablePath);
                    return availablePath;
                }
            }
        }
    }

    private static void ReleaseTargetPath(string targetPath)
    {
        lock (TargetReservationLock)
        {
            ReservedTargetPaths.Remove(targetPath);
        }
    }

    private static async Task<FfmpegMediaProbeResult> ProbeAsync(string path, CancellationToken token)
    {
        (bool succeeded, FfmpegMediaProbeResult result, string error) = await Task.Run(() =>
        {
            bool success = FfmpegMediaEngine.TryProbe(path, out FfmpegMediaProbeResult probe, out string probeError, token);
            return (success, probe, probeError);
        }, token);
        if (!succeeded)
        {
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error) ? "probe_failed" : error);
        }
        return result;
    }

    private static string GetValidationError(FfmpegMediaProbeResult source, FfmpegMediaProbeResult output)
    {
        if (!output.HasAudio && !output.HasVideo)
        {
            return "repair_output_has_no_media";
        }
        if (source.HasAudio && !output.HasAudio)
        {
            return "repair_output_audio_missing";
        }
        if (source.HasVideo && !output.HasVideo)
        {
            return "repair_output_video_missing";
        }
        if (!Converter.IsTrackTimelineWithinTolerance(
                output.AudioEndSeconds,
                output.VideoEndSeconds,
                source.AudioEndSeconds,
                source.VideoEndSeconds))
        {
            return $"repair_output_track_timeline_mismatch:audio={output.AudioEndSeconds:F3},video={output.VideoEndSeconds:F3}";
        }

        double sourceTimelineEndSeconds = Math.Max(source.AudioEndSeconds, source.VideoEndSeconds);
        if (sourceTimelineEndSeconds > 0d
            && !Converter.IsDurationWithinTolerance(sourceTimelineEndSeconds, output.DurationSeconds))
        {
            return $"repair_output_duration_mismatch:expected={sourceTimelineEndSeconds:F3},actual={output.DurationSeconds:F3}";
        }
        return string.Empty;
    }

    private static VideoRepairResult Failed(string error)
    {
        return new(VideoRepairStatus.Failed, string.Empty, string.Empty, error, 0, 0, 0, 0, 0, 0);
    }

    private static VideoRepairResult Canceled()
    {
        return new(VideoRepairStatus.Canceled, string.Empty, string.Empty, string.Empty, 0, 0, 0, 0, 0, 0);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(exception);
        }
    }
}
