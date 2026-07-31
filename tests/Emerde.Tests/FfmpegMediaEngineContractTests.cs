namespace Emerde.Tests;

public sealed class FfmpegMediaEngineContractTests
{
    [Fact]
    public void Remux_PreservesSharedSourceTimelineBeforeApplyingSessionOffset()
    {
        string source = ReadSource();
        int remuxIndex = source.IndexOf("private static FfmpegMediaRunResult Remux(", StringComparison.Ordinal);
        int normalizeIndex = source.IndexOf("NormalizeSourcePacketTimestamps(packet, inputStream, sourceTimestampBase);", remuxIndex, StringComparison.Ordinal);
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
    public void PacketNormalization_PreservesForwardTimelineGaps()
    {
        string source = ReadSource();

        Assert.Contains("packet->dts < previousPacketEnd", source);
        Assert.DoesNotContain("maximumForwardGap", source);
        Assert.DoesNotContain("packet->dts - previousPacketEnd >", source);
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
