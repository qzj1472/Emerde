using Emerde.Core;

namespace Emerde.Tests;

public sealed class RecordingRecoveryServiceTests
{
    [Fact]
    public void UpdateOptions_AppliesLatestFormatAndRemoveSourceOutsideCurrentSaveFolder()
    {
        string sourcePattern = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}.ts");
        string? markerPath = RecordingRecoveryService.Register(sourcePattern, new RoomRecordingOptions
        {
            RecordFormat = "TS/FLV -> MP4",
            IsRemoveTs = false,
        });

        Assert.NotNull(markerPath);
        try
        {
            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV -> MKV",
                IsRemoveTs = true,
            }));

            string marker = File.ReadAllText(markerPath!);
            Assert.Contains("\"TargetFormat\": \".mkv\"", marker);
            Assert.Contains("\"RemoveSource\": true", marker);

            Assert.False(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV",
            }));
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".tmp");
        }
    }

    [Fact]
    public async Task ProcessAsync_PreservesMarkerWhenItIsTemporarilyUnreadable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{}");

        try
        {
            await using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await RecordingRecoveryService.ProcessAsync(path);
            }

            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ProcessAsync_QuarantinesPermanentlyInvalidMarker()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{");

        try
        {
            await RecordingRecoveryService.ProcessAsync(path);

            Assert.False(File.Exists(path));
            Assert.True(File.Exists(path + ".invalid"));
            Assert.Contains("JSON", await File.ReadAllTextAsync(path + ".invalid.reason.txt"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".invalid");
            File.Delete(path + ".invalid.reason.txt");
        }
    }

    [Theory]
    [InlineData("relative.ts", ".mp4")]
    [InlineData("C:\\recording.txt", ".mp4")]
    [InlineData("C:\\record_%02d.ts", ".mp4")]
    [InlineData("C:\\record_%03d_%name.ts", ".mp4")]
    [InlineData("C:\\record.ts", ".avi")]
    public async Task ProcessAsync_QuarantinesSemanticallyInvalidMarker(string sourcePattern, string targetFormat)
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, $$"""{"SourcePattern":"{{sourcePattern.Replace("\\", "\\\\", StringComparison.Ordinal)}}","TargetFormat":"{{targetFormat}}"}""");

        try
        {
            await RecordingRecoveryService.ProcessAsync(path);

            Assert.False(File.Exists(path));
            Assert.True(File.Exists(path + ".invalid"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".invalid");
            File.Delete(path + ".invalid.reason.txt");
        }
    }

    [Fact]
    public void GetSourceFiles_ReturnsCompletedSegmentsInOrder()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string pattern = Path.Combine(directory, "record_%03d.ts");
            File.WriteAllBytes(Path.Combine(directory, "record_001.ts"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "record_000.ts"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "other_000.ts"), [1]);

            string[] result = RecordingRecoveryService.GetSourceFiles(pattern);

            Assert.Equal(
                [Path.Combine(directory, "record_000.ts"), Path.Combine(directory, "record_001.ts")],
                result);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetSourceFiles_RejectsMissingAndEmptySources()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string empty = Path.Combine(directory, "empty.ts");
            File.WriteAllBytes(empty, []);

            Assert.Empty(RecordingRecoveryService.GetSourceFiles(empty));
            Assert.Empty(RecordingRecoveryService.GetSourceFiles(Path.Combine(directory, "missing.ts")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IsPathWithinRoot_RejectsSiblingDirectoriesWithTheSamePrefix()
    {
        string root = Path.Combine(Path.GetTempPath(), "emerde-recordings");
        string inside = Path.Combine(root, "room", "record.ts");
        string sibling = Path.Combine(Path.GetTempPath(), "emerde-recordings-other", "record.ts");

        Assert.True(RecordingRecoveryService.IsPathWithinRoot(inside, root));
        Assert.False(RecordingRecoveryService.IsPathWithinRoot(sibling, root));
    }
}
