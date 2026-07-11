using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace Emerde.Core;

internal static class VideoRecordingMetadataStore
{
    private const string MetadataSuffix = ".mplr.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string[] SourceVideoExtensions = [".ts", ".flv"];

    public static VideoRecordingMetadata Load(FileInfo file)
    {
        foreach (string path in GetMetadataCandidates(file))
        {
            try
            {
                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize<VideoRecordingMetadata>(File.ReadAllText(path)) ?? new VideoRecordingMetadata();
                }
            }
            catch
            {
            }
        }

        return new VideoRecordingMetadata();
    }

    public static string? WriteSidecar(string saveFolder, string fileName, VideoRecordingMetadata metadata)
    {
        try
        {
            string metadataPath = Path.Combine(saveFolder, $"{fileName}{MetadataSuffix}");
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
            return metadataPath;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return null;
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
                || !string.IsNullOrWhiteSpace(metadata.Title)
                || !string.IsNullOrWhiteSpace(metadata.Resolution)
                || !string.IsNullOrWhiteSpace(metadata.Bitrate)
                || !string.IsNullOrWhiteSpace(metadata.CoverPath)
                || metadata.RecordedAt > DateTime.MinValue);
    }

    public static VideoRecordingMetadata Merge(VideoRecordingMetadata preferred, VideoRecordingMetadata? fallback)
    {
        if (!HasAnyMetadata(fallback))
        {
            return preferred;
        }

        return new VideoRecordingMetadata
        {
            FileName = First(preferred.FileName, fallback!.FileName),
            NickName = First(preferred.NickName, fallback.NickName),
            RoomUrl = First(preferred.RoomUrl, fallback.RoomUrl),
            Platform = First(preferred.Platform, fallback.Platform),
            Title = First(preferred.Title, fallback.Title),
            Resolution = First(preferred.Resolution, fallback.Resolution),
            Bitrate = First(preferred.Bitrate, fallback.Bitrate),
            CoverPath = First(preferred.CoverPath, fallback.CoverPath),
            RecordedAt = preferred.RecordedAt > DateTime.MinValue ? preferred.RecordedAt : fallback.RecordedAt,
        };
    }

    public static VideoRecordingMetadata WithFileName(VideoRecordingMetadata metadata, string fileName)
    {
        return new VideoRecordingMetadata
        {
            FileName = fileName,
            NickName = metadata.NickName,
            RoomUrl = metadata.RoomUrl,
            Platform = metadata.Platform,
            Title = metadata.Title,
            Resolution = metadata.Resolution,
            Bitrate = metadata.Bitrate,
            CoverPath = metadata.CoverPath,
            RecordedAt = metadata.RecordedAt,
        };
    }

    public static List<string> BuildFfmpegMetadataArguments(VideoRecordingMetadata metadata)
    {
        List<string> arguments = [];

        AddMetadata(arguments, "title", metadata.Title);
        AddMetadata(arguments, "artist", metadata.NickName);
        AddMetadata(arguments, "date", FormatTimestamp(metadata.RecordedAt));
        AddMetadata(arguments, "emerde_file_name", metadata.FileName);
        AddMetadata(arguments, "emerde_nick_name", metadata.NickName);
        AddMetadata(arguments, "emerde_room_url", metadata.RoomUrl);
        AddMetadata(arguments, "emerde_platform", metadata.Platform);
        AddMetadata(arguments, "emerde_title", metadata.Title);
        AddMetadata(arguments, "emerde_resolution", metadata.Resolution);
        AddMetadata(arguments, "emerde_bitrate", metadata.Bitrate);
        AddMetadata(arguments, "emerde_cover_path", metadata.CoverPath);
        AddMetadata(arguments, "emerde_recorded_at", FormatTimestamp(metadata.RecordedAt));

        return arguments;
    }

    public static bool UsesMovMetadataTags(string targetFileName)
    {
        string extension = Path.GetExtension(targetFileName);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase);
    }

    public static VideoRecordingMetadata FromTags(JsonElement tags, string fileName)
    {
        if (tags.ValueKind != JsonValueKind.Object)
        {
            return new VideoRecordingMetadata();
        }

        VideoRecordingMetadata metadata = new()
        {
            FileName = First(GetTag(tags, "emerde_file_name"), fileName),
            NickName = First(GetTag(tags, "emerde_nick_name"), GetTag(tags, "artist")),
            RoomUrl = GetTag(tags, "emerde_room_url"),
            Platform = GetTag(tags, "emerde_platform"),
            Title = First(GetTag(tags, "emerde_title"), GetTag(tags, "title")),
            Resolution = GetTag(tags, "emerde_resolution"),
            Bitrate = GetTag(tags, "emerde_bitrate"),
            CoverPath = GetTag(tags, "emerde_cover_path"),
        };

        string recordedAtText = First(GetTag(tags, "emerde_recorded_at"), GetTag(tags, "creation_time"), GetTag(tags, "date"));
        if (DateTime.TryParse(recordedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime recordedAt))
        {
            metadata.RecordedAt = recordedAt;
        }

        return metadata;
    }

    public static void TryDeleteSidecarIfNoSourceVideosRemain(string sourceFileName)
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

                File.Delete(metadataPath);
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
        foreach (string extension in SourceVideoExtensions)
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
}
