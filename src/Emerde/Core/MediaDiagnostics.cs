using System.Diagnostics;
using System.Security.Cryptography;

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
        string reason = "",
        int? segmentIndex = null,
        string conversionAttemptId = "")
    {
        AppSessionLogger.Event(
            outcome.Equals("ok", StringComparison.OrdinalIgnoreCase) ? "info" : "warn",
            "media_diagnostic",
            "media_stage_diagnostic",
            "media stage diagnostic",
            new
            {
                stage,
                segmentIndex,
                conversionAttemptId,
                path,
                targetPath,
                roomUrl,
                outcome,
                reason,
                hasAudio = probe.HasAudio,
                hasVideo = probe.HasVideo,
                audioStreamCount = probe.AudioStreamCount,
                videoStreamCount = probe.VideoStreamCount,
                audioStartSeconds = Math.Round(Math.Max(0d, probe.AudioStartSeconds), 3),
                videoStartSeconds = Math.Round(Math.Max(0d, probe.VideoStartSeconds), 3),
                probe.HasAudioStartTimestamp,
                probe.HasVideoStartTimestamp,
                audioEndSeconds = Math.Round(Math.Max(0d, probe.AudioEndSeconds), 3),
                videoEndSeconds = Math.Round(Math.Max(0d, probe.VideoEndSeconds), 3),
                durationSeconds = Math.Round(Math.Max(0d, probe.DurationSeconds), 3),
                width = probe.Width,
                height = probe.Height,
                frameRate = Math.Round(Math.Max(0d, probe.FrameRate), 3),
                bitrate = probe.Bitrate,
                videoCodec = probe.VideoCodec,
                audioCodec = probe.AudioCodec,
                sourceFileSizeBytes = GetFileSize(path),
                sourceLastWriteTimeUtc = GetLastWriteTimeUtc(path),
                sourceSha256 = ComputeSourceHash(path),
                ffmpegLibraryVersions = GetFfmpegLibraryVersions(),
                packetDiagnosticsComplete = probe.PacketDiagnosticsComplete,
                audioPacketCount = probe.AudioPacketCount,
                videoPacketCount = probe.VideoPacketCount,
                audioKeyframeCount = probe.AudioKeyframeCount,
                videoKeyframeCount = probe.VideoKeyframeCount,
                firstAudioPtsSeconds = RoundTimestamp(probe.FirstAudioPtsSeconds),
                firstAudioDtsSeconds = RoundTimestamp(probe.FirstAudioDtsSeconds),
                lastAudioPtsSeconds = RoundTimestamp(probe.LastAudioPtsSeconds),
                lastAudioDtsSeconds = RoundTimestamp(probe.LastAudioDtsSeconds),
                firstVideoPtsSeconds = RoundTimestamp(probe.FirstVideoPtsSeconds),
                firstVideoDtsSeconds = RoundTimestamp(probe.FirstVideoDtsSeconds),
                lastVideoPtsSeconds = RoundTimestamp(probe.LastVideoPtsSeconds),
                lastVideoDtsSeconds = RoundTimestamp(probe.LastVideoDtsSeconds),
                timestampDiscontinuityCount = probe.TimestampDiscontinuityCount,
                interPacketGapCount = probe.InterPacketGapCount,
                maximumInterPacketGapSeconds = RoundTimestamp(probe.MaximumInterPacketGapSeconds),
                mediaIssue = metadata?.MediaIssue ?? probe.Metadata.MediaIssue,
                issueIntervals = VideoRecordingMetadataStore.CloneIssueIntervals(metadata?.IssueIntervals ?? probe.Metadata.IssueIntervals),
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
        string targetPath = "",
        string conversionAttemptId = "")
    {
        string level = reason.Contains("recorded_source_missing", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("source_missing", StringComparison.OrdinalIgnoreCase)
            ? "info"
            : "warn";
        AppSessionLogger.Event(
            level,
            "media_diagnostic",
            "media_stage_diagnostic",
            "media stage diagnostic failed",
            new
            {
                stage,
                conversionAttemptId,
                path,
                targetPath,
                roomUrl,
                outcome = "probe_failed",
                reason,
                sourceFileSizeBytes = GetFileSize(path),
                sourceLastWriteTimeUtc = GetLastWriteTimeUtc(path),
                sourceSha256 = ComputeSourceHash(path),
                ffmpegLibraryVersions = GetFfmpegLibraryVersions(),
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
        VideoRecordingMetadata? metadata = null,
        string conversionAttemptId = "")
    {
        AppSessionLogger.Event(
            result.ExitCode == 0 ? "info" : "warn",
            "media_diagnostic",
            "media_stage_run",
            "media stage run diagnostic",
            new
            {
                stage,
                conversionAttemptId,
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
                audioEndSeconds = Math.Round(Math.Max(0d, result.AudioEndSeconds), 3),
                videoEndSeconds = Math.Round(Math.Max(0d, result.VideoEndSeconds), 3),
                recoveredReadErrors = result.RecoveredReadErrors,
                discardedPackets = result.DiscardedPackets,
                errorOutput = result.ErrorOutput,
            });

        foreach (FfmpegSegmentTimelineDiagnostic segment in result.SegmentTimelineDiagnostics ?? [])
        {
            AppSessionLogger.Event("info", "media_diagnostic", "conversion_segment_timeline", "conversion segment timeline diagnostic", new
            {
                conversionAttemptId,
                segment.SegmentIndex,
                segment.InputAudioStartSeconds,
                segment.InputAudioEndSeconds,
                segment.InputVideoStartSeconds,
                segment.InputVideoEndSeconds,
                segment.OutputAudioStartSeconds,
                segment.OutputAudioEndSeconds,
                segment.OutputVideoStartSeconds,
                segment.OutputVideoEndSeconds,
                segment.SourceClockCorrectionSeconds,
                segment.MaximumSourceClockCorrectionSeconds,
                segment.MaximumRecoveryCorrectionSeconds,
                segment.MaximumCombinedTimestampCorrectionSeconds,
                segment.SourceTimestampBaseSeconds,
                segment.TimelineOffsetSeconds,
                segment.AudioPacketCount,
                segment.VideoPacketCount,
                segment.DiscardedPacketCount,
                segment.SourceClockDiscontinuityCount,
                segment.TimelineRecoveryEventCount,
            });
        }
    }

    private static long GetFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    private static string GetLastWriteTimeUtc(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path).ToString("O") : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ComputeSourceHash(string path)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists)
            {
                return string.Empty;
            }
            if (file.Length > 512L * 1024L * 1024L)
            {
                return "skipped:size_limit";
            }
            using FileStream stream = file.OpenRead();
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return "unavailable:" + exception.GetType().Name;
        }
    }

    private static IReadOnlyDictionary<string, string> GetFfmpegLibraryVersions()
    {
        Dictionary<string, string> versions = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in new[] { "avcodec-61.dll", "avformat-61.dll", "avutil-59.dll", "swresample-5.dll", "swscale-8.dll" })
        {
            string path = Path.Combine(FfmpegMediaEngine.LibraryDirectory, name);
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                versions[name] = FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
            }
            catch
            {
                versions[name] = string.Empty;
            }
        }
        return versions;
    }

    private static double RoundTimestamp(double value)
    {
        return Math.Round(Math.Max(0d, value), 6);
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
        string reason = "",
        string conversionAttemptId = "")
    {
        try
        {
            if (FfmpegMediaEngine.TryProbe(path, out FfmpegMediaProbeResult probe, out string error, includePacketDiagnostics: true))
            {
                LogProbe(stage, path, probe, metadata, roomUrl, targetPath, string.IsNullOrWhiteSpace(reason) ? "ok" : "validation_failed", reason, conversionAttemptId: conversionAttemptId);
                return;
            }

            LogProbeFailure(stage, path, string.IsNullOrWhiteSpace(error) ? reason : error, metadata, roomUrl, targetPath, conversionAttemptId);
        }
        catch (Exception exception)
        {
            LogProbeFailure(stage, path, exception.Message, metadata, roomUrl, targetPath, conversionAttemptId);
        }
    }
}
