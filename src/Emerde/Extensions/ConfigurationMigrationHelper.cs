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
        string[] paths;
        try
        {
            paths = AppPaths.GetConfigFiles();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
        {
            Debug.WriteLine(e);
            return;
        }

        foreach (string path in paths)
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

        List<(string Source, string Target)> movedFiles = [];
        try
        {
            string[] sourcePaths = Directory.EnumerateFiles(rootDirectory, "config*.yml", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(rootDirectory, "config*.yaml", SearchOption.TopDirectoryOnly))
                .Where(sourcePath => !string.Equals(
                    Path.GetFullPath(Path.GetDirectoryName(sourcePath) ?? string.Empty),
                    Path.GetFullPath(targetDirectory),
                    StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Directory.CreateDirectory(targetDirectory);
            foreach (string sourcePath in sourcePaths)
            {
                string targetPath = GetAvailableTargetPath(targetDirectory, Path.GetFileName(sourcePath));
                File.Move(sourcePath, targetPath);
                movedFiles.Add((sourcePath, targetPath));
            }
        }
        catch (Exception migrationException)
        {
            List<Exception> rollbackErrors = [];
            foreach ((string sourcePath, string targetPath) in movedFiles.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(targetPath) && !File.Exists(sourcePath))
                    {
                        File.Move(targetPath, sourcePath);
                    }
                }
                catch (Exception exception)
                {
                    rollbackErrors.Add(exception);
                }
            }

            Debug.WriteLine(rollbackErrors.Count > 0
                ? new AggregateException("Legacy configuration migration failed and rollback was incomplete.", [migrationException, .. rollbackErrors])
                : migrationException);
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
