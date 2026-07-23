using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Emerde.Core;

internal static class RecordingRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly ConcurrentDictionary<string, Task> ProcessingTasks = new(StringComparer.OrdinalIgnoreCase);

    public static string? Register(string sourcePattern, RoomRecordingOptions options)
    {
        string? targetFormat = Recorder.GetTargetFormat(options.RecordFormat);
        if (string.IsNullOrWhiteSpace(sourcePattern) || string.IsNullOrWhiteSpace(targetFormat))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.PendingRecordingsDirectory);
            string path = Path.Combine(AppPaths.PendingRecordingsDirectory, $"{Guid.NewGuid():N}.json");
            string temporaryPath = path + ".tmp";
            PendingRecording item = new()
            {
                SourcePattern = sourcePattern,
                TargetFormat = targetFormat,
                RemoveSource = options.IsRemoveTs,
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(item, JsonOptions));
            File.Move(temporaryPath, path);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
            return null;
        }
    }

    public static void QueueRun()
    {
        string[] paths = GetPendingPaths();
        if (paths.Length > 0)
        {
            _ = Task.Run(() => ProcessPendingAsync(paths));
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
        PendingRecording? item = Load(path);
        if (item == null)
        {
            QuarantineInvalidMarker(path);
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

    private static PendingRecording? Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<PendingRecording>(File.ReadAllText(path))
                : null;
        }
        catch (JsonException e)
        {
            AppSessionLogger.WriteException(e);
            return null;
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

    private static void QuarantineInvalidMarker(string path)
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

    private sealed class PendingRecording
    {
        public string SourcePattern { get; set; } = string.Empty;

        public string TargetFormat { get; set; } = string.Empty;

        public bool RemoveSource { get; set; }
    }
}
