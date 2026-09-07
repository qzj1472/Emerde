namespace Emerde.Core;

internal static class RecordingStorageGuard
{
    internal const long MinimumFreeBytes = 10L * 1024 * 1024 * 1024;

    internal static bool IsLow(string path, out long availableBytes)
    {
        return TryGetAvailableBytes(path, out availableBytes)
            && IsLowBytes(availableBytes);
    }

    internal static bool IsExhausted(string path, out long availableBytes)
    {
        return TryGetAvailableBytes(path, out availableBytes)
            && IsExhaustedBytes(availableBytes);
    }

    internal static bool IsLowBytes(long availableBytes)
    {
        return availableBytes >= 0 && availableBytes <= MinimumFreeBytes;
    }

    internal static bool IsExhaustedBytes(long availableBytes)
    {
        return availableBytes == 0;
    }

    internal static RecordingStorageCheckResult CheckConfiguredFolders()
    {
        List<string> lowPaths = [];
        List<string> exhaustedPaths = [];
        List<long> availableBytes = [];
        bool hasUnreadablePath = false;
        string[] paths;
        try
        {
            paths = MediaFileCatalog.GetConfiguredSaveFolders()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return new RecordingStorageCheckResult([], [], [], true, null);
        }

        foreach (string path in paths)
        {
            if (!TryGetAvailableBytes(path, out long available))
            {
                hasUnreadablePath = true;
                continue;
            }

            availableBytes.Add(available);
            if (IsLowBytes(available))
            {
                lowPaths.Add(path);
            }
            if (IsExhaustedBytes(available))
            {
                exhaustedPaths.Add(path);
            }
        }

        return new RecordingStorageCheckResult(
            lowPaths,
            exhaustedPaths,
            paths,
            hasUnreadablePath,
            availableBytes.Count == 0 ? null : availableBytes.Min());
    }

    private static bool TryGetAvailableBytes(string path, out long availableBytes)
    {
        availableBytes = -1;
        try
        {
            string fullPath = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            availableBytes = new DriveInfo(root).AvailableFreeSpace;
            return availableBytes >= 0;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed record RecordingStorageCheckResult(
    IReadOnlyList<string> LowPaths,
    IReadOnlyList<string> ExhaustedPaths,
    IReadOnlyList<string> ConfiguredPaths,
    bool HasUnreadablePath,
    long? MinimumAvailableBytes);
