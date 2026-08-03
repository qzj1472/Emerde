using Emerde.Core;

namespace Emerde.Tests;

public sealed class FfmpegMediaEngineContractTests
{
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
        Assert.Equal(0, nextVideo.VideoTimestampCorrection);
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
    public void MediaTimelineRecovery_RecognizesAudioOnlyProgressAndRealignsAtKeyframe()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult stalled = recovery.Observe(false, true, 3_020_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult nonKeyframe = recovery.Observe(true, false, 40_000, 40_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recovered = recovery.Observe(true, false, 80_000, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult continued = recovery.Observe(true, false, 120_000, 40_000, false);

        Assert.Equal(FfmpegTimelineEventKind.VideoStalled, stalled.EventKind);
        Assert.Equal(3_000_000, stalled.GapMicroseconds);
        Assert.True(nonKeyframe.DiscardPacket);
        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, recovered.EventKind);
        Assert.Equal(2_960_000, recovered.VideoTimestampCorrection);
        Assert.Equal(recovered.VideoTimestampCorrection, continued.VideoTimestampCorrection);
    }

    [Fact]
    public void MediaTimelineRecovery_DoesNotShiftVideoThatResumesOnTheAudioTimeline()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        _ = recovery.Observe(false, true, 3_020_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recovered = recovery.Observe(true, false, 3_100_000, 40_000, true);

        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, recovered.EventKind);
        Assert.Equal(0, recovered.VideoTimestampCorrection);
        Assert.Equal(0, recovered.GapMicroseconds);
    }

    [Fact]
    public void MediaTimelineRecovery_RemovesPriorCorrectionWhenSourceVideoCatchesUp()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        _ = recovery.Observe(false, true, 3_020_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult recovered = recovery.Observe(true, false, 80_000, 40_000, true);
        _ = recovery.Observe(false, true, 6_000_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult caughtUp = recovery.Observe(true, false, 6_100_000, 40_000, true);

        Assert.True(recovered.VideoTimestampCorrection > 0);
        Assert.Equal(0, caughtUp.VideoTimestampCorrection);
        Assert.False(caughtUp.DiscardPacket);
    }

    [Fact]
    public void MediaTimelineRecovery_HandlesRepeatedVideoStallsAndIgnoresAudioOnlyMedia()
    {
        FfmpegMediaEngine.MediaTimelineRecovery recovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);
        FfmpegMediaEngine.MediaTimelineRecovery audioOnlyRecovery = new(
            FfmpegMediaEngine.VideoTimelineStallThresholdMicroseconds);

        _ = recovery.Observe(true, false, 0, 40_000, true);
        _ = recovery.Observe(false, true, 3_020_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult firstRecovery = recovery.Observe(true, false, 80_000, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult secondStall = recovery.Observe(false, true, 6_080_000, 20_000, false);
        FfmpegMediaEngine.MediaTimelineRecoveryResult secondRecovery = recovery.Observe(true, false, 120_000, 40_000, true);
        FfmpegMediaEngine.MediaTimelineRecoveryResult audioOnly = audioOnlyRecovery.Observe(false, true, 30_000_000, 20_000, false);

        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, firstRecovery.EventKind);
        Assert.Equal(FfmpegTimelineEventKind.VideoStalled, secondStall.EventKind);
        Assert.Equal(FfmpegTimelineEventKind.VideoRecovered, secondRecovery.EventKind);
        Assert.True(secondRecovery.VideoTimestampCorrection > firstRecovery.VideoTimestampCorrection);
        Assert.Equal(FfmpegTimelineEventKind.None, audioOnly.EventKind);
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
    public void LiveRecording_RestartsBeforeWritingConfirmedAudioOnlyTimeline()
    {
        string source = ReadSource();

        int segmentStart = source.IndexOf("private static FfmpegMediaRunResult SegmentStream(", StringComparison.Ordinal);
        int segmentRestart = source.IndexOf("CreateLiveTimelineRestartResult(hadProgress, timelineRecoveryResult, onPacketProgress)", segmentStart, StringComparison.Ordinal);
        int segmentWrite = source.IndexOf("ffmpeg.av_interleaved_write_frame(outputContext, packet)", segmentRestart, StringComparison.Ordinal);
        int remuxStart = source.IndexOf("private static FfmpegMediaRunResult Remux(", StringComparison.Ordinal);
        int remuxRestart = source.IndexOf("CreateLiveTimelineRestartResult(hadProgress, timelineRecoveryResult, onPacketProgress)", remuxStart, StringComparison.Ordinal);
        int remuxWrite = source.IndexOf("ffmpeg.av_interleaved_write_frame(outputContext, packet)", remuxRestart, StringComparison.Ordinal);

        Assert.True(segmentRestart > segmentStart);
        Assert.True(segmentWrite > segmentRestart);
        Assert.True(remuxRestart > remuxStart);
        Assert.True(remuxWrite > remuxRestart);
        Assert.Contains("if (inputOptions.IsLive)", source);
        Assert.Contains("if (inputOptions?.IsLive == true)", source);
        Assert.Contains("RequiresInputRestart: true", source);
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
