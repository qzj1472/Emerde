using System.Globalization;
using System.Text.RegularExpressions;

namespace Emerde.Core;

internal sealed record RecordingFinalizationResult(
    bool Success,
    string Path,
    VideoRecordingMetadata Metadata,
    string Error = "");

internal static partial class RecordingFinalizationService
{
    internal const string DefaultRule = "{主播名}_{录制开始时间}_{直播标题}";

    private static readonly object RenameLock = new();

    public static RecordingFinalizationResult FinalizeFile(
        string mediaPath,
        string? rule = null,
        bool hasOptimizedAudio = false,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(mediaPath))
        {
            return new RecordingFinalizationResult(false, mediaPath, new VideoRecordingMetadata(), "media file does not exist");
        }

        FileInfo source = new(mediaPath);
        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(source);
        if (!FfmpegMediaEngine.TryProbe(mediaPath, out FfmpegMediaProbeResult probe, out string probeError, token))
        {
            return new RecordingFinalizationResult(false, mediaPath, metadata, probeError);
        }

        ApplyProbe(metadata, probe, hasOptimizedAudio);
        string effectiveRule = string.IsNullOrWhiteSpace(rule)
            ? string.IsNullOrWhiteSpace(metadata.FileNameRule) ? DefaultRule : metadata.FileNameRule
            : rule;
        metadata.FileNameRule = effectiveRule;
        string segmentSuffix = GetSegmentSuffix(source.Name);
        string stem = BuildFinalStem(effectiveRule, metadata).SanitizeFileName();
        string extension = source.Extension;
        string requestedPath = Path.Combine(source.DirectoryName ?? Environment.CurrentDirectory, stem + segmentSuffix + extension);
        string targetPath;

        lock (RenameLock)
        {
            targetPath = PathsEqual(mediaPath, requestedPath)
                ? mediaPath
                : GetAvailablePath(requestedPath);
            if (!PathsEqual(mediaPath, targetPath))
            {
                File.Move(mediaPath, targetPath, false);
            }
        }

        metadata.FileName = Path.GetFileName(targetPath);
        if (VideoRecordingMetadataStore.WriteCompletedMetadata(targetPath, metadata))
        {
            if (!PathsEqual(mediaPath, targetPath))
            {
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(mediaPath);
            }
            return new RecordingFinalizationResult(true, targetPath, metadata);
        }

        if (!PathsEqual(mediaPath, targetPath))
        {
            lock (RenameLock)
            {
                if (File.Exists(targetPath) && !File.Exists(mediaPath))
                {
                    File.Move(targetPath, mediaPath, false);
                }
            }
        }
        return new RecordingFinalizationResult(false, mediaPath, metadata, "metadata could not be stored");
    }

    public static void RollBackRename(RecordingFinalizationResult result, string originalPath)
    {
        if (!result.Success || PathsEqual(result.Path, originalPath) || !File.Exists(result.Path) || File.Exists(originalPath))
        {
            return;
        }

        lock (RenameLock)
        {
            File.Move(result.Path, originalPath, false);
        }
        result.Metadata.FileName = Path.GetFileName(originalPath);
        _ = VideoRecordingMetadataStore.WriteCompletedMetadata(originalPath, result.Metadata);
        VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(result.Path);
    }

    internal static string BuildTemporaryStem(string nickName)
    {
        string safeNickName = string.IsNullOrWhiteSpace(nickName) ? "Emerde" : nickName.Trim();
        return $"录制中-{safeNickName}".SanitizeFileName();
    }

    internal static string BuildFinalStem(string rule, VideoRecordingMetadata metadata)
    {
        string value = (string.IsNullOrWhiteSpace(rule) ? DefaultRule : rule)
            .Replace("{主播名}", metadata.NickName, StringComparison.Ordinal)
            .Replace("{平台}", metadata.Platform, StringComparison.Ordinal)
            .Replace("{直播标题}", metadata.Title, StringComparison.Ordinal)
            .Replace("{标题}", metadata.Title, StringComparison.Ordinal)
            .Replace("{房间号}", metadata.RoomId, StringComparison.Ordinal)
            .Replace("{分辨率}", metadata.Resolution, StringComparison.Ordinal)
            .Replace("{码率}", metadata.Bitrate, StringComparison.Ordinal)
            .Replace("{帧率}", FormatFrameRate(metadata.FrameRate), StringComparison.Ordinal)
            .Replace("{画质}", metadata.Quality, StringComparison.Ordinal)
            .Replace("{视频编码}", metadata.VideoCodec, StringComparison.Ordinal)
            .Replace("{音频编码}", metadata.AudioCodec, StringComparison.Ordinal)
            .Replace("{优化音频}", metadata.HasOptimizedAudio ? "OptimizedAudioTrack".Tr() : string.Empty, StringComparison.Ordinal)
            .Replace("{录制开始时间}", FormatTimestamp(metadata.RecordedAt), StringComparison.Ordinal)
            .Replace("{录制结束时间}", FormatTimestamp(metadata.EndedAt), StringComparison.Ordinal)
            .Replace("{视频时长}", FormatDuration(metadata.DurationSeconds), StringComparison.Ordinal)
            .Replace("{录制时间}", FormatTimestamp(metadata.RecordedAt), StringComparison.Ordinal)
            .Replace("{日期}", metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty, StringComparison.Ordinal)
            .Replace("{时间}", metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt.ToString("HH-mm-ss", CultureInfo.InvariantCulture) : string.Empty, StringComparison.Ordinal);

        value = UnknownTokenRegex().Replace(value, string.Empty);
        value = RepeatedSeparatorRegex().Replace(value, "$1").Trim(' ', '_', '-');
        return string.IsNullOrWhiteSpace(value)
            ? $"Emerde_{FormatTimestamp(metadata.RecordedAt)}".TrimEnd('_')
            : value;
    }

    private static void ApplyProbe(VideoRecordingMetadata metadata, FfmpegMediaProbeResult probe, bool hasOptimizedAudio)
    {
        metadata.SchemaVersion = 2;
        if (probe.Width > 0 && probe.Height > 0)
        {
            metadata.Resolution = $"{probe.Width}x{probe.Height}";
        }
        if (probe.Bitrate > 0)
        {
            metadata.Bitrate = StreamQualityCatalog.FormatBitrate(probe.Bitrate) ?? metadata.Bitrate;
        }
        metadata.FrameRate = probe.FrameRate > 0 ? probe.FrameRate : metadata.FrameRate;
        metadata.VideoCodec = string.IsNullOrWhiteSpace(probe.VideoCodec) ? metadata.VideoCodec : probe.VideoCodec;
        metadata.AudioCodec = string.IsNullOrWhiteSpace(probe.AudioCodec) ? metadata.AudioCodec : probe.AudioCodec;
        metadata.DurationSeconds = probe.DurationSeconds > 0 ? probe.DurationSeconds : metadata.DurationSeconds;
        metadata.HasOptimizedAudio = metadata.HasOptimizedAudio || hasOptimizedAudio || probe.HasOptimizedAudio;
        if (metadata.RecordedAt > DateTime.MinValue && metadata.DurationSeconds > 0)
        {
            metadata.EndedAt = metadata.RecordedAt.AddSeconds(metadata.DurationSeconds);
        }
    }

    private static string GetSegmentSuffix(string fileName)
    {
        Match match = SegmentSuffixRegex().Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success ? match.Value : string.Empty;
    }

    private static string GetAvailablePath(string requestedPath)
    {
        if (!File.Exists(requestedPath) && !Directory.Exists(requestedPath))
        {
            return requestedPath;
        }

        string directory = Path.GetDirectoryName(requestedPath) ?? Environment.CurrentDirectory;
        string stem = Path.GetFileNameWithoutExtension(requestedPath);
        string extension = Path.GetExtension(requestedPath);
        for (int index = 2; index < 10000; index++)
        {
            string candidate = Path.Combine(directory, $"{stem}_{index}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{extension}");
    }

    private static bool PathsEqual(string first, string second)
    {
        return Path.GetFullPath(first).Equals(Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTimestamp(DateTime value)
    {
        return value > DateTime.MinValue ? value.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatFrameRate(double value)
    {
        return value > 0 ? $"{value.ToString("0.###", CultureInfo.InvariantCulture)}fps" : string.Empty;
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0)
        {
            return string.Empty;
        }
        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 100
            ? $"{Math.Floor(duration.TotalHours):0}-{duration.Minutes:00}-{duration.Seconds:00}"
            : $"{(int)duration.TotalHours:00}-{duration.Minutes:00}-{duration.Seconds:00}";
    }

    [GeneratedRegex(@"\{[^{}]+\}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex UnknownTokenRegex();

    [GeneratedRegex(@"([ _-])\1+", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedSeparatorRegex();

    [GeneratedRegex(@"_\d{3,}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SegmentSuffixRegex();
}
