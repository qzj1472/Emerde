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
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(null, true, true)]
    public void ShouldConsumeReconnectAttempt_ConsumesLiveAttemptsWithoutMediaProgress(bool? isLiveAfterRefresh, bool hadMediaProgress, bool expected)
    {
        Assert.Equal(expected, Recorder.ShouldConsumeReconnectAttempt(isLiveAfterRefresh, hadMediaProgress));
    }

    [Theory]
    [InlineData(0, false, false, 12, true, true)]
    [InlineData(0, false, false, 12, null, true)]
    [InlineData(0, false, false, 12, false, false)]
    [InlineData(0, false, true, 12, true, false)]
    [InlineData(0, true, false, 12, true, false)]
    [InlineData(1, false, false, 12, true, false)]
    [InlineData(0, false, false, 60, true, false)]
    public void ShouldSuppressRapidRetry_OnlySuppressesShortCleanUnconfirmedRetries(
        int exitCode,
        bool wasCanceled,
        bool wasStalled,
        double durationSeconds,
        bool? isLiveAfterRefresh,
        bool expected)
    {
        Assert.Equal(expected, Recorder.ShouldSuppressRapidRetry(exitCode, wasCanceled, wasStalled, durationSeconds, isLiveAfterRefresh));
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

    [Theory]
    [InlineData("Bilibili", "https://example.test/live.m3u8", false, false, "https://example.test/live.flv")]
    [InlineData("bilibili", "https://example.test/live.m3u8", false, false, "https://example.test/live.flv")]
    [InlineData("Douyin", "https://example.test/live.m3u8", false, false, null)]
    [InlineData("Bilibili", "https://example.test/live.m3u8", true, false, null)]
    [InlineData("Bilibili", "https://example.test/live.m3u8", false, true, null)]
    [InlineData("Bilibili", "https://example.test/live.flv", false, false, null)]
    public void SelectInputFallback_UsesBilibiliFlvOnlyBeforePrimaryMediaProgress(
        string platformName,
        string currentUrl,
        bool hadMediaProgress,
        bool alreadyTried,
        string? expected)
    {
        string? fallback = Recorder.SelectInputFallback(
            platformName,
            currentUrl,
            "https://example.test/live.m3u8",
            "https://example.test/live.flv",
            hadMediaProgress,
            alreadyTried);

        Assert.Equal(expected, fallback);
    }

    [Fact]
    public void InputFallback_RemovesFailedOutputBeforeAdvancingSessionPart()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "Recorder.cs"));
        int fallbackStart = source.IndexOf("string? fallbackUrl = SelectInputFallback(", StringComparison.Ordinal);
        int failedOutputDelete = source.IndexOf("DeleteFailedOutputFiles(outputFileName", fallbackStart, StringComparison.Ordinal);
        int fallbackContinue = source.IndexOf("continue;", failedOutputDelete, StringComparison.Ordinal);
        int sessionPartAdvance = source.IndexOf("sessionPartIndex++;", fallbackContinue, StringComparison.Ordinal);

        Assert.True(fallbackStart >= 0);
        Assert.True(failedOutputDelete > fallbackStart);
        Assert.True(fallbackContinue > failedOutputDelete);
        Assert.True(sessionPartAdvance > fallbackContinue);
    }

    [Fact]
    public void InitialSessionRecoveryMarker_PreservesOptimizedAudioSelection()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "Recorder.cs"));
        int initialRegistration = source.IndexOf(
            "string? pendingRecordingPath = RecordingRecoveryService.RegisterSessionParts(",
            StringComparison.Ordinal);
        int optimizeAudioArgument = source.IndexOf(
            "recordingOptions.IsOptimizeAudio);",
            initialRegistration,
            StringComparison.Ordinal);

        Assert.True(initialRegistration >= 0);
        Assert.True(optimizeAudioArgument > initialRegistration);
    }

    [Theory]
    [InlineData(0, "record_000.ts")]
    [InlineData(1, "record_001.ts")]
    [InlineData(12, "record_012.ts")]
    [InlineData(1234, "record_1234.ts")]
    public void BuildSessionPartOutputFileName_ExpandsSharedRecordingPattern(int index, string expected)
    {
        Assert.Equal(expected, Recorder.BuildSessionPartOutputFileName("record_%03d.ts", index));
    }

    [Theory]
    [InlineData("ts", "record_%03d.ts")]
    [InlineData(".flv", "record_%03d.flv")]
    public void BuildSessionOutputFileName_UsesSourceContainerForInternalParts(string sourceExtension, string expected)
    {
        Assert.Equal(Path.Combine("D:\\records", expected), Recorder.BuildSessionOutputFileName("D:\\records", "record", sourceExtension));
    }

    [Fact]
    public void DeleteFailedOutputFiles_RemovesOnlyCurrentSessionPart()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recorder-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string firstPart = Path.Combine(directory, "record_000.ts");
        string failedPart = Path.Combine(directory, "record_001.ts");
        string metadataPath = Path.Combine(directory, "record.mplr.json");

        try
        {
            File.WriteAllBytes(firstPart, [1]);
            File.WriteAllBytes(failedPart, [1]);
            File.WriteAllText(metadataPath, "{}");

            Recorder.DeleteFailedOutputFiles(failedPart, metadataPath: null);

            Assert.True(File.Exists(firstPart));
            Assert.False(File.Exists(failedPart));
            Assert.True(File.Exists(metadataPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UsableOutput_ExcludesAndRemovesZeroByteSessionParts()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recorder-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string pattern = Path.Combine(directory, "record_%03d.ts");
        string emptyPart = Path.Combine(directory, "record_000.ts");
        string mediaPart = Path.Combine(directory, "record_001.ts");

        try
        {
            File.WriteAllBytes(emptyPart, []);
            Assert.False(Recorder.HasUsableOutput(pattern));

            File.WriteAllBytes(mediaPart, [1, 2, 3]);
            Assert.True(Recorder.HasUsableOutput(pattern));

            Recorder.DeleteEmptyOutputFiles(pattern);

            Assert.False(File.Exists(emptyPart));
            Assert.True(File.Exists(mediaPart));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UsableOutput_IncludesLegacyLiteralSegmentPattern()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-recorder-legacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string pattern = Path.Combine(directory, "record_%03d.ts");

        try
        {
            File.WriteAllBytes(pattern, [1, 2, 3]);

            Assert.True(Recorder.HasUsableOutput(pattern));

            Recorder.DeleteFailedOutputFiles(pattern, metadataPath: null);

            Assert.False(File.Exists(pattern));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MediaWorkerOutputLength_SumsMatchingSegmentFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-worker-progress-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string pattern = Path.Combine(directory, "record_%03d.ts");

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "record_000.ts"), new byte[3]);
            File.WriteAllBytes(Path.Combine(directory, "record_001.ts"), new byte[5]);
            File.WriteAllBytes(Path.Combine(directory, "record_other.ts"), new byte[11]);

            Assert.Equal(8, MediaWorker.GetOutputLength(pattern, 0));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SegmentPaths_OrderFourDigitIndexesNumerically()
    {
        string[] paths =
        [
            @"D:\records\record_1000.ts",
            @"D:\records\record_101.ts",
            @"D:\records\record_999.ts",
        ];

        Assert.Equal(
            [@"D:\records\record_101.ts", @"D:\records\record_999.ts", @"D:\records\record_1000.ts"],
            MediaFileCatalog.OrderSegmentPaths(paths, "record_%03d.ts"));
    }

    [Fact]
    public void MediaWorkerControlInput_RunsOnBackgroundThreadAndCancelsOnQuit()
    {
        using CancellationTokenSource stopSource = new();
        using BlockingTextReader reader = new("q");

        Thread thread = MediaWorker.StartControlInputReader(stopSource, reader);

        Assert.True(reader.WaitUntilReading(TimeSpan.FromSeconds(1)));
        Assert.True(thread.IsBackground);
        reader.Release();
        Assert.True(thread.Join(TimeSpan.FromSeconds(1)));
        Assert.True(stopSource.IsCancellationRequested);
    }

    [Fact]
    public void MediaWorkerProgress_AcceptsInputAdvanceBeforeFileLengthChanges()
    {
        Recorder recorder = new();
        RecorderStartInfo startInfo = new();
        DateTime startedAt = DateTime.UtcNow;

        Assert.True(recorder.UpdateMediaWorkerWriteSpeed("progress|0|1024", startedAt, startInfo, "record.ts", out long firstProgress));
        Assert.False(recorder.UpdateMediaWorkerWriteSpeed("progress|0|1024", startedAt.AddSeconds(1), startInfo, "record.ts", out _));
        Assert.True(recorder.UpdateMediaWorkerWriteSpeed("progress|0|2048", startedAt.AddSeconds(2), startInfo, "record.ts", out long secondProgress));

        Assert.Equal(1024, firstProgress);
        Assert.Equal(2048, secondProgress);
    }

    [Fact]
    public void MediaWorkerProgress_ParsesPerTrackPacketCounters()
    {
        Assert.True(Recorder.TryParseMediaWorkerPacketProgress("progress|4096|8192|12|34", out long videoPackets, out long audioPackets));
        Assert.Equal(12, videoPackets);
        Assert.Equal(34, audioPackets);
        Assert.True(Recorder.TryParseMediaWorkerPacketProgress("progress|4096|8192|0|34|1|1", out videoPackets, out audioPackets, out bool hasVideoStream));
        Assert.True(hasVideoStream);
        Assert.False(Recorder.TryParseMediaWorkerPacketProgress("progress|4096|8192", out _, out _));
    }

    [Fact]
    public void MediaWorkerCommand_PreservesSegmentConfiguration()
    {
        string commandPath = MediaWorker.WriteCommand(
            "https://example.test/live.flv",
            @"D:\records\record_%03d.ts",
            new VideoRecordingMetadata(),
            new FfmpegInputOptions("EmerdeTest", string.Empty, false, string.Empty, true),
            new FfmpegSegmentOptions(30, SegmentTimeUnitHelper.Seconds));

        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(commandPath));
            System.Text.Json.JsonElement segmentOptions = document.RootElement.GetProperty("SegmentOptions");

            Assert.Equal(30, segmentOptions.GetProperty("Value").GetInt64());
            Assert.Equal(SegmentTimeUnitHelper.Seconds, segmentOptions.GetProperty("Unit").GetInt32());
        }
        finally
        {
            File.Delete(commandPath);
        }
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
        Assert.Equal("15000000", argumentList[argumentList.IndexOf("-rw_timeout") + 1]);
        Assert.Equal("+genpts+discardcorrupt+sortdts", argumentList[argumentList.IndexOf("-fflags") + 1]);
        Assert.Equal("ignore_err", argumentList[argumentList.IndexOf("-err_detect") + 1]);
        Assert.True(argumentList.IndexOf("-err_detect") < inputIndex);
        Assert.Equal("90", argumentList[argumentList.IndexOf("-reconnect_delay_total_max") + 1]);
        Assert.Equal("12", argumentList[argumentList.IndexOf("-reconnect_max_retries") + 1]);
        Assert.Equal("pipe:1", argumentList[argumentList.IndexOf("-progress") + 1]);
        Assert.Equal("1", argumentList[argumentList.IndexOf("-stats_period") + 1]);
    }

    [Fact]
    public void BuildArguments_DoesNotAdvertiseUnsupportedSizeSegmentOption()
    {
        Recorder recorder = new() { Url = "https://example.test/live.flv" };

        IReadOnlyList<string> arguments = recorder.BuildArguments(
            @"D:\records\Host_%03d.ts",
            false,
            string.Empty,
            string.Empty,
            "EmerdeTest",
            true,
            true,
            100_000_000,
            SegmentTimeUnitHelper.Megabytes,
            new VideoRecordingMetadata(),
            false);

        Assert.DoesNotContain("-segment_size", arguments);
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

    [Fact]
    public void RecorderProgressTracker_DetectsFrozenVideoWhileAudioContinues()
    {
        DateTime startedAt = new(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc);
        RecorderProgressTracker tracker = new(startedAt);

        Assert.True(tracker.Observe(100, 1, 1, startedAt.AddSeconds(1)));
        Assert.False(tracker.Observe(200, 1, 10, startedAt.AddSeconds(20)));
        Assert.Equal(
            RecorderStallReason.None,
            tracker.GetStallReason(startedAt.AddSeconds(30), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(30)));
        Assert.Equal(
            RecorderStallReason.Video,
            tracker.GetStallReason(startedAt.AddSeconds(31), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void RecorderProgressTracker_DoesNotRequireVideoForAudioOnlyInput()
    {
        DateTime startedAt = new(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc);
        RecorderProgressTracker tracker = new(startedAt);

        Assert.True(tracker.Observe(100, 0, 1, startedAt.AddSeconds(1)));
        Assert.False(tracker.Observe(200, 0, 10, startedAt.AddSeconds(40)));
        Assert.Equal(
            RecorderStallReason.None,
            tracker.GetStallReason(startedAt.AddSeconds(70), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void RecorderProgressTracker_DetectsDeclaredVideoThatNeverProducesPackets()
    {
        DateTime startedAt = new(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc);
        RecorderProgressTracker tracker = new(startedAt);

        Assert.True(tracker.Observe(100, 0, 1, true, startedAt.AddSeconds(1)));
        Assert.False(tracker.Observe(200, 0, 10, true, startedAt.AddSeconds(20)));
        Assert.Equal(
            RecorderStallReason.Video,
            tracker.GetStallReason(startedAt.AddSeconds(31), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void MediaSpeedSummaryWindow_CompressesSamplesIntoIntervalSummary()
    {
        DateTime startedAt = new(2026, 7, 25, 20, 30, 0, DateTimeKind.Utc);
        MediaSpeedSummaryWindow window = new(TimeSpan.FromSeconds(30));

        window.Observe(startedAt, 10, 10_000_000, 8_000_000, 1_000_000);
        window.Observe(startedAt.AddSeconds(10), 10, 20_000_000, 16_000_000, 2_000_000);

        Assert.False(window.ShouldFlush(startedAt.AddSeconds(20)));

        window.Observe(startedAt.AddSeconds(30), 10, 30_000_000, 24_000_000, 3_000_000);

        Assert.True(window.ShouldFlush(startedAt.AddSeconds(30)));
        MediaSpeedSummary? summary = window.Drain();
        Assert.NotNull(summary);
        Assert.Equal(3, summary.Samples);
        Assert.Equal(30, summary.DurationSeconds);
        Assert.Equal(60_000_000, summary.InputBytes);
        Assert.Equal(48_000_000, summary.OutputBytes);
        Assert.Equal(16d, summary.ReadAverageMbps, 6);
        Assert.Equal(8d, summary.ReadMinMbps, 6);
        Assert.Equal(24d, summary.ReadMaxMbps, 6);
        Assert.Null(window.Drain());
    }

    [Fact]
    public void MediaSpeedSummaryWindow_IncludesZeroSpeedSamplesWithoutReusingPreviousRate()
    {
        DateTime startedAt = new(2026, 7, 25, 20, 30, 0, DateTimeKind.Utc);
        MediaSpeedSummaryWindow window = new(TimeSpan.FromSeconds(30));

        window.Observe(startedAt, 10, 10_000_000, 8_000_000, 1_000_000);
        window.Observe(startedAt.AddSeconds(10), 10, 0, 1_000_000, 0);

        MediaSpeedSummary summary = Assert.IsType<MediaSpeedSummary>(window.Drain());
        Assert.Equal(0, summary.ReadMinMbps);
        Assert.Equal(8, summary.ReadMaxMbps);
    }

    [Theory]
    [InlineData("Stream specifier ':a:0' matches no streams")]
    [InlineData("Cannot find a matching stream for unlabeled input pad")]
    [InlineData("Streamcopy requested for output stream fed from a complex filtergraph")]
    public void IsMissingAudioError_RecognizesFfmpegFailures(string errorOutput)
    {
        Assert.True(Recorder.IsMissingAudioError(errorOutput));
    }

    private sealed class BlockingTextReader(string value) : TextReader
    {
        private readonly ManualResetEventSlim reading = new();
        private readonly ManualResetEventSlim released = new();

        public override string? ReadLine()
        {
            reading.Set();
            released.Wait();
            return value;
        }

        public bool WaitUntilReading(TimeSpan timeout)
        {
            return reading.Wait(timeout);
        }

        public void Release()
        {
            released.Set();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                released.Set();
                reading.Dispose();
                released.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
