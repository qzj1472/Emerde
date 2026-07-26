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

    [Fact]
    public void GetLogFilesForDate_SelectsOnlyRequestedDay()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeLogExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string currentLog = Path.Combine(root, "20260725_201514_1234.log");
        string currentErrorLog = Path.Combine(root, "20260725_201514_1234.error.log");
        string legacyLog = Path.Combine(root, "2026-07-25.log");
        string previousLog = Path.Combine(root, "20260724_201514_1234.log");

        try
        {
            File.WriteAllText(currentLog, "current");
            File.WriteAllText(currentErrorLog, "error");
            File.WriteAllText(legacyLog, "legacy");
            File.WriteAllText(previousLog, "previous");

            string[] files = LogExporter.GetLogFilesForDate(root, new DateTime(2026, 7, 25));

            Assert.Empty(new[] { currentErrorLog, currentLog, legacyLog }.Except(files, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(files.Except([currentErrorLog, currentLog, legacyLog], StringComparer.OrdinalIgnoreCase));
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
