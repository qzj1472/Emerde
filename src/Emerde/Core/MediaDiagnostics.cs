namespace Emerde.Core;

internal static class MediaDiagnostics
{
    public static void LogProbe(
        string stage,
        string path,
        FfmpegMediaProbeResult probe,
        VideoRecordingMetadata? metadata = null,
        string roomUrl = "",
        string targetPath = "",
        string outcome = "ok",
        string reason = "")
    {
        AppSessionLogger.Event(
            outcome.Equals("ok", StringComparison.OrdinalIgnoreCase) ? "info" : "warn",
            "media_diagnostic",
            "media_stage_diagnostic",
            "media stage diagnostic",
            new
            {
                stage,
                path,
                targetPath,
                roomUrl,
                outcome,
                reason,
                hasAudio = probe.HasAudio,
                hasVideo = probe.HasVideo,
                audioStreamCount = probe.AudioStreamCount,
                videoStreamCount = probe.VideoStreamCount,
                audioEndSeconds = Math.Round(Math.Max(0d, probe.AudioEndSeconds), 3),
                videoEndSeconds = Math.Round(Math.Max(0d, probe.VideoEndSeconds), 3),
                durationSeconds = Math.Round(Math.Max(0d, probe.DurationSeconds), 3),
                width = probe.Width,
                height = probe.Height,
                frameRate = Math.Round(Math.Max(0d, probe.FrameRate), 3),
                bitrate = probe.Bitrate,
                videoCodec = probe.VideoCodec,
                audioCodec = probe.AudioCodec,
                hasOptimizedAudio = probe.HasOptimizedAudio,
                mediaIssue = metadata?.MediaIssue ?? probe.Metadata.MediaIssue,
                wasRepaired = metadata?.WasRepaired ?? probe.Metadata.WasRepaired,
                recordingSessionId = metadata?.RecordingSessionId ?? probe.Metadata.RecordingSessionId,
            });
    }

    public static void LogProbeFailure(
        string stage,
        string path,
        string reason,
        VideoRecordingMetadata? metadata = null,
        string roomUrl = "",
        string targetPath = "")
    {
        AppSessionLogger.Event(
            "warn",
            "media_diagnostic",
            "media_stage_diagnostic",
            "media stage diagnostic failed",
            new
            {
                stage,
                path,
                targetPath,
                roomUrl,
                outcome = "probe_failed",
                reason,
                recordingSessionId = metadata?.RecordingSessionId ?? string.Empty,
                mediaIssue = metadata?.MediaIssue ?? string.Empty,
            });
    }

    public static void LogRun(
        string stage,
        string inputPath,
        string targetPath,
        FfmpegMediaRunResult result,
        string roomUrl = "",
        string reason = "",
        VideoRecordingMetadata? metadata = null)
    {
        AppSessionLogger.Event(
            result.ExitCode == 0 ? "info" : "warn",
            "media_diagnostic",
            "media_stage_run",
            "media stage run diagnostic",
            new
            {
                stage,
                inputPath,
                targetPath,
                roomUrl,
                recordingSessionId = metadata?.RecordingSessionId ?? string.Empty,
                outcome = result.ExitCode == 0 ? "ok" : "failed",
                reason,
                exitCode = result.ExitCode,
                wasCanceled = result.WasCanceled,
                hadMediaProgress = result.HadMediaProgress,
                processedDurationSeconds = Math.Round(Math.Max(0d, result.ProcessedDurationSeconds), 3),
                recoveredReadErrors = result.RecoveredReadErrors,
                discardedPackets = result.DiscardedPackets,
                errorOutput = result.ErrorOutput,
            });
    }

    public static void LogStreamPresence(
        string stage,
        string path,
        bool hasVideo,
        bool hasAudio,
        string roomUrl = "",
        string targetPath = "",
        string reason = "",
        VideoRecordingMetadata? metadata = null)
    {
        AppSessionLogger.Event(
            hasVideo && hasAudio ? "info" : "warn",
            "media_diagnostic",
            "media_stage_streams",
            "media stage streams discovered",
            new
            {
                stage,
                path,
                targetPath,
                roomUrl,
                recordingSessionId = metadata?.RecordingSessionId ?? string.Empty,
                outcome = "streams_discovered",
                reason,
                hasVideo,
                hasAudio,
            });
    }

    public static void LogAudioContent(
        string stage,
        string path,
        VideoRecordingMetadata? metadata = null,
        string roomUrl = "",
        string targetPath = "")
    {
        try
        {
            if (FfmpegMediaEngine.TryAnalyzeAudioContent(path, out FfmpegAudioContentDiagnostic diagnostic))
            {
                AppSessionLogger.Event(
                    diagnostic.DecodeErrorCount == 0 ? "info" : "warn",
                    "media_diagnostic",
                    "media_content_diagnostic",
                    "media audio content diagnostic",
                    new
                    {
                        stage,
                        path,
                        targetPath,
                        roomUrl,
                        content = "audio",
                        outcome = diagnostic.DecodeErrorCount == 0 ? "ok" : "decode_errors",
                        sampledSeconds = Math.Round(diagnostic.SampledSeconds, 3),
                        rms = Math.Round(diagnostic.Rms, 6),
                        peak = Math.Round(diagnostic.Peak, 6),
                        longestNearSilenceSeconds = Math.Round(diagnostic.LongestNearSilenceSeconds, 3),
                        peakFrameCount = diagnostic.PeakFrameCount,
                        decodeErrorCount = diagnostic.DecodeErrorCount,
                        recordingSessionId = metadata?.RecordingSessionId ?? string.Empty,
                    });
                return;
            }

            LogProbeFailure(stage, path, diagnostic.Error, metadata, roomUrl, targetPath);
        }
        catch (Exception exception)
        {
            LogProbeFailure(stage, path, exception.Message, metadata, roomUrl, targetPath);
        }
    }

    public static string GetFileStage(string path)
    {
        return Path.GetExtension(path).Equals(".ts", StringComparison.OrdinalIgnoreCase)
            ? "source_ts"
            : Path.GetExtension(path).Equals(".flv", StringComparison.OrdinalIgnoreCase)
                ? "source_flv"
                : "media_file";
    }

    public static void LogProbeOrFailure(
        string stage,
        string path,
        VideoRecordingMetadata? metadata = null,
        string roomUrl = "",
        string targetPath = "",
        string reason = "")
    {
        try
        {
            if (FfmpegMediaEngine.TryProbe(path, out FfmpegMediaProbeResult probe, out string error))
            {
                LogProbe(stage, path, probe, metadata, roomUrl, targetPath, string.IsNullOrWhiteSpace(reason) ? "ok" : "validation_failed", reason);
                return;
            }

            LogProbeFailure(stage, path, string.IsNullOrWhiteSpace(error) ? reason : error, metadata, roomUrl, targetPath);
        }
        catch (Exception exception)
        {
            LogProbeFailure(stage, path, exception.Message, metadata, roomUrl, targetPath);
        }
    }
}
