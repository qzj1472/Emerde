using Emerde.Core;

namespace Emerde.Tests;

public sealed class RecorderTests
{
    [Fact]
    public void GetAvailableTargetPath_PreservesExistingTarget()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string requested = Path.Combine(directory, "video.mkv");
        File.WriteAllBytes(requested, [1]);

        try
        {
            Assert.Equal(Path.Combine(directory, "video_2.mkv"), Converter.GetAvailableTargetPath(requested));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static readonly DateTime Timestamp = new(2026, 7, 3, 12, 34, 56);

    [Theory]
    [InlineData(true, true, "Host_2026-07-03_12-34-56_%03d.ts")]
    [InlineData(false, true, "Host_2026-07-03_12-34-56_%03d.ts")]
    [InlineData(true, false, "Host_2026-07-03_12-34-56.ts")]
    [InlineData(false, false, "Host_2026-07-03_12-34-56.flv")]
    public void BuildOutputFileName_SelectsExpectedSuffix(bool isHls, bool isToSegment, string expectedFileName)
    {
        string result = Recorder.BuildOutputFileName("D:\\records", "Host", Timestamp, isToSegment, isHls);

        Assert.Equal(Path.Combine("D:\\records", expectedFileName), result);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public void CanRetryRecording_StopsAfterFourAttempts(int completedAttempts, bool expected)
    {
        Assert.Equal(expected, Recorder.CanRetryRecording(completedAttempts));
    }

    [Fact]
    public void ReserveOutput_AppendsSuffixForConcurrentRecording()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EmerdeRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using Recorder.OutputReservation first = Recorder.ReserveOutput(directory, "Host", false, false);
            using Recorder.OutputReservation second = Recorder.ReserveOutput(directory, "Host", false, false);

            Assert.Equal(Path.Combine(directory, "Host.flv"), first.OutputPattern);
            Assert.Equal(Path.Combine(directory, "Host_2.flv"), second.OutputPattern);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ReserveOutput_AppendsSuffixWhenFileAlreadyExists()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EmerdeRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Host.flv"), "existing");
        try
        {
            using Recorder.OutputReservation reservation = Recorder.ReserveOutput(directory, "Host", false, false);

            Assert.Equal(Path.Combine(directory, "Host_2.flv"), reservation.OutputPattern);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(0, false, null, 0, false)]
    [InlineData(0, true, null, 0, true)]
    [InlineData(0, true, true, 0, true)]
    [InlineData(0, true, false, 1, true)]
    [InlineData(0, true, false, 2, false)]
    [InlineData(1, false, null, 0, true)]
    [InlineData(1, true, false, 2, false)]
    public void ShouldRetryRecording_RefreshesNormalEofAndStopsOnConfirmedOffline(
        int exitCode,
        bool hasStreamRefresh,
        bool? isLiveAfterRefresh,
        int offlineRefreshChecks,
        bool expected)
    {
        Assert.Equal(expected, Recorder.ShouldRetryRecording(exitCode, hasStreamRefresh, isLiveAfterRefresh, offlineRefreshChecks));
    }

    [Theory]
    [InlineData(true, false, null, true)]
    [InlineData(false, true, null, true)]
    [InlineData(false, false, ".mp4", true)]
    [InlineData(false, false, ".mkv", true)]
    [InlineData(false, false, null, false)]
    public void ShouldUseTransportStream_MatchesAudioProcessingRequirements(bool isHls, bool isToSegment, string? targetFormat, bool expected)
    {
        Assert.Equal(expected, Recorder.ShouldUseTransportStream(isHls, isToSegment, targetFormat));
    }

    [Fact]
    public void BuildAudioMappingArguments_AddsOriginalAndOptimizedTracks()
    {
        IReadOnlyList<string> arguments = Recorder.BuildAudioMappingArguments(useOptimizedAudio: true);

        Assert.Contains("-filter_complex", arguments);
        Assert.Contains("0:a:0?", arguments);
        Assert.Contains("[aopt]", arguments);
        Assert.Contains("title=原音频", arguments);
        Assert.Contains("title=优化音频", arguments);
    }

    [Fact]
    public void BuildArguments_DirectCopyDoesNotApplyRateControl()
    {
        Recorder recorder = new() { Url = "https://example.test/live.flv" };

        IReadOnlyList<string> arguments = recorder.BuildArguments(
            "D:\\records\\Host.flv",
            false,
            string.Empty,
            string.Empty,
            "EmerdeTest",
            false,
            false,
            1,
            SegmentTimeUnitHelper.Seconds,
            new VideoRecordingMetadata(),
            false);

        Assert.Contains("-c:v", arguments);
        Assert.Contains("copy", arguments);
        Assert.DoesNotContain("-b:v", arguments);
        Assert.DoesNotContain("-minrate", arguments);
        Assert.DoesNotContain("-maxrate", arguments);
        Assert.DoesNotContain("-bufsize", arguments);
        Assert.Contains("-n", arguments);
        Assert.DoesNotContain("-y", arguments);
    }

    [Theory]
    [InlineData("Stream specifier ':a:0' matches no streams")]
    [InlineData("Cannot find a matching stream for unlabeled input pad")]
    [InlineData("Streamcopy requested for output stream fed from a complex filtergraph")]
    public void IsMissingAudioError_RecognizesFfmpegFailures(string errorOutput)
    {
        Assert.True(Recorder.IsMissingAudioError(errorOutput));
    }
}
