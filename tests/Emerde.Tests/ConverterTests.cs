using Emerde.Core;

namespace Emerde.Tests;

[Collection("MediaOperationRegistry")]
public sealed class ConverterTests
{
    [Theory]
    [InlineData("mkv", false, ".mkv")]
    [InlineData(".MP4", false, ".mp4")]
    [InlineData(" ts ", true, ".ts")]
    [InlineData("flv", true, ".flv")]
    [InlineData("avi", true, null)]
    [InlineData("ts", false, null)]
    public void NormalizeTargetFormat_AcceptsOnlySupportedContainers(string value, bool allowSourceContainers, string? expected)
    {
        Assert.Equal(expected, Converter.NormalizeTargetFormat(value, allowSourceContainers));
    }

    [Theory]
    [InlineData("mp4", true)]
    [InlineData(".MP4", true)]
    [InlineData("mkv", false)]
    [InlineData("ts", false)]
    public void CreateDefaultOptions_OptimizesOnlyMp4(string targetFormat, bool expected)
    {
        Assert.Equal(expected, Converter.CreateDefaultOptions(targetFormat).OptimizeAudio);
    }

    [Theory]
    [InlineData(3600d, 3598d, true)]
    [InlineData(3600d, 3597.9d, false)]
    [InlineData(3600d, 0d, false)]
    [InlineData(0d, 0d, true)]
    public void OutputDuration_UsesABoundedAbsoluteTolerance(double expected, double actual, bool accepted)
    {
        Assert.Equal(accepted, Converter.IsDurationWithinTolerance(expected, actual));
    }

    [Fact]
    public void AudioDynamicsProcessor_PreservesLevelOrderingWithoutHardClippingNormalSamples()
    {
        double quiet = 0.01d * FfmpegMediaEngine.AudioDynamicsProcessor.CalculateLinearGain(0.01d);
        double medium = 0.1d * FfmpegMediaEngine.AudioDynamicsProcessor.CalculateLinearGain(0.1d);
        double loud = FfmpegMediaEngine.AudioDynamicsProcessor.CalculateLinearGain(1d);

        Assert.True(quiet < medium);
        Assert.True(medium < loud);
        Assert.InRange(loud, 0d, Math.Pow(10d, -1d / 20d));
    }

    [Fact]
    public void BuildTargetPath_RemovesSessionPartSuffixForMultipleSources()
    {
        FileInfo[] sources =
        [
            new(Path.Combine("D:\\records", "host_2026-07-24_000.ts")),
            new(Path.Combine("D:\\records", "host_2026-07-24_001.ts")),
        ];

        Assert.Equal(Path.Combine("D:\\records", "host_2026-07-24.mkv"), Converter.BuildTargetPath(sources, ".mkv"));
    }

    [Fact]
    public void BuildSessionTargetPath_UsesSharedSessionBaseName()
    {
        string sourcePattern = Path.Combine("D:\\records", "host_2026-07-24_%03d.ts");

        Assert.Equal(Path.Combine("D:\\records", "host_2026-07-24.ts"), Converter.BuildSessionTargetPath(sourcePattern, ".ts"));
    }

    [Fact]
    public void ActiveConversionCount_IsIdleInitially()
    {
        Assert.False(Converter.HasActiveConversions);
        Assert.Equal(0, Converter.ActiveConversionCount);
    }

    [Fact]
    public void FfmpegRuntime_IsFolderBasedAndCommandLineToolsAreNotRequired()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string ffmpegDirectory = Path.Combine(baseDirectory, "ffmpeg");

        Assert.True(FfmpegMediaEngine.IsAvailable);
        Assert.True(Directory.Exists(ffmpegDirectory));
        Assert.True(File.Exists(Path.Combine(ffmpegDirectory, "avformat-61.dll")));
        Assert.True(File.Exists(Path.Combine(ffmpegDirectory, "avcodec-61.dll")));
        Assert.True(File.Exists(Path.Combine(ffmpegDirectory, "avutil-59.dll")));
        Assert.False(File.Exists(Path.Combine(baseDirectory, "ffmpeg.exe")));
        Assert.False(File.Exists(Path.Combine(baseDirectory, "ffprobe.exe")));
        Assert.True(FfmpegMediaEngine.HasAacEncoder);
        Assert.True(FfmpegMediaEngine.HasRequiredRuntimeCapabilities);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFalseWhenSourceCannotBeProbed()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "session_000.ts");

        try
        {
            await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);

            Assert.False(await new Converter().ExecuteAsync(source, ".mkv"));
            Assert.False(File.Exists(Path.Combine(directory, "session_000.mkv")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RejectsUnsupportedTargetBeforeCreatingOutput()
    {
        string source = Path.Combine(Path.GetTempPath(), $"emerde-invalid-target-{Guid.NewGuid():N}.ts");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        try
        {
            Assert.False(await new Converter().ExecuteAsync(source, ".avi"));
            Assert.False(File.Exists(Path.ChangeExtension(source, ".avi")));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public async Task ExecuteSessionPartsAsync_ReturnsFalseWhenTransportStreamPartsCannotBeProbed()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-raw-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePattern = Path.Combine(directory, "session_%03d.ts");
        string firstSource = Path.Combine(directory, "session_000.ts");
        string secondSource = Path.Combine(directory, "session_001.ts");

        try
        {
            await File.WriteAllBytesAsync(firstSource, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(secondSource, [5, 6, 7, 8]);

            Assert.False(await new Converter().ExecuteSessionPartsAsync(sourcePattern, [firstSource, secondSource], ".ts"));
            Assert.False(File.Exists(Path.Combine(directory, "session.ts")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteSessionPartsAsync_ReturnsFalseWhenFlvPartsCannotBeProbed()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-flv-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePattern = Path.Combine(directory, "session_%03d.flv");
        string firstSource = Path.Combine(directory, "session_000.flv");
        string secondSource = Path.Combine(directory, "session_001.flv");

        try
        {
            await File.WriteAllBytesAsync(firstSource, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(secondSource, [5, 6, 7, 8]);

            Assert.False(await new Converter().ExecuteSessionPartsAsync(sourcePattern, [firstSource, secondSource], ".flv"));
            Assert.False(File.Exists(Path.Combine(directory, "session.flv")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteWithCompletionAsync_ReportsReservedTargetBeforeMediaWork()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-reservation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "session.ts");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        string? reservedTarget = null;

        try
        {
            bool converted = await new Converter().ExecuteWithCompletionAsync(
                source,
                ".mkv",
                _ => { },
                onTargetReserved: path => reservedTarget = path);

            Assert.False(converted);
            Assert.Equal(Path.Combine(directory, "session.mkv"), reservedTarget);
            Assert.False(File.Exists(reservedTarget));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}
