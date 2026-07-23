namespace Emerde.Core;

internal static class DataRetentionUnitHelper
{
    public const int MaximumValue = 9999;

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
        int safeValue = Math.Clamp(value, 1, MaximumValue);
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
    private static readonly EnumerationOptions DirectoryEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private static int queuedRunRequested;
    private static int queuedWorkerRunning;

    public static void QueueRun()
    {
        if (!Configurations.IsDataRetentionEnabled.Get())
        {
            return;
        }

        Interlocked.Exchange(ref queuedRunRequested, 1);
        StartQueuedWorker();
    }

    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await RunGate.WaitAsync(cancellationToken);
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
            RunGate.Release();
        }
    }

    private static void StartQueuedWorker()
    {
        if (Interlocked.CompareExchange(ref queuedWorkerRunning, 1, 0) == 0)
        {
            _ = Task.Run(ProcessQueuedRunsAsync);
        }
    }

    private static async Task ProcessQueuedRunsAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref queuedRunRequested, 0) != 0)
            {
                await RunAsync();
            }
        }
        finally
        {
            Interlocked.Exchange(ref queuedWorkerRunning, 0);
            if (Volatile.Read(ref queuedRunRequested) != 0)
            {
                StartQueuedWorker();
            }
        }
    }

    private static void Cleanup(CancellationToken cancellationToken)
    {
        if (!Configurations.IsDataRetentionEnabled.Get())
        {
            return;
        }

        string[] roots = MediaFileCatalog.GetConfiguredSaveFolders();
        if (roots.Length == 0)
        {
            return;
        }
        DateTime cutoff = DateTime.Now - DataRetentionUnitHelper.ToTimeSpan(
            Configurations.DataRetentionValue.Get(),
            Configurations.DataRetentionUnit.Get());
        string[] pendingSourcePatterns = RecordingRecoveryService.GetPendingSourcePatterns();

        int deletedCount = 0;
        foreach (string root in roots.Where(Directory.Exists))
        {
            foreach (string filePath in EnumerateFilesSafe(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!MediaFileCatalog.IsMediaPath(filePath)
                    || MediaFileCatalog.IsApplicationTemporaryPath(filePath)
                    || MediaOperationRegistry.IsPathProtected(filePath)
                    || RecordingRecoveryService.IsPendingSourcePath(filePath, pendingSourcePatterns))
                {
                    continue;
                }

                try
                {
                    FileInfo file = new(filePath);
                    if (file.Exists && file.LastWriteTime < cutoff && VideoRecordingMetadataStore.HasValidMetadata(file))
                    {
                        file.Delete();
                        VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(filePath);
                        deletedCount++;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    AppSessionLogger.Write($"cleanup skipped file {filePath}: {exception.Message}");
                }
            }

            RemoveEmptyDirectories(root, cancellationToken);
        }

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
                if (ShouldTraverseDirectory(child))
                {
                    directories.Push(child);
                }
            }
        }
    }

    internal static bool ShouldTraverseDirectory(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void RemoveEmptyDirectories(string root, CancellationToken cancellationToken)
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory
                .EnumerateDirectories(root, "*", DirectoryEnumerationOptions)
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
