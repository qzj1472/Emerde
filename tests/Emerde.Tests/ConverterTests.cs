using Emerde.Core;

namespace Emerde.Tests;

[Collection("MediaOperationRegistry")]
public sealed class ConverterTests
{
    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(102, 100, true)]
    [InlineData(102.001, 100, false)]
    [InlineData(100, 102, true)]
    [InlineData(100, 102.001, false)]
    [InlineData(0, 100, true)]
    [InlineData(100, 0, true)]
    public void TrackTimelineValidation_RejectsEitherTrackPastTheOther(
        double audioEndSeconds,
        double videoEndSeconds,
        bool expected)
    {
        Assert.Equal(expected, Converter.IsTrackTimelineWithinTolerance(audioEndSeconds, videoEndSeconds));
    }

    [Fact]
    public void SourceSegmentTimelineValidation_DoesNotLetOppositeTailErrorsCancel()
    {
        FfmpegMediaProbeResult aligned = CreateProbe(10, 10);
        FfmpegMediaProbeResult audioAhead = CreateProbe(14, 10);
        FfmpegMediaProbeResult videoAhead = CreateProbe(10, 14);

        Assert.Equal(1, Converter.FindFirstSourceTimelineMismatchIndex([aligned, audioAhead, videoAhead]));
    }

    [Fact]
    public void SourceSegmentTimelineValidation_DetectsCumulativeDriftAtTheFirstAffectedPart()
    {
        FfmpegMediaProbeResult first = CreateProbe(10, 11.1);
        FfmpegMediaProbeResult second = CreateProbe(10, 11.1);
        FfmpegMediaProbeResult third = CreateProbe(10, 11.1);

        Assert.Equal(1, Converter.FindFirstSourceTimelineMismatchIndex([first, second, third]));
    }

    [Fact]
    public void SourceSegmentTimelineValidation_DoesNotAccumulateConstantStartOffsets()
    {
        FfmpegMediaProbeResult first = CreateProbe(10.3, 10) with
        {
            AudioStartSeconds = 0.3d,
        };
        FfmpegMediaProbeResult second = first;
        FfmpegMediaProbeResult third = first;

        Assert.Equal(-1, Converter.FindFirstSourceTimelineMismatchIndex([first, second, third]));
    }

    [Theory]
    [InlineData(0, 0, 100, 100, true)]
    [InlineData(0, 2, 100, 102, true)]
    [InlineData(0, 2.001, 102.001, 102.001, true)]
    [InlineData(0, 2.001, 100, 100, true)]
    [InlineData(2.001, 0, 100, 100, true)]
    [InlineData(2, 0, 102, 100, true)]
    [InlineData(0, 0, 100, 102.001, false)]
    [InlineData(0, 0, 102.001, 100, false)]
    public void SourceTimelineEligibility_RejectsStartOrEndMismatch(
        double audioStartSeconds,
        double videoStartSeconds,
        double audioEndSeconds,
        double videoEndSeconds,
        bool expected)
    {
        FfmpegMediaProbeResult probe = CreateProbe(audioEndSeconds, videoEndSeconds) with
        {
            AudioStartSeconds = audioStartSeconds,
            VideoStartSeconds = videoStartSeconds,
        };

        Assert.Equal(expected, Converter.IsSourceTimelineWithinTolerance(probe));
    }

    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(102, 100, 100)]
    [InlineData(102.001, 100, 0)]
    [InlineData(4257.260, 4278.433, 0)]
    public void GetTimelineIssueStartSeconds_UsesWholeFileForLargeFixedOffset(
        double audioEndSeconds,
        double videoEndSeconds,
        double expected)
    {
        Assert.Equal(expected, Converter.GetTimelineIssueStartSeconds(audioEndSeconds, videoEndSeconds));
    }

    [Theory]
    [InlineData(true, true, 100, 100, "")]
    [InlineData(true, true, 100, 102, "")]
    [InlineData(true, true, 100, 102.001, "output_track_timeline_mismatch")]
    [InlineData(true, true, 0, 100, "output_track_timeline_unknown")]
    [InlineData(true, false, 0, 100, "")]
    public void OutputTrackTimelineValidation_RejectsUnknownAndVisibleOffsets(
        bool hasAudio,
        bool hasVideo,
        double audioEndSeconds,
        double videoEndSeconds,
        string expected)
    {
        Assert.Equal(
            expected,
            Converter.GetTrackTimelineValidationError(hasAudio, hasVideo, audioEndSeconds, videoEndSeconds));
    }

    [Theory]
    [InlineData(0, 2, 100, 100, "")]
    [InlineData(0, 2.001, 100, 100, "output_track_timeline_start_mismatch")]
    [InlineData(0, 0, 100, 102.001, "output_track_timeline_mismatch")]
    public void OutputTrackTimelineValidation_RejectsStartAndEndOffsets(
        double audioStartSeconds,
        double videoStartSeconds,
        double audioEndSeconds,
        double videoEndSeconds,
        string expected)
    {
        Assert.Equal(
            expected,
            Converter.GetTrackTimelineValidationError(
                true,
                true,
                audioStartSeconds,
                videoStartSeconds,
                audioEndSeconds,
                videoEndSeconds));
    }

    [Fact]
    public void OutputTrackTimelineValidation_DoesNotTreatUnknownStartAsZero()
    {
        Assert.Equal(
            string.Empty,
            Converter.GetTrackTimelineValidationError(
                true,
                true,
                0d,
                1.25d,
                100d,
                100d,
                hasAudioStartTimestamp: false,
                hasVideoStartTimestamp: true));
    }

    [Fact]
    public void OutputTrackTimelineValidation_StillRejectsKnownStartOffsetOverTolerance()
    {
        Assert.Equal(
            "output_track_timeline_start_mismatch",
            Converter.GetTrackTimelineValidationError(
                true,
                true,
                0d,
            2.001d,
                100d,
                100d,
                hasAudioStartTimestamp: true,
                hasVideoStartTimestamp: true));
    }

    [Fact]
    public void PacketDiagnosticsValidation_RequiresPacketsAndKeyframes()
    {
        FfmpegMediaProbeResult output = CreateProbe(100d, 100d) with
        {
            PacketDiagnosticsComplete = true,
            AudioPacketCount = 10,
            VideoPacketCount = 10,
            VideoKeyframeCount = 0,
        };

        Assert.Equal(
            "output_video_keyframe_missing",
            Converter.GetPacketDiagnosticsValidationError(output, requireAudio: true, requireVideo: true));
    }

    [Fact]
    public void PacketDiagnosticsValidation_RejectsInternalPauseAndTimestampReversal()
    {
        FfmpegMediaProbeResult output = CreateProbe(100d, 100d) with
        {
            PacketDiagnosticsComplete = true,
            AudioPacketCount = 10,
            VideoPacketCount = 10,
            VideoKeyframeCount = 1,
            InterPacketGapCount = 1,
            MaximumInterPacketGapSeconds = 3d,
        };

        Assert.Equal(
            "output_internal_pause:count=1,maximum=3.000",
            Converter.GetPacketDiagnosticsValidationError(output, requireAudio: true, requireVideo: true));

        output = output with { InterPacketGapCount = 0, TimestampDiscontinuityCount = 1 };
        Assert.Equal(
            "output_timestamp_discontinuity:1",
            Converter.GetPacketDiagnosticsValidationError(output, requireAudio: true, requireVideo: true));
    }

    [Fact]
    public void PacketDiagnosticsValidation_AllowsCompleteContinuousOutput()
    {
        FfmpegMediaProbeResult output = CreateProbe(100d, 100d) with
        {
            PacketDiagnosticsComplete = true,
            AudioPacketCount = 10,
            VideoPacketCount = 10,
            VideoKeyframeCount = 1,
        };

        Assert.Empty(Converter.GetPacketDiagnosticsValidationError(output, requireAudio: true, requireVideo: true));
    }

    [Theory]
    [InlineData(103, 100, 102, 100, "")]
    [InlineData(103, 100, 100, 102, "output_track_timeline_mismatch")]
    public void OutputTrackTimelineValidation_AccountsForSourceDriftWithoutAllowingNewOppositeDrift(
        double audioEndSeconds,
        double videoEndSeconds,
        double sourceAudioEndSeconds,
        double sourceVideoEndSeconds,
        string expected)
    {
        Assert.Equal(
            expected,
            Converter.GetTrackTimelineValidationError(
                true,
                true,
                0d,
                0d,
                audioEndSeconds,
                videoEndSeconds,
                sourceAudioEndSeconds,
                sourceVideoEndSeconds));
    }

    [Theory]
    [InlineData(100d, 99d, 100d)]
    [InlineData(0d, 99d, 99d)]
    [InlineData(0d, 0d, 0d)]
    public void TrackEndResolution_UsesPacketEvidenceOnlyWhenProbeIsUnknown(
        double probedEndSeconds,
        double packetEndSeconds,
        double expected)
    {
        Assert.Equal(expected, Converter.ResolveTrackEndSeconds(probedEndSeconds, packetEndSeconds));
    }

    [Theory]
    [InlineData(true, false, true, true, true)]
    [InlineData(false, true, true, true, true)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, false, false)]
    public void InitialTimelineSynchronization_CoversLiveAndFileRemuxInputs(
        bool isLiveInput,
        bool isFileRemux,
        bool hasVideoReference,
        bool hasAudio,
        bool expected)
    {
        Assert.Equal(
            expected,
            FfmpegMediaEngine.ShouldSynchronizeInitialTimeline(isLiveInput, isFileRemux, hasVideoReference, hasAudio));
    }

    [Theory]
    [InlineData(4257.260, 4278.433, 4257.100, 4278.300, true)]
    [InlineData(22255.046, 22348.884, 22255.000, 22348.762, true)]
    [InlineData(96, 100, 96, 100, true)]
    [InlineData(94.999, 100, 94, 100, true)]
    [InlineData(90, 100, 100, 100, false)]
    [InlineData(110, 100, 90, 100, false)]
    [InlineData(100, 100, 90, 100, true)]
    [InlineData(0, 100, 0, 100, true)]
    public void TrackTimelineValidation_RejectsSourceOrOutputTrackTailDifference(
        double audioEndSeconds,
        double videoEndSeconds,
        double sourceAudioEndSeconds,
        double sourceVideoEndSeconds,
        bool expected)
    {
        Assert.Equal(
            expected,
            Converter.IsTrackTimelineWithinTolerance(
                audioEndSeconds,
                videoEndSeconds,
                sourceAudioEndSeconds,
                sourceVideoEndSeconds));
    }

    [Fact]
    public void TrackTimelineValidation_RejectsTheTwentyFourSecondFinalPacketTailMismatch()
    {
        Assert.False(Converter.IsTrackTimelineWithinTolerance(
            15892.166d,
            15916.112d,
            15892.02d,
            15892.05d));
    }

    [Fact]
    public void RemoveProvisionalTimelineWarning_ClearsOnlyTransientNormalizationIssue()
    {
        VideoRecordingMetadata metadata = new()
        {
            MediaIssue = "timeline_mismatch;video_decode_error",
            IssueIntervals =
            [
                new VideoRecordingIssueInterval
                {
                    Kind = "timeline_mismatch",
                    StartSeconds = 10d,
                    EndSeconds = 10.2d,
                    Detail = "output_timestamp_normalization_mismatch",
                },
            ],
        };

        Converter.RemoveProvisionalTimelineWarning(metadata);

        Assert.Equal("video_decode_error", metadata.MediaIssue);
        Assert.Empty(metadata.IssueIntervals);
    }

    [Fact]
    public void RemoveProvisionalTimelineWarning_PreservesConfirmedTimelineDamage()
    {
        VideoRecordingMetadata metadata = new()
        {
            MediaIssue = "timeline_mismatch",
            IssueIntervals =
            [
                new VideoRecordingIssueInterval
                {
                    Kind = "timeline_mismatch",
                    StartSeconds = 10d,
                    EndSeconds = 12d,
                    Detail = "source_track_end_mismatch",
                },
                new VideoRecordingIssueInterval
                {
                    Kind = "timeline_mismatch",
                    StartSeconds = 12d,
                    EndSeconds = 12.1d,
                    Detail = "output_timestamp_normalization_mismatch",
                },
            ],
        };

        Converter.RemoveProvisionalTimelineWarning(metadata);

        Assert.Equal("timeline_mismatch", metadata.MediaIssue);
        Assert.Single(metadata.IssueIntervals);
        Assert.Equal("source_track_end_mismatch", metadata.IssueIntervals[0].Detail);
    }

    [Fact]
    public void TryGetAlignedTimelineEnd_UsesTheSharedTrackBoundary()
    {
        FfmpegMediaProbeResult probe = new(
            true,
            true,
            1,
            1,
            96,
            100,
            1920,
            1080,
            30,
            100,
            0,
            "h264",
            "aac",
            string.Empty,
            new VideoRecordingMetadata(),
            false,
            0d,
            0d,
            true,
            true);

        Assert.True(Converter.TryGetAlignedTimelineEnd([probe], out double endSeconds));
        Assert.Equal(96, endSeconds);
    }

    [Fact]
    public void TryGetAlignedTimelineEnd_SumsSessionPartsBeforeAligning()
    {
        FfmpegMediaProbeResult first = new(
            true, true, 1, 1, 40, 42, 1920, 1080, 30, 42, 0, "h264", "aac", string.Empty, new VideoRecordingMetadata(), false);
        FfmpegMediaProbeResult second = first with { AudioEndSeconds = 50, VideoEndSeconds = 48, DurationSeconds = 50 };

        Assert.True(Converter.TryGetAlignedTimelineEnd([first, second], out double endSeconds));
        Assert.Equal(90, endSeconds);
    }

    [Fact]
    public void SourceTimelineEnd_UsesRelativeDurationWhenStreamsStartAtNonZeroPts()
    {
        FfmpegMediaProbeResult probe = new(
            true,
            true,
            1,
            1,
            3720,
            3718,
            1920,
            1080,
            30,
            120,
            0,
            "h264",
            "aac",
            string.Empty,
            new VideoRecordingMetadata(),
            false,
            3600,
            3600,
            true,
            true);

        Assert.Equal(120, Converter.GetSourceTimelineEndSeconds(probe), 3);
        Assert.Equal(120, Converter.GetAudioDurationSeconds(probe), 3);
        Assert.Equal(118, Converter.GetVideoDurationSeconds(probe), 3);
    }

    private static FfmpegMediaProbeResult CreateProbe(double audioEndSeconds, double videoEndSeconds)
    {
        return new FfmpegMediaProbeResult(
            true,
            true,
            1,
            1,
            audioEndSeconds,
            videoEndSeconds,
            1920,
            1080,
            30,
            Math.Max(audioEndSeconds, videoEndSeconds),
            0,
            "h264",
            "aac",
            string.Empty,
            new VideoRecordingMetadata(),
            false,
            0d,
            0d,
            true,
            true);
    }

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
    [InlineData(3600d, 3598d, true)]
    [InlineData(3600d, 3597.9d, false)]
    [InlineData(3600d, 3602d, true)]
    [InlineData(3600d, 3602.1d, false)]
    [InlineData(20000d, 20002d, true)]
    [InlineData(20000d, 20002.1d, false)]
    [InlineData(3600d, 0d, false)]
    [InlineData(0d, 0d, true)]
    public void OutputDuration_RejectsShortfallAndBoundsSurplus(double expected, double actual, bool accepted)
    {
        Assert.Equal(accepted, Converter.IsDurationWithinTolerance(expected, actual));
    }

    [Theory]
    [InlineData(454.479, 460.371, 458.831, true)]
    [InlineData(100, 111, 100, false)]
    [InlineData(100, 97, 100, false)]
    [InlineData(0, 10, 10, false)]
    public void IsRecoverableDurationMismatch_PreservesCompleteOutputsOnly(
        double expected,
        double actual,
        double probed,
        bool recoverable)
    {
        Assert.Equal(recoverable, Converter.IsRecoverableDurationMismatch(expected, actual, probed));
    }

    [Theory]
    [InlineData(7200d, 7198.5d, 7198.5d)]
    [InlineData(7200d, 0d, 7200d)]
    [InlineData(-1d, -1d, 0d)]
    public void ExpectedDuration_PrefersProcessedPacketTimeline(double probed, double processed, double expected)
    {
        Assert.Equal(expected, Converter.SelectExpectedDuration(probed, processed));
    }

    [Theory]
    [InlineData(20d, 18d, 19d, 20d)]
    [InlineData(0d, 18d, 19d, 19d)]
    [InlineData(20d, 0d, 0d, 20d)]
    public void SourceTimelineEnd_UsesTheLatestAvailableProbeBoundary(
        double audioEndSeconds,
        double videoEndSeconds,
        double durationSeconds,
        double expected)
    {
        Assert.Equal(
            expected,
            Converter.GetSourceTimelineEndSeconds(new FfmpegMediaProbeResult(
                audioEndSeconds > 0,
                videoEndSeconds > 0,
                audioEndSeconds > 0 ? 1 : 0,
                videoEndSeconds > 0 ? 1 : 0,
                audioEndSeconds,
                videoEndSeconds,
                1920,
                1080,
                30,
                durationSeconds,
                0,
                "h264",
                "aac",
                string.Empty,
                new VideoRecordingMetadata(),
                false)));
    }

    [Theory]
    [InlineData(22278.651, 22082.523, false)]
    [InlineData(3600, 3585, true)]
    [InlineData(3600, 3584.9, false)]
    [InlineData(20, 8, true)]
    [InlineData(0, 0, true)]
    public void RecordingDurationValidation_RejectsMaterialTimelineLoss(double expected, double actual, bool accepted)
    {
        Assert.Equal(accepted, Converter.IsRecordingDurationComplete(expected, actual));
    }

    [Fact]
    public void RecordingExpectedDuration_UsesRecordedAtAndLastSourceWrite()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-duration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "session.flv");
        File.WriteAllBytes(sourcePath, [1]);
        DateTime recordedAt = new(2026, 8, 12, 22, 4, 55, DateTimeKind.Local);
        File.SetLastWriteTime(sourcePath, recordedAt.AddSeconds(22278.651));
        try
        {
            double duration = Converter.GetRecordingExpectedDuration(
                new VideoRecordingMetadata { RecordedAt = recordedAt },
                [new FileInfo(sourcePath)]);

            Assert.Equal(22278.651, duration, 3);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(22278.651, 22082.523, 22278.651)]
    [InlineData(7200, 3600, 0)]
    [InlineData(0, 3600, 0)]
    public void RecordingExpectedDuration_IgnoresImplausibleCopiedFileTimestamps(
        double wallClockDuration,
        double mediaDuration,
        double expected)
    {
        Assert.Equal(expected, Converter.NormalizeRecordingExpectedDuration(wallClockDuration, mediaDuration), 3);
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

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 2)]
    public void ConversionConcurrency_UsesRecordingAwareLimits(
        bool hasActiveRecorders,
        int expectedTotal)
    {
        ConversionConcurrencyProfile profile = ConversionWorkScheduler.GetProfile(hasActiveRecorders);

        Assert.Equal(expectedTotal, profile.TotalLimit);
    }

    [Fact]
    public async Task ConversionConcurrency_AllowsFourIdleJobs()
    {
        List<IDisposable> leases = [];
        try
        {
            for (int index = 0; index < 4; index++)
            {
                leases.Add(await ConversionWorkScheduler.EnterAsync(CancellationToken.None));
            }

            Task<IDisposable> queued = ConversionWorkScheduler.EnterAsync(CancellationToken.None);
            await Task.Delay(50);
            Assert.False(queued.IsCompleted);

            leases[0].Dispose();
            leases.RemoveAt(0);
            leases.Add(await queued.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            foreach (IDisposable lease in leases)
            {
                lease.Dispose();
            }
        }
    }

    [Fact]
    public async Task ConversionConcurrency_LimitsNewJobsWhileRecording()
    {
        using IDisposable recording = MediaOperationRegistry.Register(MediaOperationKind.Recording, () => []);
        List<IDisposable> leases = [];
        try
        {
            leases.Add(await ConversionWorkScheduler.EnterAsync(CancellationToken.None));
            leases.Add(await ConversionWorkScheduler.EnterAsync(CancellationToken.None));

            Task<IDisposable> queued = ConversionWorkScheduler.EnterAsync(CancellationToken.None);
            await Task.Delay(50);
            Assert.False(queued.IsCompleted);

            leases[0].Dispose();
            leases.RemoveAt(0);
            leases.Add(await queued.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            foreach (IDisposable lease in leases)
            {
                lease.Dispose();
            }
        }
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

    [Fact]
    public async Task ExecuteWithCompletionAsync_UsesRequestedTargetPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-converter-requested-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "session_000.ts");
        string requestedTarget = Path.Combine(directory, "session.mkv");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        string? reservedTarget = null;

        try
        {
            bool converted = await new Converter().ExecuteWithCompletionAsync(
                source,
                ".mkv",
                _ => { },
                onTargetReserved: path => reservedTarget = path,
                requestedTargetPath: requestedTarget);

            Assert.False(converted);
            Assert.Equal(requestedTarget, reservedTarget);
            Assert.False(File.Exists(requestedTarget));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}
