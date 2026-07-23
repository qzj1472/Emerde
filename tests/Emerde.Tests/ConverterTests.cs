using Emerde.Core;
using System.Diagnostics;

namespace Emerde.Tests;

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
    public async Task ExecuteAsync_ConvertsRealVideoWithoutAudio()
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        Assert.True(File.Exists(ffmpegPath));

        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourceFile = Path.Combine(directory, "no-audio.ts");

        try
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
            process.StartInfo.ArgumentList.Add("color=c=black:s=320x180:d=0.2");
            process.StartInfo.ArgumentList.Add("-c:v");
            process.StartInfo.ArgumentList.Add("libx264");
            process.StartInfo.ArgumentList.Add("-an");
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add("mpegts");
            process.StartInfo.ArgumentList.Add(sourceFile);

            process.Start();
            Task errorTask = process.StandardError.ReadToEndAsync();
            Task outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(errorTask, outputTask);

            Assert.Equal(0, process.ExitCode);
            Assert.Equal(AudioStreamPresence.Absent, Converter.ProbeAudioStream(sourceFile));
            Assert.True(await new Converter().ExecuteAsync(sourceFile, ".mp4"));
            Assert.True(File.Exists(Path.ChangeExtension(sourceFile, ".mp4")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MergesSessionPartsIntoOneTarget()
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        Assert.True(File.Exists(ffmpegPath));

        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string firstSource = Path.Combine(directory, "session_000.ts");
        string secondSource = Path.Combine(directory, "session_001.ts");

        try
        {
            await CreateTestTransportStreamAsync(ffmpegPath, firstSource, "black");
            await CreateTestTransportStreamAsync(ffmpegPath, secondSource, "blue");

            Assert.True(await new Converter().ExecuteAsync([firstSource, secondSource], ".mkv"));
            Assert.True(File.Exists(Path.Combine(directory, "session.mkv")));
            Assert.False(File.Exists(Path.Combine(directory, "session_000.mkv")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteSessionPartsAsync_MergesRawTransportStreamIntoOneSourceFormatTarget()
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        Assert.True(File.Exists(ffmpegPath));

        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-raw-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePattern = Path.Combine(directory, "session_%03d.ts");
        string firstSource = Path.Combine(directory, "session_000.ts");
        string secondSource = Path.Combine(directory, "session_001.ts");

        try
        {
            await CreateTestTransportStreamAsync(ffmpegPath, firstSource, "black");
            await CreateTestTransportStreamAsync(ffmpegPath, secondSource, "blue");

            Assert.True(await new Converter().ExecuteSessionPartsAsync(sourcePattern, [firstSource, secondSource], ".ts"));
            Assert.True(File.Exists(Path.Combine(directory, "session.ts")));
            Assert.False(File.Exists(Path.Combine(directory, "session_000_2.ts")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteSessionPartsAsync_MergesRawFlvIntoOneSourceFormatTarget()
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        Assert.True(File.Exists(ffmpegPath));

        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-flv-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePattern = Path.Combine(directory, "session_%03d.flv");
        string firstSource = Path.Combine(directory, "session_000.flv");
        string secondSource = Path.Combine(directory, "session_001.flv");

        try
        {
            await CreateTestFlvAsync(ffmpegPath, firstSource, "black");
            await CreateTestFlvAsync(ffmpegPath, secondSource, "blue");

            Assert.True(await new Converter().ExecuteSessionPartsAsync(sourcePattern, [firstSource, secondSource], ".flv"));
            Assert.True(File.Exists(Path.Combine(directory, "session.flv")));
            Assert.False(File.Exists(Path.Combine(directory, "session_000_2.flv")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CreateTestTransportStreamAsync(string ffmpegPath, string targetPath, string color)
    {
        await CreateTestVideoAsync(ffmpegPath, targetPath, color, "mpegts");
    }

    private static async Task CreateTestFlvAsync(string ffmpegPath, string targetPath, string color)
    {
        await CreateTestVideoAsync(ffmpegPath, targetPath, color, "flv");
    }

    private static async Task CreateTestVideoAsync(string ffmpegPath, string targetPath, string color, string muxer)
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
        process.StartInfo.ArgumentList.Add(muxer);
        process.StartInfo.ArgumentList.Add(targetPath);

        process.Start();
        Task errorTask = process.StandardError.ReadToEndAsync();
        Task outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(errorTask, outputTask);

        Assert.Equal(0, process.ExitCode);
    }
}
