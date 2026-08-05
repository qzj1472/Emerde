using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Emerde.Installer;

internal sealed class InstallerPayload(Func<Stream> openArchive, string? runtimeSource = null)
{
    private long? uncompressedLength;

    public static InstallerPayload FromArguments(string[] arguments)
    {
        string? archivePath = GetArgument(arguments, "--payload");
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return new InstallerPayload(() =>
                throw new InvalidDataException("当前安装程序不包含可用的应用负载。"));
        }

        string fullPath = Path.GetFullPath(archivePath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("安装负载不存在。", fullPath);
        }

        string? runtimeSource = GetArgument(arguments, "--runtime-source");
        string? runtimeRoot = string.IsNullOrWhiteSpace(runtimeSource)
            ? null
            : Path.GetFullPath(runtimeSource.Trim().Trim('"'));
        if (runtimeRoot is not null && !Directory.Exists(runtimeRoot))
        {
            throw new DirectoryNotFoundException("安装器公共运行时不存在。");
        }

        return new InstallerPayload(() => new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan), runtimeRoot);
    }

    public async Task ExtractAsync(
        string destination,
        IProgress<InstallationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destination);
        string destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        long totalBytes = GetUncompressedLength();
        long completedBytes = 0;

        await using Stream archiveStream = openArchive();
        using IArchive archive = ArchiveFactory.OpenArchive(archiveStream);
        byte[] buffer = new byte[1024 * 1024];

        if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
        {
            using IReader reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                completedBytes = await ExtractEntryAsync(
                    destination,
                    destinationRoot,
                    reader.Entry.Key,
                    reader.Entry.IsDirectory,
                    reader.Entry.LastModifiedTime,
                    reader.OpenEntryStream,
                    buffer,
                    completedBytes,
                    totalBytes,
                    progress,
                    cancellationToken);
            }
        }
        else
        {
            foreach (IArchiveEntry entry in archive.Entries)
            {
                completedBytes = await ExtractEntryAsync(
                    destination,
                    destinationRoot,
                    entry.Key,
                    entry.IsDirectory,
                    entry.LastModifiedTime,
                    entry.OpenEntryStream,
                    buffer,
                    completedBytes,
                    totalBytes,
                    progress,
                    cancellationToken);
            }
        }

        if (runtimeSource is not null)
        {
            await CopyRuntimeAsync(
                runtimeSource,
                Path.Combine(destination, InstallationPaths.RuntimeDirectoryName),
                completedBytes,
                totalBytes,
                progress,
                cancellationToken);
        }
    }

    private static async Task<long> ExtractEntryAsync(
        string destination,
        string destinationRoot,
        string? key,
        bool isDirectory,
        DateTime? lastModifiedTime,
        Func<Stream> openEntryStream,
        byte[] buffer,
        long completedBytes,
        long totalBytes,
        IProgress<InstallationProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string entryPath = key?.Replace('/', Path.DirectorySeparatorChar)
            ?? throw new InvalidDataException("安装负载包含无效文件名。");
        string targetPath = Path.GetFullPath(Path.Combine(destination, entryPath));

        if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"安装负载包含无效路径：{key}");
        }

        if (isDirectory)
        {
            Directory.CreateDirectory(targetPath);
            return completedBytes;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await using (Stream source = openEntryStream())
        await using (FileStream target = new(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                completedBytes += bytesRead;
                int percentage = 5 + (int)Math.Min(70, completedBytes * 70 / totalBytes);
                progress.Report(new InstallationProgress(percentage, "正在释放程序文件..."));
            }
        }

        if (lastModifiedTime is DateTime value)
        {
            File.SetLastWriteTimeUtc(targetPath, value.ToUniversalTime());
        }

        return completedBytes;
    }

    public long GetUncompressedLength()
    {
        if (uncompressedLength is long cachedLength)
        {
            return cachedLength;
        }

        using Stream archiveStream = openArchive();
        using IArchive archive = ArchiveFactory.OpenArchive(archiveStream);
        long totalBytes = archive.TotalUncompressedSize;

        if (runtimeSource is not null)
        {
            totalBytes = Directory.EnumerateFiles(runtimeSource, "*", SearchOption.AllDirectories)
                .Aggregate(
                    totalBytes,
                    (length, filePath) => checked(length + new FileInfo(filePath).Length));
        }

        uncompressedLength = Math.Max(1, totalBytes);
        return uncompressedLength.Value;
    }

    private static async Task CopyRuntimeAsync(
        string sourceRoot,
        string destinationRoot,
        long completedBytes,
        long totalBytes,
        IProgress<InstallationProgress> progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024 * 1024];
        foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            string destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            DateTime lastWriteTime = File.GetLastWriteTimeUtc(sourcePath);
            await using (FileStream source = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream destination = new(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    completedBytes += bytesRead;
                    int percentage = 5 + (int)Math.Min(70, completedBytes * 70 / totalBytes);
                    progress.Report(new InstallationProgress(percentage, "正在释放公共运行时..."));
                }
            }

            File.SetLastWriteTimeUtc(destinationPath, lastWriteTime);
        }
    }

    private static string? GetArgument(string[] arguments, string name)
    {
        int index = Array.FindIndex(
            arguments,
            value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
}
