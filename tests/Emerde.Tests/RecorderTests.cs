using Emerde.Core;

namespace Emerde.Tests;

public sealed class RecorderTests
{
    [Fact]
    public void ProcessStopGracePeriod_KeepsExplicitStopResponsive()
    {
        Assert.InRange(Recorder.ProcessStopGracePeriod, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
    }

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
    [InlineData(".", "recording.flv")]
    [InlineData("CON", "_CON.flv")]
    [InlineData("  custom.  ", "custom.flv")]
    public void BuildOutputFileName_SanitizesInvalidCustomRule(string rule, string expectedFileName)
    {
        RecorderStartInfo startInfo = new()
        {
            NickName = "Host",
            Options = new RoomRecordingOptions { SaveFileNameCustomRule = rule },
        };

        string result = Recorder.BuildOutputFileName("D:\\records", startInfo, Timestamp, false, false);

        Assert.Equal(Path.Combine("D:\\records", expectedFileName), result);
    }

    [Fact]
    public void BuildOutputFileName_LimitsCustomRuleLength()
    {
        RecorderStartInfo startInfo = new()
        {
            NickName = "Host",
            Options = new RoomRecordingOptions { SaveFileNameCustomRule = new string('a', 300) },
        };

        string result = Recorder.BuildOutputFileName("D:\\records", startInfo, Timestamp, false, false);

        Assert.Equal(124, Path.GetFileName(result).Length);
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
    [InlineData(0, true, false, 1, false)]
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
        Assert.Equal(1, Recorder.OfflineRefreshConfirmationCount);
        Assert.Equal(TimeSpan.FromSeconds(90), Recorder.ProgressStallTimeout);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(null, true)]
    public void ShouldConsumeReconnectAttempt_OnlyConsumesUnconfirmedStates(bool? isLiveAfterRefresh, bool expected)
    {
        Assert.Equal(expected, Recorder.ShouldConsumeReconnectAttempt(isLiveAfterRefresh));
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

    [Fact]
    public void BuildArguments_PlacesReconnectOptionsBeforeInput()
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

        List<string> argumentList = arguments.ToList();
        int inputIndex = argumentList.IndexOf("-i");
        Assert.True(inputIndex > 0);
        Assert.True(argumentList.IndexOf("-reconnect") < inputIndex);
        Assert.True(argumentList.IndexOf("-reconnect_at_eof") < inputIndex);
        Assert.True(argumentList.IndexOf("-reconnect_on_network_error") < inputIndex);
        Assert.Equal("45000000", argumentList[argumentList.IndexOf("-rw_timeout") + 1]);
        Assert.Equal("90", argumentList[argumentList.IndexOf("-reconnect_delay_total_max") + 1]);
        Assert.Equal("12", argumentList[argumentList.IndexOf("-reconnect_max_retries") + 1]);
        Assert.Equal("pipe:1", argumentList[argumentList.IndexOf("-progress") + 1]);
        Assert.Equal("1", argumentList[argumentList.IndexOf("-stats_period") + 1]);
    }

    [Theory]
    [InlineData(0, false, false, "info")]
    [InlineData(-1, true, false, "info")]
    [InlineData(1, false, true, "info")]
    [InlineData(1, false, false, "warn")]
    public void GetProcessExitLogLevel_DowngradesHandledStops(int exitCode, bool wasCanceled, bool wasStalled, string expected)
    {
        Assert.Equal(expected, Recorder.GetProcessExitLogLevel(exitCode, wasCanceled, wasStalled));
    }

    [Theory]
    [InlineData(false, false, 59, true)]
    [InlineData(true, false, 10, false)]
    [InlineData(false, true, 10, false)]
    [InlineData(false, false, 60, false)]
    public void ShouldLogRapidExit_ExcludesHandledStops(bool wasCanceled, bool wasStalled, double durationSeconds, bool expected)
    {
        Assert.Equal(expected, Recorder.ShouldLogRapidExit(wasCanceled, wasStalled, durationSeconds));
    }

    [Fact]
    public void RecorderProgressTracker_DetectsOnlyStalledMediaTime()
    {
        DateTime startedAt = new(2026, 7, 23, 5, 27, 0, DateTimeKind.Utc);
        RecorderProgressTracker tracker = new(startedAt);

        Assert.False(tracker.IsStalled(startedAt.AddSeconds(29), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15)));
        Assert.True(tracker.IsStalled(startedAt.AddSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15)));

        Assert.True(tracker.Observe("out_time=00:00:01.000000", startedAt.AddSeconds(30)));
        Assert.False(tracker.Observe("out_time=00:00:01.000000", startedAt.AddSeconds(40)));

        Assert.False(tracker.IsStalled(startedAt.AddSeconds(44), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15)));
        Assert.True(tracker.IsStalled(startedAt.AddSeconds(45), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15)));

        Assert.False(tracker.Observe("out_time=00:00:02.000000", startedAt.AddSeconds(45)));

        Assert.False(tracker.IsStalled(startedAt.AddSeconds(59), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15)));
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
