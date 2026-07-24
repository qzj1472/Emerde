using Emerde.Core;

namespace Emerde.Tests;

[Collection("MediaOperationRegistry")]
public sealed class ConverterTests
{
    [Theory]
    [InlineData("record.ts", true)]
    [InlineData("record.flv", false)]
    public void BuildArguments_PreservesOriginalAudioAndAddsOptimizedAudio(string sourceFileName, bool expectsGeneratedTimestamps)
    {
        VideoRecordingMetadata metadata = new()
        {
            NickName = "Host",
            RoomUrl = "https://example.test/room",
            Platform = "Test",
            Title = "Live",
            RecordedAt = new DateTime(2026, 7, 12, 12, 0, 0),
        };

        IReadOnlyList<string> arguments = Converter.BuildArguments(sourceFileName, "record.mp4", metadata);

        Assert.Equal(expectsGeneratedTimestamps, arguments.Contains("+genpts"));
        Assert.Contains("[0:a:0]volume=30dB,acompressor=threshold=-10dB:ratio=3,alimiter=limit=0.316227766:level=false[aopt]", arguments);
        Assert.Contains("0:a:0?", arguments);
        Assert.Contains("[aopt]", arguments);
        Assert.Contains("title=原音频", arguments);
        Assert.Contains("title=优化音频", arguments);
        Assert.Contains("use_metadata_tags", arguments);
        Assert.Equal("record.mp4", arguments[^1]);
    }

    [Fact]
    public void BuildArguments_RejectsUnsupportedSourceFormat()
    {
        Assert.Empty(Converter.BuildArguments("record.mkv", "record.mp4", new VideoRecordingMetadata()));
    }

    [Fact]
    public void BuildArguments_HandlesVideoWithoutAudio()
    {
        IReadOnlyList<string> arguments = Converter.BuildArguments("record.ts", "record.mp4", new VideoRecordingMetadata(), hasAudio: false);

        Assert.DoesNotContain("-filter_complex", arguments);
        Assert.DoesNotContain("[aopt]", arguments);
        Assert.DoesNotContain("title=优化音频", arguments);
        Assert.Contains("0:v?", arguments);
        Assert.Equal("record.mp4", arguments[^1]);
    }

    [Fact]
    public void BuildArguments_PreservesOriginalAudioWhenProbeIsUnknown()
    {
        IReadOnlyList<string> arguments = Converter.BuildArguments(
            "record.ts",
            "record.mp4",
            new VideoRecordingMetadata(),
            AudioStreamPresence.Unknown);

        Assert.DoesNotContain("-filter_complex", arguments);
        Assert.Contains("0:a?", arguments);
        Assert.Contains("-c:a", arguments);
        Assert.Contains("copy", arguments);
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
    public void BuildConcatArguments_UsesConcatInputAndSharedOutputMapping()
    {
        IReadOnlyList<string> arguments = Converter.BuildConcatArguments(
            "recording.concat.txt",
            "record.mkv",
            new VideoRecordingMetadata(),
            AudioStreamPresence.Unknown);

        Assert.Contains("-f", arguments);
        Assert.Contains("concat", arguments);
        Assert.Contains("-safe", arguments);
        Assert.Contains("0:a?", arguments);
        Assert.Equal("record.mkv", arguments[^1]);
    }

    [Fact]
    public void ActiveConversionCount_IsIdleInitially()
    {
        Assert.False(Converter.HasActiveConversions);
        Assert.Equal(0, Converter.ActiveConversionCount);
    }

    [Fact]
    public void ProbeAudioStream_ReturnsUnknownWhenFileCannotBeOpened()
    {
        string missingFile = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.ts");

        Assert.Equal(AudioStreamPresence.Unknown, Converter.ProbeAudioStream(missingFile));
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
}
