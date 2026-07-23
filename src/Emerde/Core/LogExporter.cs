using System.IO;
using System.IO.Compression;

namespace Emerde.Core;

internal static class LogExporter
{
    public static string ExportLatest(string targetDirectory)
    {
        string[] files = GetLatestSessionFiles();
        if (files.Length == 0)
        {
            throw new FileNotFoundException("没有找到可导出的日志文件。");
        }

        return CreateArchive(targetDirectory, $"Emerde_logs_latest_{DateTime.Now:yyyyMMdd_HHmmss}", files);
    }

    public static string ExportAll(string targetDirectory)
    {
        string[] files = Directory.Exists(AppPaths.LogsDirectory)
            ? Directory.GetFiles(AppPaths.LogsDirectory, "*.log", SearchOption.TopDirectoryOnly)
                .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        if (files.Length == 0)
        {
            throw new FileNotFoundException("没有找到可导出的日志文件。");
        }

        return CreateArchive(targetDirectory, $"Emerde_logs_all_{DateTime.Now:yyyyMMdd_HHmmss}", files);
    }

    private static string[] GetLatestSessionFiles()
    {
        if (!Directory.Exists(AppPaths.LogsDirectory))
        {
            return [];
        }

        string? latest = Directory.GetFiles(AppPaths.LogsDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .Where(static file => !file.EndsWith(".error.log", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latest))
        {
            return [];
        }

        string errorLog = Path.Combine(
            Path.GetDirectoryName(latest)!,
            Path.GetFileNameWithoutExtension(latest) + ".error.log");

        return File.Exists(errorLog) ? [latest, errorLog] : [latest];
    }

    internal static string CreateArchive(string targetDirectory, string archiveName, IReadOnlyList<string> files)
    {
        Directory.CreateDirectory(targetDirectory);
        string archivePath = GetAvailableFilePath(Path.Combine(targetDirectory, archiveName + ".zip"));
        string temporaryPath = archivePath + $".{Guid.NewGuid():N}.tmp";
        int entryCount = 0;

        try
        {
            using (FileStream archiveStream = new(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new(archiveStream, ZipArchiveMode.Create))
            {
                HashSet<string> entryNames = new(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files.Where(File.Exists))
                {
                    string entryName = GetAvailableEntryName(Path.GetFileName(file), entryNames);
                    ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    using Stream destination = entry.Open();
                    using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    source.CopyTo(destination);
                    entryCount++;
                }
            }

            if (entryCount == 0)
            {
                throw new FileNotFoundException("没有找到可导出的日志文件。");
            }

            File.Move(temporaryPath, archivePath);
            return archivePath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetAvailableFilePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path)!;
        string fileName = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int index = 1; index < 1000; index++)
        {
            string candidate = Path.Combine(directory, $"{fileName}_{index}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{fileName}_{Guid.NewGuid():N}{extension}");
    }

    private static string GetAvailableEntryName(string requestedName, ISet<string> usedNames)
    {
        if (usedNames.Add(requestedName))
        {
            return requestedName;
        }

        string fileName = Path.GetFileNameWithoutExtension(requestedName);
        string extension = Path.GetExtension(requestedName);
        for (int index = 2; ; index++)
        {
            string candidate = $"{fileName}_{index}{extension}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
