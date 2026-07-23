using Emerde.Extensions;

namespace Emerde.Core;

internal static class MediaFileCatalog
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts",
        ".flv",
        ".mp4",
        ".mkv",
        ".mov",
        ".m4v",
        ".webm",
        ".avi",
    };

    public static bool IsMediaPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && MediaExtensions.Contains(Path.GetExtension(path));
    }

    public static bool IsApplicationTemporaryPath(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.StartsWith(".emerde-", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".emerde-", StringComparison.OrdinalIgnoreCase);
    }

    public static string CreateTemporaryPath(string targetPath, string purpose)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        string extension = Path.GetExtension(targetPath);
        string safePurpose = string.IsNullOrWhiteSpace(purpose) ? "media" : purpose;
        return Path.Combine(directory, $".emerde-{safePurpose}-{Guid.NewGuid():N}{extension}");
    }

    public static string[] GetConfiguredSaveFolders(bool createDirectories = false)
    {
        List<string?> configuredFolders = [Configurations.SaveFolder.Get()];
        configuredFolders.AddRange((Configurations.Rooms.Get() ?? [])
            .Where(room => !room.IsFollowGlobalSettings && !string.IsNullOrWhiteSpace(room.SaveFolder))
            .Select(room => room.SaveFolder));

        HashSet<string> folders = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? configuredFolder in configuredFolders)
        {
            if (SaveFolderHelper.TryGetSaveFolder(configuredFolder, createDirectories, out string folder, out Exception? error))
            {
                folders.Add(folder);
            }
            else if (error != null)
            {
                AppSessionLogger.Event("warn", "storage", "save_folder_unavailable", error.Message, new { configuredFolder });
            }
        }

        return [.. folders];
    }
}
