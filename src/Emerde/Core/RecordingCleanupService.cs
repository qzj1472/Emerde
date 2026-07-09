using Emerde.Extensions;

namespace Emerde.Core;

internal static class DataRetentionUnitHelper
{
    public const int Days = 0;
    public const int Weeks = 1;
    public const int Months = 2;
    public const int Years = 3;

    public static int NormalizeUnit(int unitIndex)
    {
        return unitIndex is Days or Weeks or Months or Years ? unitIndex : Weeks;
    }

    public static TimeSpan ToTimeSpan(int value, int unitIndex)
    {
        int safeValue = Math.Max(1, value);
        return NormalizeUnit(unitIndex) switch
        {
            Years => TimeSpan.FromDays(safeValue * 365d),
            Months => TimeSpan.FromDays(safeValue * 30d),
            Weeks => TimeSpan.FromDays(safeValue * 7d),
            Days or _ => TimeSpan.FromDays(safeValue),
        };
    }
}

internal static class RecordingCleanupService
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

    private static int isRunning;

    public static void QueueRun()
    {
        _ = Task.Run(() => RunAsync());
    }

    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref isRunning, 1) == 1)
        {
            return;
        }

        try
        {
            await Task.Run(() => Cleanup(cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation from application shutdown.
        }
        catch (Exception exception)
        {
            AppSessionLogger.WriteException(exception);
        }
        finally
        {
            Interlocked.Exchange(ref isRunning, 0);
        }
    }

    private static void Cleanup(CancellationToken cancellationToken)
    {
        string root = SaveFolderHelper.GetSaveFolder(Configurations.SaveFolder.Get());
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        string fullRoot = Path.GetFullPath(root);
        DateTime cutoff = DateTime.Now - DataRetentionUnitHelper.ToTimeSpan(
            Configurations.DataRetentionValue.Get(),
            Configurations.DataRetentionUnit.Get());

        int deletedCount = 0;
        foreach (string filePath in EnumerateFilesSafe(fullRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!MediaExtensions.Contains(Path.GetExtension(filePath)))
            {
                continue;
            }

            try
            {
                FileInfo file = new(filePath);
                if (file.Exists && file.LastWriteTime < cutoff)
                {
                    file.Delete();
                    deletedCount++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.Write($"cleanup skipped file {filePath}: {exception.Message}");
            }
        }

        RemoveEmptyDirectories(fullRoot, cancellationToken);

        if (deletedCount > 0)
        {
            AppSessionLogger.Write($"cleanup deleted {deletedCount} expired recording files");
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        Stack<string> directories = new();
        directories.Push(root);

        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            string[] files;
            string[] children;

            try
            {
                files = Directory.GetFiles(directory);
                children = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.Write($"cleanup skipped directory {directory}: {exception.Message}");
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            foreach (string child in children)
            {
                directories.Push(child);
            }
        }
    }

    private static void RemoveEmptyDirectories(string root, CancellationToken cancellationToken)
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory
                .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.Write($"cleanup skipped empty directory pass: {exception.Message}");
            return;
        }

        foreach (string directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.Write($"cleanup skipped empty directory {directory}: {exception.Message}");
            }
        }
    }
}
