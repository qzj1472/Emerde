using System.IO.Compression;
using Emerde.Core;

namespace Emerde.Tests;

public sealed class LogExporterTests
{
    [Fact]
    public void CreateArchive_ExportsOpenLogFilesAndAvoidsNameCollisions()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeLogExporterTests", Guid.NewGuid().ToString("N"));
        string sourceA = Path.Combine(root, "a", "session.log");
        string sourceB = Path.Combine(root, "b", "session.log");
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceA)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceB)!);
        File.WriteAllText(sourceA, "first");
        File.WriteAllText(sourceB, "second");

        try
        {
            using FileStream openLog = new(sourceA, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            string archivePath = LogExporter.CreateArchive(output, "logs", [sourceA, sourceB]);

            Assert.EndsWith(".zip", archivePath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(archivePath));
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Assert.Equal(["session.log", "session_2.log"], archive.Entries.Select(static entry => entry.FullName).ToArray());
            Assert.Equal("first", ReadEntry(archive.Entries[0]));
            Assert.Equal("second", ReadEntry(archive.Entries[1]));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesExistingArchive()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeLogExporterTests", Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "session.log");
        Directory.CreateDirectory(root);
        File.WriteAllText(source, "log");
        File.WriteAllText(Path.Combine(root, "logs.zip"), "existing");

        try
        {
            string archivePath = LogExporter.CreateArchive(root, "logs", [source]);

            Assert.Equal(Path.Combine(root, "logs_1.zip"), archivePath);
            Assert.Equal("existing", File.ReadAllText(Path.Combine(root, "logs.zip")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using StreamReader reader = new(entry.Open());
        return reader.ReadToEnd();
    }
}
