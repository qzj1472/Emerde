using Emerde.Core;

namespace Emerde.Tests;

public sealed class FfmpegMediaEngineContractTests
{
    [Fact]
    public void CrossStreamSampling_AcceptsFirstSampleWithoutSentinelArithmetic()
    {
        Assert.True(FfmpegMediaEngine.IsCrossStreamSampleDue(
            TimeSpan.Zero,
            null,
            TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void CrossStreamSampling_UsesConfiguredIntervalAfterFirstSample()
    {
        TimeSpan previous = TimeSpan.FromSeconds(1);
        TimeSpan interval = TimeSpan.FromMilliseconds(200);

        Assert.False(FfmpegMediaEngine.IsCrossStreamSampleDue(
            previous + TimeSpan.FromMilliseconds(199),
            previous,
            interval));
        Assert.True(FfmpegMediaEngine.IsCrossStreamSampleDue(
            previous + interval,
            previous,
            interval));
    }

    [Fact]
    public void CrossStreamDecision_RestartsAfterPersistentMismatch()
    {
        FfmpegMediaEngine.CrossStreamDecisionTracker tracker = new(
            0.5,
            0.2,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10));

        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Pending, tracker.Observe(TimeSpan.Zero, 0.7, false));
        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Pending, tracker.Observe(TimeSpan.FromSeconds(4.9), 0.7, false));
        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Restart, tracker.Observe(TimeSpan.FromSeconds(5), 0.7, false));
    }

    [Fact]
    public void CrossStreamDecision_CancelsAfterStableRecovery()
    {
        FfmpegMediaEngine.CrossStreamDecisionTracker tracker = new(
            0.5,
            0.2,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10));

        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Pending, tracker.Observe(TimeSpan.Zero, 0.1, false));
        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Pending, tracker.Observe(TimeSpan.FromSeconds(9.9), 0.1, false));
        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Cancel, tracker.Observe(TimeSpan.FromSeconds(10), 0.1, false));
    }

    [Fact]
    public void CrossStreamDecision_DoesNotResetPersistentMismatchOnRepeatedSamples()
    {
        FfmpegMediaEngine.CrossStreamDecisionTracker tracker = new(
            0.5,
            0.2,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10));

        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Pending, tracker.Observe(TimeSpan.Zero, 0.8, false));
        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Pending, tracker.Observe(TimeSpan.FromSeconds(2), 0.9, true));
        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Restart, tracker.Observe(TimeSpan.FromSeconds(5), 0.8, false));
    }

    [Fact]
    public void CrossStreamDecision_RequiresContinuousFreshEvidenceAfterReset()
    {
        FfmpegMediaEngine.CrossStreamDecisionTracker tracker = new(
            0.5,
            0.2,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10));

        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Pending, tracker.Observe(TimeSpan.Zero, 0.8, false));
        tracker.Reset();
        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Pending, tracker.Observe(TimeSpan.FromSeconds(10), 0.8, false));
        Assert.Equal(FfmpegMediaEngine.CrossStreamDecision.Restart, tracker.Observe(TimeSpan.FromSeconds(15), 0.8, false));
    }

    [Fact]
    public void Remux_PreservesSharedSourceTimelineBeforeApplyingSessionOffset()
    {
        string source = ReadSource();
        int remuxIndex = source.IndexOf("private static FfmpegMediaRunResult Remux(", StringComparison.Ordinal);
        int normalizeIndex = source.IndexOf("NormalizeSourcePacketTimestamps(", remuxIndex, StringComparison.Ordinal);
        int rescaleIndex = source.IndexOf("ffmpeg.av_packet_rescale_ts(packet, inputStream->time_base, outputStream->time_base);", remuxIndex, StringComparison.Ordinal);
        int offsetIndex = source.IndexOf("ApplyTimelineOffset(packet, outputStream, timelineOffset);", rescaleIndex, StringComparison.Ordinal);

        Assert.True(remuxIndex >= 0);
        Assert.True(normalizeIndex >= 0);
        Assert.True(rescaleIndex >= 0);
        Assert.True(normalizeIndex < rescaleIndex);
        Assert.True(offsetIndex > rescaleIndex);
        Assert.Contains("GetPacketDecodeEndTimestamp(packet, outputStream)", source);
    }

    [Fact]
    public void Remux_ClosesOutputAndChecksFlushBeforeReturningSuccess()
    {
        string source = ReadSource();
        int remuxIndex = source.IndexOf("private static FfmpegMediaRunResult Remux(", StringComparison.Ordinal);
        int closeIndex = source.IndexOf("CloseSegmentOutput(&outputContext, ref outputOpened, ref headerWritten)", remuxIndex, StringComparison.Ordinal);
        int successIndex = source.IndexOf("new FfmpegMediaRunResult(", closeIndex, StringComparison.Ordinal);

        Assert.True(remuxIndex >= 0);
        Assert.True(closeIndex > remuxIndex);
        Assert.True(successIndex > closeIndex);
        Assert.Contains("int closeResult = ffmpeg.avio_closep(&context->pb);", source);
        Assert.Contains("trailerResult = closeResult;", source);
    }

    [Fact]
    public void Remux_ReportsProcessedPacketTimelineDuration()
    {
        string source = ReadSource();

        Assert.Contains("double ProcessedDurationSeconds = 0d", source);
        Assert.Contains("timelineOffset / (double)ffmpeg.AV_TIME_BASE", source);
    }

    [Fact]
    public void OptimizedAudio_PadsPartialFifoFramesAndReportsBaseDuration()
    {
        string source = ReadOptimizedAudioSource();

        Assert.Contains("GetPaddedAudioFrameSampleCount(remaining, encoderContext->frame_size)", source);
        Assert.Contains("baseTimelineEnd = Math.Max(baseTimelineEnd, GetPacketDecodeEndTimestamp(basePacket, inputStream));", source);
        Assert.Contains("baseTimelineEnd / (double)ffmpeg.AV_TIME_BASE", source);
        Assert.Contains("IsFileInputFullyConsumed(inputContext, sourceFileName)", source);
        Assert.Contains("IsFileInputFullyConsumed(baseContext, baseVideoPath)", source);
    }

    [Fact]
    public void OptimizedAudio_ParallelPreparationCancelsSiblingBeforeFallback()
    {
        string source = ReadOptimizedAudioSource();
        int parallelIndex = source.IndexOf("private static OptimizedAudioPreparationResult RunParallelPreparation(", StringComparison.Ordinal);
        int firstIndex = source.IndexOf("Task.WhenAny(audioTask, baseTask)", parallelIndex, StringComparison.Ordinal);
        int cancelIndex = source.IndexOf("preparationCancellation.Cancel();", firstIndex, StringComparison.Ordinal);
        int awaitAudioIndex = source.IndexOf("audioTask.GetAwaiter().GetResult();", cancelIndex, StringComparison.Ordinal);
        int awaitBaseIndex = source.IndexOf("baseTask.GetAwaiter().GetResult();", awaitAudioIndex, StringComparison.Ordinal);

        Assert.True(parallelIndex >= 0);
        Assert.True(firstIndex > parallelIndex);
        Assert.True(cancelIndex > firstIndex);
        Assert.True(awaitAudioIndex > cancelIndex);
        Assert.True(awaitBaseIndex > awaitAudioIndex);
        Assert.Contains("if (preparation.Failure != null)", source);
        Assert.Contains("MuxAdditionalAudio(muxBasePath, optimizedAudioPath, targetFileName, metadata, token)", source);
    }

    [Fact]
    public void FileRemux_RequiresPhysicalInputEndBeforeSuccess()
    {
        string source = ReadSource();

        Assert.Contains("IsFileInputFullyConsumed(inputContext, sourceFileNames[sourceIndex])", source);
        Assert.Contains("input->error < 0", source);
        Assert.Contains("ffmpeg.avio_tell(input) >= inputSize", source);
    }

    [Fact]
    public void InputOptions_EnableReconnectOnlyForLiveSources()
    {
        string source = ReadSource().Replace("\r\n", "\n", StringComparison.Ordinal);
        int methodIndex = source.IndexOf("private static void AddInputOptions", StringComparison.Ordinal);
        int liveGuardIndex = source.IndexOf("if (inputOptions.IsLive)", methodIndex, StringComparison.Ordinal);
        int reconnectIndex = source.IndexOf("ffmpeg.av_dict_set(options, \"reconnect\", \"1\", 0);", methodIndex, StringComparison.Ordinal);
        int userAgentIndex = source.IndexOf("if (!string.IsNullOrWhiteSpace(inputOptions.UserAgent))", methodIndex, StringComparison.Ordinal);

        Assert.True(methodIndex >= 0);
        Assert.True(liveGuardIndex > methodIndex);
        Assert.True(reconnectIndex > liveGuardIndex);
        Assert.True(userAgentIndex > reconnectIndex);
    }

    [Fact]
    public void LaterSessionParts_RebuildAndValidateTheirStreamMap()
    {
        string source = ReadSource();

        Assert.Contains("streamMap = CreateCompatibleStreamMap(inputContext, streamSignatures);", source);
        Assert.Contains("input streams are incompatible with the first source", source);
        Assert.Contains("CreateStreamSignatures", source);
        Assert.Contains("BuildStreamSignature", source);
        Assert.Contains("streamSignatures.Order(StringComparer.Ordinal)", source);
    }

    [Fact]
    public void SegmentedRecording_RotatesNativeOutputsInsteadOfPassingPatternToOneMuxer()
    {
        string source = ReadSource();

        Assert.Contains("BuildSegmentPath(targetPattern, segmentIndex)", source);
        Assert.Contains("segmentClock.Observe(packet, inputStream)", source);
        Assert.Contains("ShouldRotateSegment(", source);
        Assert.Contains("CloseSegmentOutput(&outputContext", source);
        Assert.DoesNotContain("avformat_alloc_output_context2(&outputContext, null, \"segment\", targetPattern)", source);
    }

    [Fact]
    public void PacketNormalization_RepairsOnlyExtremeForwardTimelineGaps()
    {
        string source = ReadSource();
        string optimizedAudioSource = ReadOptimizedAudioSource();

        Assert.Contains("MaximumPacketForwardGapMicroseconds", source);
        Assert.Contains("gap < 0 || gap > maximumForwardGap", source);
        Assert.Contains("NormalizePacketDts(inputPacket, packetStream, packetStreamIndex, lastPacketEnds)", optimizedAudioSource);
        Assert.Contains("NormalizePacketDts(packet, inputStream, inputStreamIndex, lastPacketEnds)", optimizedAudioSource);
    }

    [Theory]
    [InlineData(105, 100, 10, 0)]
    [InlineData(110, 100, 10, 0)]
    [InlineData(111, 100, 10, -11)]
    [InlineData(90, 100, 10, 10)]
    public void PacketNormalization_ComputesDiscontinuityShift(
        long packetDts,
        long previousPacketEnd,
        long maximumForwardGap,
        long expectedShift)
    {
        Assert.Equal(
            expectedShift,
            FfmpegMediaEngine.GetPacketTimestampShift(packetDts, previousPacketEnd, maximumForwardGap));
    }

    [Theory]
    [InlineData(120, 100, 4, 30, 20, false)]
    [InlineData(100, 100, 4, 30, 4, false)]
    [InlineData(90, 100, 4, 30, 4, true)]
    [InlineData(131, 100, 4, 30, 4, true)]
    public void SharedTimeline_RepairsOnlyDiscontinuousReferenceClock(
        long sourceTimestamp,
        long previousTimestamp,
        long previousPacketDuration,
        long maximumForwardGap,
        long expectedDelta,
        bool expectedDiscontinuity)
    {
        long delta = FfmpegMediaEngine.GetSharedTimelineDelta(
            sourceTimestamp,
            previousTimestamp,
            previousPacketDuration,
            maximumForwardGap,
            out bool wasDiscontinuity);

        Assert.Equal(expectedDelta, delta);
        Assert.Equal(expectedDiscontinuity, wasDiscontinuity);
    }

    [Fact]
    public void OptimizedAudio_SingleSourceSkipsRedundantBaseRemux()
    {
        string source = ReadOptimizedAudioSource();

        Assert.Contains("string muxBasePath = sourceFileNames.Count == 1 ? sourceFileNames[0] : baseVideoPath;", source);
        Assert.Contains("if (sourceFileNames.Count > 1)", source);
    }

    [Fact]
    public void MediaTimelineRecovery_DoesNotAlterNormalInterleaving()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        FfmpegMediaEngine.MediaTimelineRecoveryResult video = recovery.Observe(true, false, 0, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult audio = recovery.Observe(false, true, 20_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult nextVideo = recovery.Observe(true, false, 40_000, 40_000, false);

        Assert.Equal(FfmpegTimelineEventKind.None, video.EventKind);
        Assert.Equal(FfmpegTimelineEventKind.None, audio.EventKind);
        Assert.False(nextVideo.DiscardPacket);
        Assert.Equal(0, nextVideo.PacketTimestampCorrection);
    }

    [Fact]
    public void RecordingStartup_DiscardsPacketsUntilFirstUsableVideoKeyframe()
    {
        bool awaitingInitialVideoKeyframe = true;

        Assert.True(FfmpegMediaEngine.ShouldDiscardBeforeInitialVideoKeyframe(
            false,
            false,
            false,
            ref awaitingInitialVideoKeyframe));
        Assert.True(FfmpegMediaEngine.ShouldDiscardBeforeInitialVideoKeyframe(
            true,
            false,
            false,
            ref awaitingInitialVideoKeyframe));
        Assert.True(FfmpegMediaEngine.ShouldDiscardBeforeInitialVideoKeyframe(
            true,
            true,
            true,
            ref awaitingInitialVideoKeyframe));
        Assert.False(FfmpegMediaEngine.ShouldDiscardBeforeInitialVideoKeyframe(
            true,
            true,
            false,
            ref awaitingInitialVideoKeyframe));
        Assert.False(awaitingInitialVideoKeyframe);
        Assert.False(FfmpegMediaEngine.ShouldDiscardBeforeInitialVideoKeyframe(
            false,
            false,
            false,
            ref awaitingInitialVideoKeyframe));
    }

    [Fact]
    public void RecordingStartup_DoesNotGateAudioOnlyInput()
    {
        bool awaitingInitialVideoKeyframe = false;

        Assert.False(FfmpegMediaEngine.ShouldDiscardBeforeInitialVideoKeyframe(
            false,
            false,
            false,
            ref awaitingInitialVideoKeyframe));
    }

    [Fact]
    public void RecordingStartup_UsesVideoKeyframeWhenAudioIsAlreadyAvailable()
    {
        FfmpegMediaEngine.InitialMediaTimelineSynchronizer synchronizer = new(true);

        synchronizer.ObserveAudioBeforeVideoKeyframe(900_000);
        bool synchronized = synchronizer.BeginAtVideoKeyframe(1_000_000, 40_000, 1024);

        Assert.True(synchronized);
        Assert.False(synchronizer.IsBuffering);
        Assert.Equal(1_000_000, synchronizer.SharedStartTimestamp);
        Assert.Equal(0, synchronizer.VideoTimestampCorrection);
    }

    [Fact]
    public void RecordingStartup_AlignsBufferedVideoWithAudioThatStartsLate()
    {
        FfmpegMediaEngine.InitialMediaTimelineSynchronizer synchronizer = new(true);

        Assert.False(synchronizer.BeginAtVideoKeyframe(1_000_000, 40_000, 1024));
        synchronizer.ObserveAudioBeforeVideoKeyframe(3_700_000);
        Assert.True(synchronizer.BeginAtVideoKeyframe(4_000_000, 40_000, 1024));

        Assert.False(synchronizer.IsBuffering);
        Assert.Equal(4_000_000, synchronizer.SharedStartTimestamp);
        Assert.Equal(0, synchronizer.VideoTimestampCorrection);
        Assert.Equal(3_000_000, synchronizer.AlignmentGapMicroseconds);
    }

    [Fact]
    public void RecordingStartup_ReleasesBoundedBufferWhenAudioPacketsAreMissing()
    {
        FfmpegMediaEngine.InitialMediaTimelineSynchronizer synchronizer = new(true);

        Assert.False(synchronizer.BeginAtVideoKeyframe(0, 40_000, 1024));
        Assert.True(synchronizer.ObserveBufferedPacket(
            true,
            false,
            FfmpegMediaEngine.InitialMediaSyncMaximumDurationMicroseconds - 40_000,
            40_000,
            1024));

        Assert.False(synchronizer.IsBuffering);
        Assert.Equal(0, synchronizer.SharedStartTimestamp);
        Assert.Equal(0, synchronizer.VideoTimestampCorrection);
    }

    [Fact]
    public void MediaTimelineRecovery_RecognizesAudioOnlyProgressAndRealignsAtKeyframe()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult stalled = recovery.Observe(false, true, 3_020_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult quarantinedAudio = recovery.Observe(false, true, 3_040_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult nonKeyframe = recovery.Observe(true, false, 40_000, 40_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recovered = recovery.Observe(true, false, 80_000, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult continued = recovery.Observe(true, false, 120_000, 40_000, false);

        Assert.Equal(FfmpegTimelineEventKind.VideoStalled, stalled.EventKind);
        Assert.Equal(3_000_000, stalled.GapMicroseconds);
        Assert.True(stalled.DiscardPacket);
        Assert.True(quarantinedAudio.DiscardPacket);
        Assert.True(nonKeyframe.DiscardPacket);
        Assert.False(recovered.DiscardPacket);
        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, recovered.EventKind);
        Assert.Equal(-40_000, recovered.PacketTimestampCorrection);
        Assert.Equal(recovered.PacketTimestampCorrection, continued.PacketTimestampCorrection);
    }

    [Fact]
    public void MediaTimelineRecovery_AlignsRecoveryToLastSafeWrittenTimestamp()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        _ = recovery.Observe(false, true, 3_020_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recovered = recovery.Observe(true, false, 3_100_000, 40_000, true);

        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, recovered.EventKind);
        Assert.Equal(40_000, 3_100_000 + recovered.PacketTimestampCorrection);
        Assert.Equal(0, recovered.GapMicroseconds);
    }

    [Fact]
    public void MediaTimelineRecovery_RebasesBothTracksAtSharedRecoveryAnchor()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        _ = recovery.Observe(false, true, 20_000, 20_000, false);
        _ = recovery.Observe(false, true, 3_020_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recovered = recovery.Observe(true, false, 80_000, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recoveredAudio = recovery.Observe(false, true, 53_000_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult continuedAudio = recovery.Observe(false, true, 53_020_000, 20_000, false);

        Assert.Equal(40_000, 80_000 + recovered.PacketTimestampCorrection);
        Assert.Equal(40_000, 53_000_000 + recoveredAudio.PacketTimestampCorrection);
        Assert.Equal(recoveredAudio.PacketTimestampCorrection, continuedAudio.PacketTimestampCorrection);
        Assert.False(recoveredAudio.DiscardPacket);
    }

    [Fact]
    public void MediaTimelineRecovery_PreservesSixtyFpsDurationAfterLongQuarantine()
    {
        const long videoFrameDuration = 16_667;
        const long audioFrameDuration = 21_333;
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        _ = recovery.Observe(true, false, 0, videoFrameDuration, true);
        _ = recovery.Observe(false, true, 0, audioFrameDuration, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult stalled = recovery.Observe(
            false,
            true,
            3_000_000,
            audioFrameDuration,
            false);
        _ = recovery.Observe(false, true, 53_000_000, audioFrameDuration, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recovered = recovery.Observe(
            true,
            false,
            videoFrameDuration,
            videoFrameDuration,
            true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recoveredAudio = recovery.Observe(
            false,
            true,
            53_000_000,
            audioFrameDuration,
            false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult finalVideo = default;
        for (int frameIndex = 2; frameIndex <= 3_601; frameIndex++)
        {
            finalVideo = recovery.Observe(
                true,
                false,
                videoFrameDuration * frameIndex,
                videoFrameDuration,
                false);
        }

        long recoveredVideoStart = videoFrameDuration + recovered.PacketTimestampCorrection;
        long recoveredAudioStart = 53_000_000 + recoveredAudio.PacketTimestampCorrection;
        long finalVideoStart = videoFrameDuration * 3_601 + finalVideo.PacketTimestampCorrection;

        Assert.Equal(FfmpegTimelineEventKind.VideoStalled, stalled.EventKind);
        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, recovered.EventKind);
        Assert.Equal(recoveredVideoStart, recoveredAudioStart);
        Assert.InRange(finalVideoStart - recoveredVideoStart, 60_000_000, 60_002_000);
    }

    [Fact]
    public void PacketNormalization_PersistsDerivedFrameDuration()
    {
        string source = ReadSource();

        Assert.Contains("GetPacketDuration(packet, inputStream)", source);
        Assert.Contains("long duration = GetPacketDuration(packet, stream);", source);
        Assert.Contains("packet->duration = duration;", source);
    }

    [Fact]
    public void MediaTimelineRecovery_HandlesRepeatedVideoStallsAndIgnoresAudioOnlyMedia()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);
        FfmpegMediaEngine.MediaTimelineRecovery audioOnlyRecovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        _ = recovery.Observe(false, true, 20_000, 20_000, false);
        _ = recovery.Observe(false, true, 3_020_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult firstRecovery = recovery.Observe(true, false, 80_000, 40_000, true);
        _ = recovery.Observe(false, true, 3_040_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult secondStall = recovery.Observe(false, true, 6_100_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult secondRecovery = recovery.Observe(true, false, 160_000, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult audioOnly = audioOnlyRecovery.Observe(false, true, 30_000_000, 20_000, false);

        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, firstRecovery.EventKind);
        Assert.Equal(FfmpegTimelineEventKind.VideoStalled, secondStall.EventKind);
        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, secondRecovery.EventKind);
        Assert.False(secondRecovery.DiscardPacket);
        Assert.Equal(FfmpegTimelineEventKind.None, audioOnly.EventKind);
    }

    [Fact]
    public void MediaTimelineRecovery_RecognizesVideoProgressWhileAudioIsStalled()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds,
            detectAudioStalls: true);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        _ = recovery.Observe(false, true, 0, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult stalled = recovery.Observe(
            true,
            false,
            3_000_000,
            40_000,
            false);

        Assert.Equal(FfmpegTimelineEventKind.AudioStalled, stalled.EventKind);
        Assert.Equal(3_020_000, stalled.GapMicroseconds);
    }

    [Fact]
    public void MediaTimelineRecovery_RestartsWhenAudioDoesNotReturnAfterVideoRecovery()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds,
            detectAudioStalls: true);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        _ = recovery.Observe(false, true, 20_000, 20_000, false);
        _ = recovery.Observe(false, true, 3_020_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recovered = recovery.Observe(true, false, 80_000, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult audioStalled = recovery.Observe(
            true,
            false,
            3_040_000,
            40_000,
            false);

        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, recovered.EventKind);
        Assert.Equal(FfmpegTimelineEventKind.AudioStalled, audioStalled.EventKind);
        Assert.Equal(3_000_000, audioStalled.GapMicroseconds);
    }

    [Fact]
    public void MediaTimelineRecovery_DoesNotTreatInitialAlignmentAsAudioStall()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds,
            detectAudioStalls: true,
            initialVideoTimestampCorrection: 2_700_000);

        _ = recovery.Observe(true, false, 1_000_000, 40_000, true);
        _ = recovery.Observe(true, false, 3_600_000, 40_000, false);
        _ = recovery.Observe(false, true, 3_700_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult continued = recovery.Observe(
            true,
            false,
            3_640_000,
            40_000,
            false);

        Assert.Equal(FfmpegTimelineEventKind.None, continued.EventKind);
    }

    [Fact]
    public void MediaTimelineRecovery_DoesNotReportAudioStallAfterAudioTimestampReset()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds,
            detectAudioStalls: true);

        _ = recovery.Observe(true, false, 10_000_000, 40_000, true);
        _ = recovery.Observe(false, true, 10_000_000, 20_000, false);
        _ = recovery.Observe(false, true, 0, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult video = recovery.Observe(
            true,
            false,
            12_000_000,
            40_000,
            false);

        Assert.Equal(FfmpegTimelineEventKind.None, video.EventKind);
    }

    [Fact]
    public void MediaTimelineRecovery_DoesNotReportVideoStallForRepeatedVideoTimestampsThatRemainWritable()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds,
            detectAudioStalls: true);

        _ = recovery.Observe(true, false, 0, 1_000_000, true);
        _ = recovery.Observe(false, true, 0, 1_000_000, false);
        _ = recovery.Observe(true, false, 0, 1_000_000, false);
        _ = recovery.Observe(false, true, 1_000_000, 1_000_000, false);
        _ = recovery.Observe(true, false, 0, 1_000_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult audio = recovery.Observe(
            false,
            true,
            2_000_000,
            1_000_000,
            false);

        Assert.Equal(FfmpegTimelineEventKind.None, audio.EventKind);
        Assert.False(audio.DiscardPacket);
    }

    [Fact]
    public void MediaTimelineRecovery_DoesNotAccumulatePacketDurationRoundingIntoPeriodicAudioStalls()
    {
        const long videoTimestampStep = 16_667;
        const long videoPacketDuration = 17_000;
        const long audioTimestampStep = 21_333;
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds,
            detectAudioStalls: true);
        long videoTimestamp = 0;
        long audioTimestamp = 0;

        while (videoTimestamp <= 151_000_000 || audioTimestamp <= 151_000_000)
        {
            FfmpegMediaEngine.MediaTimelineRecoveryResult result;
            if (videoTimestamp <= audioTimestamp && videoTimestamp <= 151_000_000)
            {
                result = recovery.Observe(true, false, videoTimestamp, videoPacketDuration, videoTimestamp == 0);
                videoTimestamp += videoTimestampStep;
            }
            else
            {
                result = recovery.Observe(false, true, audioTimestamp, audioTimestampStep, false);
                audioTimestamp += audioTimestampStep;
            }

            Assert.NotEqual(FfmpegTimelineEventKind.AudioStalled, result.EventKind);
            Assert.NotEqual(FfmpegTimelineEventKind.VideoStalled, result.EventKind);
        }
    }

    [Fact]
    public void Remux_UsesOneReferenceClockAndWaitsForVideoRecoveryKeyframe()
    {
        string source = ReadSource();
        string optimizedAudioSource = ReadOptimizedAudioSource();

        Assert.Contains("sourceClock.Observe(packet, inputStream)", source);
        Assert.Contains("sourceClock.CurrentCorrection", source);
        Assert.Contains("MediaTimelineRecovery timelineRecovery", source);
        Assert.Contains("FfmpegTimelineEventKind.VideoStalled", source);
        Assert.Contains("inputStreamIndex == referenceStreamIndex", source);
        Assert.Contains("segmentClock.LastObservationWasDiscontinuity", source);
        Assert.Contains("ApplyPacketTimestampCorrection(inputPacket, packetStream, sourceClock.CurrentCorrection)", optimizedAudioSource);
        Assert.Contains("packetStreamIndex == referenceStreamIndex", optimizedAudioSource);
    }

    [Fact]
    public void LiveRecording_ReportsTimelineStallsWithoutRestartingBeforeCrossStreamVerification()
    {
        string source = ReadSource();

        int segmentStart = source.IndexOf("private static FfmpegMediaRunResult SegmentStream(", StringComparison.Ordinal);
        int segmentReport = source.IndexOf("ReportTimelineEvent(timelineRecoveryResult, onPacketProgress);", segmentStart, StringComparison.Ordinal);
        int segmentDiscard = source.IndexOf("if (timelineRecoveryResult.DiscardPacket)", segmentReport, StringComparison.Ordinal);
        int segmentWrite = source.IndexOf("ffmpeg.av_interleaved_write_frame(outputContext, packet)", segmentDiscard, StringComparison.Ordinal);
        int remuxStart = source.IndexOf("private static FfmpegMediaRunResult Remux(", StringComparison.Ordinal);
        int remuxReport = source.IndexOf("ReportTimelineEvent(timelineRecoveryResult, onPacketProgress);", remuxStart, StringComparison.Ordinal);
        int remuxDiscard = source.IndexOf("if (timelineRecoveryResult.DiscardPacket)", remuxReport, StringComparison.Ordinal);
        int remuxWrite = source.IndexOf("ffmpeg.av_interleaved_write_frame(outputContext, packet)", remuxDiscard, StringComparison.Ordinal);

        Assert.True(segmentReport > segmentStart);
        Assert.True(segmentDiscard > segmentReport);
        Assert.True(segmentWrite > segmentDiscard);
        Assert.True(remuxReport > remuxStart);
        Assert.True(remuxDiscard > remuxReport);
        Assert.True(remuxWrite > remuxDiscard);
        Assert.Contains("FfmpegTimelineEventKind.AudioStalled", source);
        Assert.Contains("if (inputOptions.IsLive)", source);
        Assert.Contains("if (inputOptions?.IsLive == true)", source);
        Assert.DoesNotContain("return CreateLiveTimelineRestartResult", source);
    }

    [Fact]
    public void LiveRecording_UsesFirstVideoKeyframeAsSharedTimelineOrigin()
    {
        string source = ReadSource();

        int segmentStart = source.IndexOf("private static FfmpegMediaRunResult SegmentStream(", StringComparison.Ordinal);
        int segmentGate = source.IndexOf("ShouldDiscardBeforeInitialVideoKeyframe(", segmentStart, StringComparison.Ordinal);
        int segmentBase = source.IndexOf("segmentOutputTimestampBase = GetPacketTimelineTimestampMicroseconds(", segmentGate, StringComparison.Ordinal);
        int segmentRecovery = source.IndexOf("MediaTimelineRecoveryResult timelineRecoveryResult = timelineRecovery.Observe(", segmentBase, StringComparison.Ordinal);
        int remuxStart = source.IndexOf("private static FfmpegMediaRunResult Remux(", StringComparison.Ordinal);
        int remuxGate = source.IndexOf("ShouldDiscardBeforeInitialVideoKeyframe(", remuxStart, StringComparison.Ordinal);
        int remuxBase = source.IndexOf("sourceTimestampBase = GetPacketTimelineTimestampMicroseconds(", remuxGate, StringComparison.Ordinal);
        int remuxRecovery = source.IndexOf("MediaTimelineRecoveryResult timelineRecoveryResult = timelineRecovery.Observe(", remuxBase, StringComparison.Ordinal);

        Assert.True(segmentGate > segmentStart);
        Assert.True(segmentBase > segmentGate);
        Assert.True(segmentRecovery > segmentBase);
        Assert.True(remuxGate > remuxStart);
        Assert.True(remuxBase > remuxGate);
        Assert.True(remuxRecovery > remuxBase);
        Assert.Contains("bool awaitingInitialVideoKeyframe = inputOptions.IsLive", source);
        Assert.Contains("bool awaitingInitialVideoKeyframe = inputOptions?.IsLive == true", source);
    }

    [Fact]
    public void Remux_UsesEveryMappedStreamDecodeEndAndRejectsEmptySources()
    {
        string source = ReadSource();

        Assert.Contains("sourceDecodeEndTimestamp = Math.Max(", source);
        Assert.Contains("GetPacketDecodeEndTimestamp(packet, outputStream)", source);
        Assert.Contains("if (!sourceHadReferenceProgress)", source);
        Assert.Contains("contains no readable media packets", source);
        Assert.DoesNotContain("referenceStreamEndTimestamp", source);
    }

    [Fact]
    public void NativeInputFailures_ReportCancellationConsistently()
    {
        string source = ReadSource();

        Assert.Contains("CreateNativeFailureResult(openResult, token", source);
        Assert.Contains("CreateNativeFailureResult(streamInfoResult, token", source);
        Assert.Contains("CreateNativeFailureResult(readResult, token", source);
        Assert.Contains("new FfmpegMediaRunResult(255, true, hadProgress, string.Empty)", source);
    }

    [Fact]
    public void OutputStreams_PreserveTrackRoleMetadataAndFrameRate()
    {
        string source = ReadSource();

        Assert.Contains("outputStream->disposition = inputStream->disposition;", source);
        Assert.Contains("outputStream->avg_frame_rate = inputStream->avg_frame_rate;", source);
        Assert.Contains("outputStream->r_frame_rate = inputStream->r_frame_rate;", source);
        Assert.Contains("ffmpeg.av_dict_copy(&outputStream->metadata, inputStream->metadata, 0)", source);
        Assert.Contains("AV_DISPOSITION_ATTACHED_PIC", source);
        Assert.Contains("ReadMetadataValue(stream->metadata, \"language\")", source);
        Assert.Contains("ReadMetadataValue(stream->metadata, \"title\")", source);
    }

    private static string ReadSource()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "Emerde", "Core", "FfmpegMediaEngine.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("FfmpegMediaEngine.cs");
    }

    private static string ReadOptimizedAudioSource()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "Emerde", "Core", "FfmpegOptimizedAudio.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("FfmpegOptimizedAudio.cs");
    }
}
