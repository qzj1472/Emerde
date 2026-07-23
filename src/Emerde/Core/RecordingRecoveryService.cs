using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Emerde.Extensions;

namespace Emerde.Core;

internal static class RecordingRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly ConcurrentDictionary<string, Task> ProcessingTasks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly DateTime ProcessStartedAtUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public static string? Register(string sourcePattern, RoomRecordingOptions options)
    {
        string? targetFormat = Recorder.GetTargetFormat(options.RecordFormat);
        if (string.IsNullOrWhiteSpace(sourcePattern) || string.IsNullOrWhiteSpace(targetFormat))
        {
            return null;
        }

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
                RemoveSource = options.IsRemoveTs,
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

    internal static bool UpdateOptions(string path, RoomRecordingOptions options)
    {
        string? targetFormat = Recorder.GetTargetFormat(options.RecordFormat);
        if (string.IsNullOrWhiteSpace(targetFormat))
        {
            DeleteMarker(path);
            return false;
        }

        PendingRecording? item = Load(path, out _, validateAllowedDirectory: false);
        if (item == null)
        {
            return false;
        }

        string temporaryPath = path + ".tmp";
        try
        {
            item.TargetFormat = targetFormat;
            item.RemoveSource = options.IsRemoveTs;
            using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
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

    public static void QueueRun()
    {
        _ = Task.Run(RunStartupMaintenanceAsync);
    }

    private static async Task RunStartupMaintenanceAsync()
    {
        DeleteIncompleteMarkers();
        DeleteStaleTemporaryMediaFiles();
        await ProcessPendingAsync(GetPendingPaths());
        await RecordingCleanupService.RunAsync();
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

    private static async Task ProcessPendingAsync(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            await ProcessAsync(path);
        }
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
        string lockKey = Path.GetFullPath(path);
        Task processingTask = ProcessingTasks.GetOrAdd(lockKey, _ => ProcessCoreAsync(path));
        try
        {
            await processingTask;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            AppSessionLogger.WriteException(e);
        }
        finally
        {
            if (ProcessingTasks.TryGetValue(lockKey, out Task? current) && ReferenceEquals(current, processingTask))
            {
                _ = ProcessingTasks.TryRemove(lockKey, out _);
            }
        }
    }

    private static async Task ProcessCoreAsync(string path)
    {
        PendingRecording? item = Load(path, out string? invalidReason);
        if (item == null)
        {
            QuarantineInvalidMarker(path, invalidReason ?? "恢复标记内容无效");
            return;
        }

        bool completed = await ProcessSourcePatternAsync(item.SourcePattern, item.TargetFormat, item.RemoveSource);
        if (GetSourceFiles(item.SourcePattern).Length == 0)
        {
            DeleteMarker(path);
            return;
        }

        if (completed)
        {
            DeleteMarker(path);
        }
    }

    internal static async Task<bool> ProcessSourcePatternAsync(string sourcePattern, string targetFormat, bool removeSource)
    {
        string[] sources = GetSourceFiles(sourcePattern);
        if (sources.Length == 0)
        {
            return true;
        }

        foreach (string source in sources)
        {
            if (!await new Converter().ExecuteAsync(source, targetFormat))
            {
                return false;
            }

            if (removeSource)
            {
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

        Regex regex = new("^" + Regex.Escape(pattern).Replace("%03d", @"\d{3,}") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Directory.EnumerateFiles(directory)
            .Where(file => regex.IsMatch(Path.GetFileName(file)) && IsUsableSource(file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool IsPendingSourcePath(string path)
    {
        return IsPendingSourcePath(path, GetPendingSourcePatterns());
    }

    internal static string[] GetPendingSourcePatterns()
    {
        return GetPendingPaths()
            .Select(path => Load(path, out _))
            .Where(item => item != null)
            .Select(item => item!.SourcePattern)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private static bool IsUsableSource(string path)
    {
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

    private static PendingRecording? Load(string path, out string? invalidReason, bool validateAllowedDirectory = true)
    {
        invalidReason = null;
        try
        {
            PendingRecording? item = File.Exists(path)
                ? JsonSerializer.Deserialize<PendingRecording>(File.ReadAllText(path))
                : null;
            invalidReason = GetValidationError(item, validateAllowedDirectory);
            return invalidReason == null ? item : null;
        }
        catch (JsonException e)
        {
            AppSessionLogger.WriteException(e);
            invalidReason = $"JSON 语法损坏：{e.Message}";
            return null;
        }
    }

    private static string? GetValidationError(PendingRecording? item, bool validateAllowedDirectory)
    {
        if (item == null)
        {
            return "恢复标记为空";
        }
        if (string.IsNullOrWhiteSpace(item.SourcePattern) || !Path.IsPathFullyQualified(item.SourcePattern))
        {
            return "源文件路径不是有效的绝对路径";
        }
        if (!MediaFileCatalog.IsMediaPath(item.SourcePattern))
        {
            return "源文件不是受支持的媒体格式";
        }
        if (item.SourcePattern.Contains('*') || item.SourcePattern.Contains('?'))
        {
            return "源文件路径包含不允许的通配符";
        }

        string fileName = Path.GetFileName(item.SourcePattern);
        if (fileName.Replace("%03d", string.Empty, StringComparison.Ordinal).Contains('%'))
        {
            return "分段占位符只能使用 %03d";
        }

        if (item.TargetFormat is not (".mp4" or ".mkv"))
        {
            return "目标格式只能是 MP4 或 MKV";
        }

        string sourcePath;
        try
        {
            sourcePath = Path.GetFullPath(item.SourcePattern);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"源文件路径无效：{e.Message}";
        }

        if (!validateAllowedDirectory)
        {
            return null;
        }

        return MediaFileCatalog.GetConfiguredSaveFolders()
            .Concat(SaveFolderHelper.GetFallbackSaveFolders())
            .Any(root => IsPathWithinRoot(sourcePath, root))
            ? null
            : "源文件不在当前配置的保存目录中";
    }

    internal static bool IsPathWithinRoot(string path, string root)
    {
        try
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
            return !Path.IsPathRooted(relative)
                && !relative.Equals("..", StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void DeleteStaleTemporaryMediaFiles()
    {
        EnumerationOptions options = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (string root in MediaFileCatalog.GetConfiguredSaveFolders().Where(Directory.Exists))
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
    }
}
