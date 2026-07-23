using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Emerde.Core;

internal static class VideoRecordingMetadataStore
{
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
            return true;
        }

        string directory = Path.GetDirectoryName(mediaPath) ?? Environment.CurrentDirectory;
        return WriteSidecar(directory, Path.GetFileNameWithoutExtension(mediaPath), completed) != null;
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
