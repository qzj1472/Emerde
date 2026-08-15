using System.Diagnostics;
using Emerde.Core;

namespace Emerde.Extensions;

internal static class ConfigurationMigrationHelper
{
    public static void MigrateLegacyConfiguration()
    {
        MigrateRootConfigurationFiles();
        RemoveTransientRoomFields();
        MigrateLegacyThumbnailCache(
            Path.Combine(AppPaths.ConfigDirectory, "video_thumbnails"),
            AppPaths.ThumbnailCacheDirectory);
    }

    private static void RemoveTransientRoomFields()
    {
        foreach (string path in AppPaths.GetConfigFiles())
        {
            try
            {
                _ = ConfigFileManager.RemoveTransientRoomFields(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
            {
                Debug.WriteLine(e);
            }
        }
    }

    internal static void MigrateLegacyThumbnailCache(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory)
            || string.Equals(Path.GetFullPath(sourceDirectory), Path.GetFullPath(targetDirectory), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                string targetPath = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    File.Delete(sourcePath);
                    continue;
                }

                File.Move(sourcePath, targetPath);
            }

            if (!Directory.EnumerateFileSystemEntries(sourceDirectory).Any())
            {
                Directory.Delete(sourceDirectory);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    private static void MigrateRootConfigurationFiles()
    {
        string rootDirectory = AppPaths.ConfigDirectory;
        string targetDirectory = AppPaths.ConfigFilesDirectory;
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            foreach (string sourcePath in Directory.EnumerateFiles(rootDirectory, "config*.yml", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(rootDirectory, "config*.yaml", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string sourceDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
                if (string.Equals(Path.GetFullPath(sourceDirectory), Path.GetFullPath(targetDirectory), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string targetPath = GetAvailableTargetPath(targetDirectory, Path.GetFileName(sourcePath));
                File.Move(sourcePath, targetPath);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    private static string GetAvailableTargetPath(string directory, string fileName)
    {
        string targetPath = Path.Combine(directory, fileName);
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        string name = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int index = 2; ; index++)
        {
            targetPath = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(targetPath))
            {
                return targetPath;
            }
        }
    }
}
