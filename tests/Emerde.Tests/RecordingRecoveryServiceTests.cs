using Emerde.Core;
using System.Diagnostics;

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
    public void UpdateOptions_KeepsSessionMarkerWhenLatestFormatIsRaw()
    {
        string sourcePattern = Path.Combine(Path.GetTempPath(), $"emerde-recording-{Guid.NewGuid():N}_%03d.ts");
        string? markerPath = RecordingRecoveryService.RegisterSessionParts(sourcePattern, ".ts");

        Assert.NotNull(markerPath);
        try
        {
            Assert.True(RecordingRecoveryService.UpdateOptions(markerPath!, new RoomRecordingOptions
            {
                RecordFormat = "TS/FLV",
                IsRemoveTs = false,
            }));

            string marker = File.ReadAllText(markerPath!);
            Assert.Contains("\"TargetFormat\": \".ts\"", marker);
            Assert.Contains("\"RemoveSource\": true", marker);
            Assert.Contains("\"MergeSessionParts\": true", marker);
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
    [InlineData("C:\\record.ts", ".ts")]
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
    public async Task ProcessSourcePatternAsync_MergesOnlyInternalSessionParts()
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        Assert.True(File.Exists(ffmpegPath));

        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recovery-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePattern = Path.Combine(directory, "session_%03d.ts");
        string firstSource = Path.Combine(directory, "session_000.ts");
        string secondSource = Path.Combine(directory, "session_001.ts");

        try
        {
            await CreateTestTransportStreamAsync(ffmpegPath, firstSource, "black");
            await CreateTestTransportStreamAsync(ffmpegPath, secondSource, "blue");

            Assert.True(await RecordingRecoveryService.ProcessSourcePatternAsync(sourcePattern, ".mkv", removeSource: false));
            Assert.False(File.Exists(Path.Combine(directory, "session.mkv")));
            Assert.True(File.Exists(Path.Combine(directory, "session_000.mkv")));
            Assert.True(File.Exists(Path.Combine(directory, "session_001.mkv")));

            Assert.True(await RecordingRecoveryService.ProcessSourcePatternAsync(sourcePattern, ".mp4", removeSource: false, mergeSessionParts: true));
            Assert.True(File.Exists(Path.Combine(directory, "session.mp4")));
            Assert.False(File.Exists(Path.Combine(directory, "session_000.mp4")));
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

    private static async Task CreateTestTransportStreamAsync(string ffmpegPath, string targetPath, string color)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("lavfi");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add($"color=c={color}:s=320x180:d=0.2");
        process.StartInfo.ArgumentList.Add("-c:v");
        process.StartInfo.ArgumentList.Add("libx264");
        process.StartInfo.ArgumentList.Add("-an");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("mpegts");
        process.StartInfo.ArgumentList.Add(targetPath);

        process.Start();
        Task errorTask = process.StandardError.ReadToEndAsync();
        Task outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(errorTask, outputTask);

        Assert.Equal(0, process.ExitCode);
    }
}
