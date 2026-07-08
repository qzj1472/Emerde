using System.IO;

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

        return CopyFiles(targetDirectory, $"Emerde_logs_latest_{DateTime.Now:yyyyMMdd_HHmmss}", files);
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

        return CopyFiles(targetDirectory, $"Emerde_logs_all_{DateTime.Now:yyyyMMdd_HHmmss}", files);
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

    private static string CopyFiles(string targetDirectory, string folderName, IReadOnlyList<string> files)
    {
        Directory.CreateDirectory(targetDirectory);
        string exportDirectory = GetAvailableDirectory(Path.Combine(targetDirectory, folderName));
        Directory.CreateDirectory(exportDirectory);

        foreach (string file in files.Where(File.Exists))
        {
            File.Copy(file, Path.Combine(exportDirectory, Path.GetFileName(file)), overwrite: true);
        }

        return exportDirectory;
    }

    private static string GetAvailableDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return path;
        }

        for (int index = 1; index < 1000; index++)
        {
            string candidate = $"{path}_{index}";
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return $"{path}_{Guid.NewGuid():N}";
    }
}
