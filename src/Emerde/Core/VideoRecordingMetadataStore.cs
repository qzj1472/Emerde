using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Emerde.Core;

internal static class VideoRecordingMetadataStore
{
    internal const string TimelineStallSegmentReason = "timeline_stall";

    private const string MetadataSuffix = ".mplr.json";
    private const string AttachedMetadataStream = ":emerde.metadata";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string[] AssociatedVideoExtensions = [".ts", ".flv", ".mp4", ".mkv", ".mov", ".m4v", ".webm", ".avi"];

    public static VideoRecordingMetadata Load(FileInfo file)
    {
        VideoRecordingMetadata? attached = ReadAttachedMetadata(file.FullName);
        if (attached != null)
        {
            return attached;
        }

        foreach (string path in GetMetadataCandidates(file))
        {
            try
            {
                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize<VideoRecordingMetadata>(File.ReadAllText(path)) ?? new VideoRecordingMetadata();
                }
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(e);
                QuarantineCorruptSidecar(path);
            }
        }

        return new VideoRecordingMetadata();
    }

    public static bool WriteCompletedMetadata(string mediaPath, VideoRecordingMetadata metadata)
    {
        VideoRecordingMetadata completed = WithFileName(metadata, Path.GetFileName(mediaPath));
        if (WriteAttachedMetadata(mediaPath, completed))
        {
            RecordingCleanupService.TrackFile(mediaPath, completed);
            return true;
        }

        string directory = Path.GetDirectoryName(mediaPath) ?? Environment.CurrentDirectory;
        bool written = WriteSidecar(directory, Path.GetFileNameWithoutExtension(mediaPath), completed) != null;
        if (written)
        {
            RecordingCleanupService.TrackFile(mediaPath, completed);
        }
        return written;
    }

    public static bool FinalizeSidecarForMedia(IEnumerable<string> mediaPaths, string? metadataPath)
    {
        string[] paths = mediaPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0 || string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
        {
            return false;
        }

        VideoRecordingMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<VideoRecordingMetadata>(File.ReadAllText(metadataPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            AppSessionLogger.WriteException(e);
            return false;
        }

        if (!HasAnyMetadata(metadata)
            || paths.Any(path => !WriteAttachedMetadata(path, WithFileName(metadata!, Path.GetFileName(path)))))
        {
            return false;
        }

        foreach (string path in paths)
        {
            RecordingCleanupService.TrackFile(path, WithFileName(metadata!, Path.GetFileName(path)));
        }

        try
        {
            File.Delete(metadataPath);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            return false;
        }
    }

    internal static bool HasAttachedMetadata(string mediaPath)
    {
        return HasAnyMetadata(ReadAttachedMetadata(mediaPath));
    }

    public static string? WriteSidecar(string saveFolder, string fileName, VideoRecordingMetadata metadata)
    {
        try
        {
            string metadataPath = Path.Combine(saveFolder, $"{fileName}{MetadataSuffix}");
            using StagedVideoMetadata? staged = StageSidecarPath(metadataPath, metadata, "metadata");
            return staged?.Commit();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return null;
        }
    }

    public static StagedVideoMetadata? StageSidecarForMedia(
        string mediaPath,
        VideoRecordingMetadata metadata,
        string purpose)
    {
        try
        {
            string metadataPath = GetDirectMetadataPath(new FileInfo(mediaPath));
            return StageSidecarPath(metadataPath, WithFileName(metadata, Path.GetFileName(mediaPath)), purpose);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return null;
        }
    }

    private static StagedVideoMetadata StageSidecarPath(
        string metadataPath,
        VideoRecordingMetadata metadata,
        string purpose)
    {
        string temporaryPath = MediaFileCatalog.CreateTemporaryPath(metadataPath, purpose);
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(JsonSerializer.Serialize(metadata, JsonOptions));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            return new StagedVideoMetadata(temporaryPath, metadataPath);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(e);
            }
            throw;
        }
    }

    public static IEnumerable<string> GetMetadataCandidates(FileInfo file)
    {
        yield return GetDirectMetadataPath(file);

        if (TryGetSegmentBaseStem(file, out string baseStem))
        {
            yield return GetSharedSegmentMetadataPath(file, baseStem);
        }
    }

    internal static string GetSidecarStem(string metadataPath)
    {
        string fileName = Path.GetFileName(metadataPath);
        return fileName.EndsWith(MetadataSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^MetadataSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    public static bool HasValidSidecar(FileInfo file)
    {
        foreach (string path in GetMetadataCandidates(file))
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                VideoRecordingMetadata? metadata = JsonSerializer.Deserialize<VideoRecordingMetadata>(File.ReadAllText(path));
                if (HasAnyMetadata(metadata))
                {
                    return true;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                AppSessionLogger.WriteException(e);
            }
        }

        return false;
    }

    public static bool HasValidMetadata(FileInfo file)
    {
        return HasAttachedMetadata(file.FullName) || HasValidSidecar(file);
    }

    public static bool NeedsEmbeddedMetadataProbe(VideoRecordingMetadata metadata)
    {
        return string.IsNullOrWhiteSpace(metadata.NickName)
            && string.IsNullOrWhiteSpace(metadata.Title)
            && string.IsNullOrWhiteSpace(metadata.RoomUrl)
            && string.IsNullOrWhiteSpace(metadata.Platform)
            && metadata.RecordedAt <= DateTime.MinValue;
    }

    public static bool HasAnyMetadata(VideoRecordingMetadata? metadata)
    {
        return metadata != null
            && (!string.IsNullOrWhiteSpace(metadata.FileName)
                || !string.IsNullOrWhiteSpace(metadata.NickName)
                || !string.IsNullOrWhiteSpace(metadata.RoomUrl)
                || !string.IsNullOrWhiteSpace(metadata.Platform)
                || !string.IsNullOrWhiteSpace(metadata.RoomId)
                || !string.IsNullOrWhiteSpace(metadata.Title)
                || !string.IsNullOrWhiteSpace(metadata.Resolution)
                || !string.IsNullOrWhiteSpace(metadata.Bitrate)
                || metadata.FrameRate > 0
                || !string.IsNullOrWhiteSpace(metadata.Quality)
                || !string.IsNullOrWhiteSpace(metadata.VideoCodec)
                || !string.IsNullOrWhiteSpace(metadata.AudioCodec)
                || metadata.HasOptimizedAudio
                || !string.IsNullOrWhiteSpace(metadata.CoverPath)
                || metadata.RecordingAvatar.Length > 0
                || metadata.CoverCompositionVersion > 0
                || !string.IsNullOrWhiteSpace(metadata.SegmentReason)
                || !string.IsNullOrWhiteSpace(metadata.RecordingSessionId)
                || !string.IsNullOrWhiteSpace(metadata.SegmentGroupId)
                || metadata.SegmentIndex >= 0
                || metadata.SegmentCount > 0
                || !string.IsNullOrWhiteSpace(metadata.SegmentKind)
                || !string.IsNullOrWhiteSpace(metadata.MediaIssue)
                || metadata.WasRepaired
                || metadata.RecordedAt > DateTime.MinValue
                || metadata.EndedAt > DateTime.MinValue
                || metadata.DurationSeconds > 0);
    }

    public static VideoRecordingMetadata Merge(VideoRecordingMetadata preferred, VideoRecordingMetadata? fallback)
    {
        if (!HasAnyMetadata(fallback))
        {
            return preferred;
        }

        return new VideoRecordingMetadata
        {
            SchemaVersion = Math.Max(preferred.SchemaVersion, fallback!.SchemaVersion),
            RecordingSessionId = First(preferred.RecordingSessionId, fallback.RecordingSessionId),
            SegmentGroupId = First(preferred.SegmentGroupId, fallback.SegmentGroupId),
            SegmentIndex = preferred.SegmentIndex >= 0 ? preferred.SegmentIndex : fallback.SegmentIndex,
            SegmentCount = preferred.SegmentCount > 0 ? preferred.SegmentCount : fallback.SegmentCount,
            SegmentKind = First(preferred.SegmentKind, fallback.SegmentKind),
            MediaIssue = First(preferred.MediaIssue, fallback.MediaIssue),
            WasRepaired = preferred.WasRepaired || fallback.WasRepaired,
            FileName = First(preferred.FileName, fallback!.FileName),
            NickName = First(preferred.NickName, fallback.NickName),
            RoomUrl = First(preferred.RoomUrl, fallback.RoomUrl),
            Platform = First(preferred.Platform, fallback.Platform),
            RoomId = First(preferred.RoomId, fallback.RoomId),
            Title = First(preferred.Title, fallback.Title),
            Resolution = First(preferred.Resolution, fallback.Resolution),
            Bitrate = First(preferred.Bitrate, fallback.Bitrate),
            FrameRate = preferred.FrameRate > 0 ? preferred.FrameRate : fallback.FrameRate,
            Quality = First(preferred.Quality, fallback.Quality),
            VideoCodec = First(preferred.VideoCodec, fallback.VideoCodec),
            AudioCodec = First(preferred.AudioCodec, fallback.AudioCodec),
            HasOptimizedAudio = preferred.HasOptimizedAudio || fallback.HasOptimizedAudio,
            CoverPath = First(preferred.CoverPath, fallback.CoverPath),
            RecordingAvatar = preferred.RecordingAvatar.Length > 0 ? preferred.RecordingAvatar : fallback.RecordingAvatar,
            CoverCompositionVersion = Math.Max(preferred.CoverCompositionVersion, fallback.CoverCompositionVersion),
            SegmentReason = First(preferred.SegmentReason, fallback.SegmentReason),
            RecordedAt = preferred.RecordedAt > DateTime.MinValue ? preferred.RecordedAt : fallback.RecordedAt,
            EndedAt = preferred.EndedAt > DateTime.MinValue ? preferred.EndedAt : fallback.EndedAt,
            DurationSeconds = preferred.DurationSeconds > 0 ? preferred.DurationSeconds : fallback.DurationSeconds,
            FileNameRule = First(preferred.FileNameRule, fallback.FileNameRule),
        };
    }

    public static VideoRecordingMetadata WithFileName(VideoRecordingMetadata metadata, string fileName)
    {
        return new VideoRecordingMetadata
        {
            SchemaVersion = metadata.SchemaVersion,
            RecordingSessionId = metadata.RecordingSessionId,
            SegmentGroupId = metadata.SegmentGroupId,
            SegmentIndex = metadata.SegmentIndex,
            SegmentCount = metadata.SegmentCount,
            SegmentKind = metadata.SegmentKind,
            MediaIssue = metadata.MediaIssue,
            WasRepaired = metadata.WasRepaired,
            FileName = fileName,
            NickName = metadata.NickName,
            RoomUrl = metadata.RoomUrl,
            Platform = metadata.Platform,
            RoomId = metadata.RoomId,
            Title = metadata.Title,
            Resolution = metadata.Resolution,
            Bitrate = metadata.Bitrate,
            FrameRate = metadata.FrameRate,
            Quality = metadata.Quality,
            VideoCodec = metadata.VideoCodec,
            AudioCodec = metadata.AudioCodec,
            HasOptimizedAudio = metadata.HasOptimizedAudio,
            CoverPath = metadata.CoverPath,
            RecordingAvatar = metadata.RecordingAvatar,
            CoverCompositionVersion = metadata.CoverCompositionVersion,
            SegmentReason = metadata.SegmentReason,
            RecordedAt = metadata.RecordedAt,
            EndedAt = metadata.EndedAt,
            DurationSeconds = metadata.DurationSeconds,
            FileNameRule = metadata.FileNameRule,
        };
    }

    public static List<string> BuildFfmpegMetadataArguments(VideoRecordingMetadata metadata)
    {
        List<string> arguments = [];

        AddMetadata(arguments, "title", metadata.Title);
        AddMetadata(arguments, "artist", metadata.NickName);
        AddMetadata(arguments, "date", FormatTimestamp(metadata.RecordedAt));
        AddMetadata(arguments, "emerde_file_name", metadata.FileName);
        AddMetadata(arguments, "emerde_recording_session_id", metadata.RecordingSessionId);
        AddMetadata(arguments, "emerde_segment_group_id", metadata.SegmentGroupId);
        AddMetadata(arguments, "emerde_segment_index", metadata.SegmentIndex >= 0 ? metadata.SegmentIndex.ToString(CultureInfo.InvariantCulture) : string.Empty);
        AddMetadata(arguments, "emerde_segment_count", metadata.SegmentCount > 0 ? metadata.SegmentCount.ToString(CultureInfo.InvariantCulture) : string.Empty);
        AddMetadata(arguments, "emerde_segment_kind", metadata.SegmentKind);
        AddMetadata(arguments, "emerde_media_issue", metadata.MediaIssue);
        AddMetadata(arguments, "emerde_was_repaired", metadata.WasRepaired ? bool.TrueString : string.Empty);
        AddMetadata(arguments, "emerde_nick_name", metadata.NickName);
        AddMetadata(arguments, "emerde_room_url", metadata.RoomUrl);
        AddMetadata(arguments, "emerde_platform", metadata.Platform);
        AddMetadata(arguments, "emerde_room_id", metadata.RoomId);
        AddMetadata(arguments, "emerde_title", metadata.Title);
        AddMetadata(arguments, "emerde_resolution", metadata.Resolution);
        AddMetadata(arguments, "emerde_bitrate", metadata.Bitrate);
        AddMetadata(arguments, "emerde_frame_rate", FormatNumber(metadata.FrameRate));
        AddMetadata(arguments, "emerde_quality", metadata.Quality);
        AddMetadata(arguments, "emerde_video_codec", metadata.VideoCodec);
        AddMetadata(arguments, "emerde_audio_codec", metadata.AudioCodec);
        AddMetadata(arguments, "emerde_optimized_audio", metadata.HasOptimizedAudio ? bool.TrueString : string.Empty);
        AddMetadata(arguments, "emerde_cover_path", metadata.CoverPath);
        AddMetadata(arguments, "emerde_cover_composition_version", metadata.CoverCompositionVersion > 0 ? metadata.CoverCompositionVersion.ToString(CultureInfo.InvariantCulture) : string.Empty);
        AddMetadata(arguments, "emerde_segment_reason", metadata.SegmentReason);
        AddMetadata(arguments, "emerde_recorded_at", FormatTimestamp(metadata.RecordedAt));
        AddMetadata(arguments, "emerde_ended_at", FormatTimestamp(metadata.EndedAt));
        AddMetadata(arguments, "emerde_duration_seconds", FormatNumber(metadata.DurationSeconds));
        AddMetadata(arguments, "emerde_file_name_rule", metadata.FileNameRule);

        return arguments;
    }

    public static bool UsesMovMetadataTags(string targetFileName)
    {
        string extension = Path.GetExtension(targetFileName);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase);
    }

    public static VideoRecordingMetadata FromTags(JsonElement tags, string fileName)
    {
        if (tags.ValueKind != JsonValueKind.Object)
        {
            return new VideoRecordingMetadata();
        }

        VideoRecordingMetadata metadata = new()
        {
            RecordingSessionId = GetTag(tags, "emerde_recording_session_id"),
            SegmentGroupId = GetTag(tags, "emerde_segment_group_id"),
            SegmentIndex = ParseInteger(GetTag(tags, "emerde_segment_index"), -1),
            SegmentCount = ParseInteger(GetTag(tags, "emerde_segment_count"), 0),
            SegmentKind = GetTag(tags, "emerde_segment_kind"),
            MediaIssue = GetTag(tags, "emerde_media_issue"),
            WasRepaired = ParseBoolean(GetTag(tags, "emerde_was_repaired")),
            FileName = First(GetTag(tags, "emerde_file_name"), fileName),
            NickName = First(GetTag(tags, "emerde_nick_name"), GetTag(tags, "artist")),
            RoomUrl = GetTag(tags, "emerde_room_url"),
            Platform = GetTag(tags, "emerde_platform"),
            RoomId = GetTag(tags, "emerde_room_id"),
            Title = First(GetTag(tags, "emerde_title"), GetTag(tags, "title")),
            Resolution = GetTag(tags, "emerde_resolution"),
            Bitrate = GetTag(tags, "emerde_bitrate"),
            FrameRate = ParseNumber(GetTag(tags, "emerde_frame_rate")),
            Quality = GetTag(tags, "emerde_quality"),
            VideoCodec = GetTag(tags, "emerde_video_codec"),
            AudioCodec = GetTag(tags, "emerde_audio_codec"),
            HasOptimizedAudio = ParseBoolean(GetTag(tags, "emerde_optimized_audio")),
            CoverPath = GetTag(tags, "emerde_cover_path"),
            CoverCompositionVersion = ParseInteger(GetTag(tags, "emerde_cover_composition_version"), 0),
            SegmentReason = GetTag(tags, "emerde_segment_reason"),
            DurationSeconds = ParseNumber(GetTag(tags, "emerde_duration_seconds")),
            FileNameRule = GetTag(tags, "emerde_file_name_rule"),
        };

        string recordedAtText = First(GetTag(tags, "emerde_recorded_at"), GetTag(tags, "creation_time"), GetTag(tags, "date"));
        if (DateTime.TryParse(recordedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime recordedAt))
        {
            metadata.RecordedAt = recordedAt;
        }
        if (DateTime.TryParse(GetTag(tags, "emerde_ended_at"), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime endedAt))
        {
            metadata.EndedAt = endedAt;
        }

        return metadata;
    }

    public static VideoRecordingMetadata FromTags(IReadOnlyDictionary<string, string> tags, string fileName)
    {
        string Get(string key)
        {
            return tags.TryGetValue(key, out string? value) ? value : string.Empty;
        }

        VideoRecordingMetadata metadata = new()
        {
            RecordingSessionId = Get("emerde_recording_session_id"),
            SegmentGroupId = Get("emerde_segment_group_id"),
            SegmentIndex = ParseInteger(Get("emerde_segment_index"), -1),
            SegmentCount = ParseInteger(Get("emerde_segment_count"), 0),
            SegmentKind = Get("emerde_segment_kind"),
            MediaIssue = Get("emerde_media_issue"),
            WasRepaired = ParseBoolean(Get("emerde_was_repaired")),
            FileName = First(Get("emerde_file_name"), fileName),
            NickName = First(Get("emerde_nick_name"), Get("artist")),
            RoomUrl = Get("emerde_room_url"),
            Platform = Get("emerde_platform"),
            RoomId = Get("emerde_room_id"),
            Title = First(Get("emerde_title"), Get("title")),
            Resolution = Get("emerde_resolution"),
            Bitrate = Get("emerde_bitrate"),
            FrameRate = ParseNumber(Get("emerde_frame_rate")),
            Quality = Get("emerde_quality"),
            VideoCodec = Get("emerde_video_codec"),
            AudioCodec = Get("emerde_audio_codec"),
            HasOptimizedAudio = ParseBoolean(Get("emerde_optimized_audio")),
            CoverPath = Get("emerde_cover_path"),
            CoverCompositionVersion = ParseInteger(Get("emerde_cover_composition_version"), 0),
            SegmentReason = Get("emerde_segment_reason"),
            DurationSeconds = ParseNumber(Get("emerde_duration_seconds")),
            FileNameRule = Get("emerde_file_name_rule"),
        };

        string recordedAtText = First(Get("emerde_recorded_at"), Get("creation_time"), Get("date"));
        if (DateTime.TryParse(recordedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime recordedAt))
        {
            metadata.RecordedAt = recordedAt;
        }
        if (DateTime.TryParse(Get("emerde_ended_at"), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime endedAt))
        {
            metadata.EndedAt = endedAt;
        }
        return metadata;
    }

    public static bool HasEmerdeTags(IReadOnlyDictionary<string, string> tags)
    {
        bool Has(string key)
        {
            return tags.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value);
        }

        return Has("emerde_room_url")
            || Has("emerde_recorded_at") && (Has("emerde_platform") || Has("emerde_nick_name"));
    }

    public static void TryDeleteSidecarIfNoSourceVideosRemain(string sourceFileName, bool sendToRecycleBin = false)
    {
        FileInfo source = new(sourceFileName);
        foreach (string metadataPath in GetMetadataCandidates(source).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(metadataPath) || HasRemainingSourceVideo(metadataPath))
                {
                    continue;
                }

                if (sendToRecycleBin)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        metadataPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    File.Delete(metadataPath);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }
    }

    private static string GetDirectMetadataPath(FileInfo file)
    {
        string directory = file.DirectoryName ?? string.Empty;
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(file.Name)}{MetadataSuffix}");
    }

    private static string GetSharedSegmentMetadataPath(FileInfo file, string baseStem)
    {
        string directory = file.DirectoryName ?? string.Empty;
        return Path.Combine(directory, $"{baseStem}{MetadataSuffix}");
    }

    private static bool TryGetSegmentBaseStem(FileInfo file, out string baseStem)
    {
        string stem = Path.GetFileNameWithoutExtension(file.Name);
        int separatorIndex = stem.LastIndexOf('_');
        if (separatorIndex > 0
            && separatorIndex < stem.Length - 1
            && int.TryParse(stem[(separatorIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            baseStem = stem[..separatorIndex];
            return true;
        }

        baseStem = string.Empty;
        return false;
    }

    private static bool HasRemainingSourceVideo(string metadataPath)
    {
        string? directory = Path.GetDirectoryName(metadataPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        string fileName = Path.GetFileName(metadataPath);
        if (!fileName.EndsWith(MetadataSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string stem = fileName[..^MetadataSuffix.Length];
        foreach (string extension in AssociatedVideoExtensions)
        {
            if (File.Exists(Path.Combine(directory, $"{stem}{extension}")))
            {
                return true;
            }

            if (Directory.EnumerateFiles(directory, $"{stem}_*{extension}", SearchOption.TopDirectoryOnly)
                .Any(file => TryGetSegmentBaseStem(new FileInfo(file), out string baseStem)
                    && string.Equals(baseStem, stem, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static int DeleteOrphanedSidecars(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        int deleted = 0;
        EnumerationOptions options = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        try
        {
            foreach (string metadataPath in Directory.EnumerateFiles(root, $"*{MetadataSuffix}", options))
            {
                if (HasRemainingSourceVideo(metadataPath))
                {
                    continue;
                }

                try
                {
                    File.Delete(metadataPath);
                    deleted++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    AppSessionLogger.WriteException(e);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
        }
        return deleted;
    }

    private static void QuarantineCorruptSidecar(string path)
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
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    private static void AddMetadata(List<string> arguments, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        arguments.Add("-metadata");
        arguments.Add($"{key}={value.Trim()}");
    }

    private static string GetTag(JsonElement tags, string key)
    {
        foreach (JsonProperty property in tags.EnumerateObject())
        {
            if (property.NameEquals(key) || string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
            }
        }

        return string.Empty;
    }

    private static string First(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string FormatTimestamp(DateTime value)
    {
        return value > DateTime.MinValue
            ? value.ToString("O", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string FormatNumber(double value)
    {
        return value > 0 ? value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
    }

    private static double ParseNumber(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0
            ? parsed
            : 0;
    }

    private static int ParseInteger(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    private static bool ParseBoolean(string value)
    {
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private static VideoRecordingMetadata? ReadAttachedMetadata(string mediaPath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(mediaPath))
        {
            return null;
        }

        SafeFileHandle handle = CreateFile(
            mediaPath + AttachedMetadataStream,
            GenericRead,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileAttributes.Normal,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        try
        {
            using FileStream stream = new(handle, FileAccess.Read);
            return JsonSerializer.Deserialize<VideoRecordingMetadata>(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            AppSessionLogger.WriteException(e);
            return null;
        }
    }

    private static bool WriteAttachedMetadata(string mediaPath, VideoRecordingMetadata metadata)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(mediaPath))
        {
            return false;
        }

        SafeFileHandle handle = CreateFile(
            mediaPath + AttachedMetadataStream,
            GenericWrite,
            FileShare.Read | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Create,
            FileAttributes.Normal,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return false;
        }

        try
        {
            using FileStream stream = new(handle, FileAccess.Write);
            JsonSerializer.Serialize(stream, metadata, JsonOptions);
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppSessionLogger.WriteException(e);
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        FileAttributes flagsAndAttributes,
        IntPtr templateFile);
}

internal sealed class StagedVideoMetadata(string temporaryPath, string finalPath) : IDisposable
{
    private string? pendingPath = temporaryPath;

    public string FinalPath { get; } = finalPath;

    public string Commit()
    {
        string source = pendingPath ?? throw new InvalidOperationException("The staged metadata has already been committed.");
        File.Move(source, FinalPath, overwrite: true);
        pendingPath = null;
        return FinalPath;
    }

    public void DeleteCommitted()
    {
        try
        {
            File.Delete(FinalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    public void Dispose()
    {
        if (pendingPath == null)
        {
            return;
        }

        try
        {
            File.Delete(pendingPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
        }
        pendingPath = null;
    }
}
