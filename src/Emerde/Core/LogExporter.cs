using System.IO;
using System.IO.Compression;
using System.Globalization;

namespace Emerde.Core;

internal static class LogExporter
{
    public static string ExportToday(string targetDirectory)
    {
        DateTime now = DateTime.Now;
        string[] files = GetLogDirectories()
            .SelectMany(directory => GetLogFilesForDate(directory, now))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            throw new FileNotFoundException("没有找到可导出的日志文件。");
        }

        return CreateArchive(targetDirectory, $"Emerde_logs_today_{now:yyyyMMdd_HHmmss}", files);
    }

    public static string ExportAll(string targetDirectory)
    {
        DateTime now = DateTime.Now;
        string[] files = GetLogDirectories()
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new FileNotFoundException("没有找到可导出的日志文件。");
        }

        return CreateArchive(targetDirectory, $"Emerde_logs_all_{now:yyyyMMdd_HHmmss}", files);
    }

    internal static string[] GetLogFilesForDate(string logDirectory, DateTime date)
    {
        if (!Directory.Exists(logDirectory))
        {
            return [];
        }

        DateTime targetDate = date.Date;
        string compactPrefix = targetDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string dashedPrefix = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .Where(file => IsLogFileForDate(file, compactPrefix, dashedPrefix))
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> GetLogDirectories()
    {
        yield return AppPaths.LogsDirectory;
        if (!string.Equals(AppPaths.LogsDirectory, AppSessionLogger.FallbackLogsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield return AppSessionLogger.FallbackLogsDirectory;
        }
    }

    private static bool IsLogFileForDate(string file, string compactPrefix, string dashedPrefix)
    {
        string fileName = Path.GetFileName(file);
        return fileName.StartsWith(compactPrefix, StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith(dashedPrefix, StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith($"_{compactPrefix}.log", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith($"_{compactPrefix}.error.log", StringComparison.OrdinalIgnoreCase);
    }

    internal static string CreateArchive(string targetDirectory, string archiveName, IReadOnlyList<string> files)
    {
        Directory.CreateDirectory(targetDirectory);
        string archivePath = GetAvailableFilePath(Path.Combine(targetDirectory, archiveName + ".zip"));
        string temporaryPath = archivePath + $".{Guid.NewGuid():N}.tmp";
        int entryCount = 0;
        Exception? archiveError = null;

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
        catch (Exception e)
        {
            archiveError = e;
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception e) when (archiveError != null && e is IOException or UnauthorizedAccessException)
            {
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
