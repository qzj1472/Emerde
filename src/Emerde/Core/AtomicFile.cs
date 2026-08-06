using System.Text;
using System.Text.Json;

namespace Emerde.Core;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        string targetPath = Path.GetFullPath(path);
        string temporaryPath = CreateTemporaryPath(targetPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(stream, encoding ?? new UTF8Encoding(false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            Commit(temporaryPath, targetPath, null);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    public static void Copy(string sourcePath, string targetPath)
    {
        string sourceFullPath = Path.GetFullPath(sourcePath);
        string targetFullPath = Path.GetFullPath(targetPath);
        if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string temporaryPath = CreateTemporaryPath(targetFullPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath)!);
            using (FileStream source = new(sourceFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream target = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }
            Commit(temporaryPath, targetFullPath, null);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    public static async Task WriteJsonAsync<T>(
        string path,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken,
        string? backupPath = null)
    {
        string targetPath = Path.GetFullPath(path);
        string? backupFullPath = string.IsNullOrWhiteSpace(backupPath) ? null : Path.GetFullPath(backupPath);
        string temporaryPath = CreateTemporaryPath(targetPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            Commit(temporaryPath, targetPath, backupFullPath);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private static string CreateTemporaryPath(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory;
        return Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static void Commit(string temporaryPath, string targetPath, string? backupPath)
    {
        if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(targetPath))
        {
            File.Replace(temporaryPath, targetPath, backupPath, ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporaryPath, targetPath, overwrite: true);
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(exception);
        }
    }
}
