using System.Diagnostics;

namespace Emerde.Extensions;

internal static class SaveFolderHelper
{
    internal const string DefaultSaveFolderName = "EmerdeDownloads";

    public static string GetSaveFolder(string? settingsFolder = null)
    {
        if (TryGetSaveFolder(settingsFolder, createDirectory: true, out string folder, out Exception? error))
        {
            return folder;
        }

        throw new IOException($"The configured save folder is unavailable: {settingsFolder}", error);
    }

    internal static bool TryGetSaveFolder(string? settingsFolder, bool createDirectory, out string folder, out Exception? error)
    {
        string path = string.IsNullOrWhiteSpace(settingsFolder) ? GetDefaultSaveFolder() : settingsFolder;
        try
        {
            folder = Path.GetFullPath(path);
            if (createDirectory && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine(e);
            folder = string.Empty;
            error = e;
            return false;
        }
    }

    public static SaveFolderResolution ResolveForRecording(string? settingsFolder)
    {
        if (TryGetWritableSaveFolder(settingsFolder, out string folder, out Exception? error))
        {
            return new SaveFolderResolution(folder, false, null);
        }

        Exception? configuredError = error;
        string[] fallbacks =
        [
            Configurations.SaveFolder.Get() ?? string.Empty,
            .. GetFallbackSaveFolders(),
        ];
        foreach (string fallback in fallbacks.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(fallback, settingsFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryGetWritableSaveFolder(fallback, out folder, out _))
            {
                return new SaveFolderResolution(folder, true, configuredError);
            }
        }

        throw new IOException($"The configured save folder is unavailable: {settingsFolder}", configuredError);
    }

    internal static string[] GetFallbackSaveFolders()
    {
        return
        [
            GetDefaultSaveFolder(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppConfig.PackName, DefaultSaveFolderName),
            Path.Combine(AppContext.BaseDirectory, "downloads"),
        ];
    }

    internal static string GetDefaultSaveFolder()
    {
        string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? string.Empty;
        string documentsRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return SelectDefaultSaveFolder(
            systemRoot,
            GetFixedReadyDriveRoots(),
            documentsRoot);
    }

    internal static string SelectDefaultSaveFolder(
        string systemRoot,
        IEnumerable<string> fixedReadyDriveRoots,
        string documentsRoot)
    {
        string normalizedSystemRoot = NormalizeDriveRoot(systemRoot);
        string? preferredDriveRoot = fixedReadyDriveRoots
            .Select(NormalizeDriveRoot)
            .Where(root => root.Length > 0 && string.CompareOrdinal(root, normalizedSystemRoot) > 0)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        string root = preferredDriveRoot
            ?? (string.IsNullOrWhiteSpace(documentsRoot) ? AppContext.BaseDirectory : documentsRoot);
        return Path.Combine(root, DefaultSaveFolderName);
    }

    private static string[] GetFixedReadyDriveRoots()
    {
        List<string> roots = [];
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return [.. roots];
    }

    private static string NormalizeDriveRoot(string root)
    {
        return Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
    }

    private static bool TryGetWritableSaveFolder(string? settingsFolder, out string folder, out Exception? error)
    {
        if (!TryGetSaveFolder(settingsFolder, createDirectory: true, out folder, out error))
        {
            return false;
        }

        string probePath = Path.Combine(folder, $".emerde-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using FileStream stream = new(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
            error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = e;
            return false;
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(e);
            }
        }
    }
}

internal sealed record SaveFolderResolution(string Folder, bool UsedFallback, Exception? Error);
