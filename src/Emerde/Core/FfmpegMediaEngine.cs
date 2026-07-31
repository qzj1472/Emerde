using FFmpeg.AutoGen;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Emerde.Core;

internal sealed record FfmpegInputOptions(
    string UserAgent,
    string Headers,
    bool IsUseProxy,
    string HttpProxy,
    bool IsLive);

internal sealed record FfmpegSegmentOptions(long Value, int Unit)
{
    public bool IsSizeBased => SegmentTimeUnitHelper.IsSizeUnit(Unit);
}

internal sealed record FfmpegMediaProbeResult(
    bool HasAudio,
    bool HasVideo,
    int AudioStreamCount,
    int VideoStreamCount,
    int Width,
    int Height,
    double DurationSeconds,
    long Bitrate,
    string StreamSignature,
    VideoRecordingMetadata Metadata);

internal sealed record FfmpegMediaRunResult(
    int ExitCode,
    bool WasCanceled,
    bool HadMediaProgress,
    string ErrorOutput,
    double ProcessedDurationSeconds = 0d);

internal readonly record struct FfmpegPacketProgress(int Bytes, bool IsVideo, bool IsAudio);

internal static unsafe partial class FfmpegMediaEngine
{
    private const int InputFormatFlags = ffmpeg.AVFMT_FLAG_GENPTS
        | ffmpeg.AVFMT_FLAG_DISCARD_CORRUPT
        | ffmpeg.AVFMT_FLAG_SORT_DTS;
    private const int InputErrorRecognitionFlags = ffmpeg.AV_EF_IGNORE_ERR;
    private static readonly object InitializeLock = new();
    private static bool initialized;

    public static string LibraryDirectory => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    public static bool IsAvailable => Directory.Exists(LibraryDirectory)
        && File.Exists(Path.Combine(LibraryDirectory, "avformat-61.dll"))
        && File.Exists(Path.Combine(LibraryDirectory, "avcodec-61.dll"))
        && File.Exists(Path.Combine(LibraryDirectory, "avutil-59.dll"))
        && File.Exists(Path.Combine(LibraryDirectory, "swresample-5.dll"))
        && File.Exists(Path.Combine(LibraryDirectory, "libwinpthread-1.dll"));

    public static bool HasAacEncoder
    {
        get
        {
            try
            {
                EnsureInitialized();
                return ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC) != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool HasRequiredRuntimeCapabilities
    {
        get
        {
            try
            {
                EnsureInitialized();
                string[] decoders = ["aac", "av1", "h264", "hevc", "mp3", "opus", "vorbis", "vp8", "vp9", "mjpeg"];
                string[] demuxers = ["aac", "avi", "concat", "flv", "hls", "live_flv", "matroska", "mov", "mp3", "mpegts"];
                string[] muxers = ["flv", "matroska", "mp4", "mpegts", "null", "segment"];
                return decoders.All(name => ffmpeg.avcodec_find_decoder_by_name(name) != null)
                    && demuxers.All(name => ffmpeg.av_find_input_format(name) != null)
                    && muxers.All(name => ffmpeg.av_guess_format(name, null, null) != null)
                    && HasAacEncoder;
            }
            catch
            {
                return false;
            }
        }
    }

    public static FfmpegMediaRunResult RemuxFiles(
        IReadOnlyList<string> sourceFileNames,
        string targetFileName,
        VideoRecordingMetadata metadata,
        CancellationToken token,
        Action<long>? onProgress = null)
    {
        return Remux(sourceFileNames, targetFileName, metadata, null, token, onProgress);
    }

    public static FfmpegMediaRunResult RecordStream(
        string inputUrl,
        string targetFileName,
        VideoRecordingMetadata metadata,
        FfmpegInputOptions options,
        FfmpegSegmentOptions? segmentOptions,
        CancellationToken token,
        Action<long>? onProgress = null,
        Action<FfmpegPacketProgress>? onPacketProgress = null,
        Action<bool, bool>? onStreamsDiscovered = null)
    {
        return segmentOptions == null
            ? Remux([inputUrl], targetFileName, metadata, options, token, onProgress, onPacketProgress, onStreamsDiscovered)
            : SegmentStream(inputUrl, targetFileName, metadata, options, segmentOptions, token, onProgress, onPacketProgress, onStreamsDiscovered);
    }

    public static FfmpegMediaRunResult SplitFile(
        string sourceFileName,
        string targetPattern,
        int segmentSeconds,
        VideoRecordingMetadata metadata,
        CancellationToken token,
        Action<long>? onProgress = null)
    {
        return SegmentStream(
            sourceFileName,
            targetPattern,
            metadata,
            new FfmpegInputOptions(string.Empty, string.Empty, false, string.Empty, false),
            new FfmpegSegmentOptions(segmentSeconds, SegmentTimeUnitHelper.Seconds),
            token,
            onProgress,
            null,
            null);
    }

    private static FfmpegMediaRunResult SegmentStream(
        string sourceFileName,
        string targetPattern,
        VideoRecordingMetadata metadata,
        FfmpegInputOptions inputOptions,
        FfmpegSegmentOptions segmentOptions,
        CancellationToken token,
        Action<long>? onProgress,
        Action<FfmpegPacketProgress>? onPacketProgress,
        Action<bool, bool>? onStreamsDiscovered)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName)
            || string.IsNullOrWhiteSpace(targetPattern)
            || !targetPattern.Contains("%03d", StringComparison.Ordinal)
            || segmentOptions.Value <= 0)
        {
            return new FfmpegMediaRunResult(1, false, false, "input, output pattern, or segment threshold is invalid");
        }

        AVFormatContext* inputContext = null;
        AVFormatContext* outputContext = null;
        AVDictionary* options = null;
        AVPacket* packet = null;
        GCHandle interruptHandle = default;
        bool outputOpened = false;
        bool headerWritten = false;
        bool hadProgress = false;

        try
        {
            EnsureInitialized();
            ConfigureInterruptCallback(&inputContext, token, out interruptHandle);
            AddInputOptions(&options, inputOptions);
            int openResult = ffmpeg.avformat_open_input(&inputContext, sourceFileName, null, &options);
            if (openResult < 0)
            {
                return CreateNativeFailureResult(openResult, token, false);
            }
            ApplyInputRepairPolicy(inputContext);

            int streamInfoResult = ffmpeg.avformat_find_stream_info(inputContext, null);
            if (streamInfoResult < 0)
            {
                return CreateNativeFailureResult(streamInfoResult, token, false);
            }

            int referenceStreamIndex = GetSegmentReferenceStreamIndex(inputContext);
            if (referenceStreamIndex < 0)
            {
                return new FfmpegMediaRunResult(1, false, false, "input contains no supported audio or video streams");
            }
            NotifyStreamPresence(inputContext, onStreamsDiscovered);

            int segmentIndex = 0;
            int[] streamMap = OpenSegmentOutput(
                inputContext,
                BuildSegmentPath(targetPattern, segmentIndex),
                metadata,
                &outputContext,
                out outputOpened,
                out headerWritten);
            long[] lastPacketEnds = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)outputContext->nb_streams).ToArray();
            long[] nextInputDts = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)inputContext->nb_streams).ToArray();
            SegmentClock segmentClock = new();
            long segmentClockStartTimestamp = ffmpeg.AV_NOPTS_VALUE;
            long segmentOutputTimestampBase = inputContext->start_time;
            long segmentPayloadBytes = 0;
            bool segmentHasPackets = false;

            packet = ffmpeg.av_packet_alloc();
            if (packet == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "packet allocation failed");
            }

            while (!token.IsCancellationRequested)
            {
                int readResult = ffmpeg.av_read_frame(inputContext, packet);
                if (readResult < 0)
                {
                    if (readResult == ffmpeg.AVERROR_EOF)
                    {
                        break;
                    }

                    return CreateNativeFailureResult(readResult, token, hadProgress);
                }

                int inputStreamIndex = packet->stream_index;
                if (inputStreamIndex < 0 || inputStreamIndex >= streamMap.Length || streamMap[inputStreamIndex] < 0)
                {
                    ffmpeg.av_packet_unref(packet);
                    continue;
                }

                AVStream* inputStream = inputContext->streams[inputStreamIndex];
                AVMediaType mediaType = inputStream->codecpar->codec_type;
                EnsurePacketDts(packet, inputStream, inputStreamIndex, nextInputDts, segmentOutputTimestampBase);
                long packetSourceTimestamp = GetPacketTimestamp(packet, inputStream);
                long packetClockTimestamp = inputStreamIndex == referenceStreamIndex
                    ? segmentClock.Observe(packet, inputStream)
                    : ffmpeg.AV_NOPTS_VALUE;
                if (segmentOutputTimestampBase == ffmpeg.AV_NOPTS_VALUE && packetSourceTimestamp != ffmpeg.AV_NOPTS_VALUE)
                {
                    segmentOutputTimestampBase = AddSaturated(packetSourceTimestamp, segmentClock.CurrentCorrection);
                }
                if (segmentClockStartTimestamp == ffmpeg.AV_NOPTS_VALUE && packetClockTimestamp != ffmpeg.AV_NOPTS_VALUE)
                {
                    segmentClockStartTimestamp = packetClockTimestamp;
                }

                if (segmentHasPackets
                    && ShouldRotateSegment(
                        packet,
                        inputContext,
                        referenceStreamIndex,
                        packetClockTimestamp,
                        segmentClockStartTimestamp,
                        segmentPayloadBytes,
                        segmentOptions))
                {
                    int closeResult = CloseSegmentOutput(&outputContext, ref outputOpened, ref headerWritten);
                    if (closeResult < 0)
                    {
                        ffmpeg.av_packet_unref(packet);
                        return new FfmpegMediaRunResult(closeResult, false, hadProgress, ErrorToString(closeResult));
                    }

                    segmentIndex++;
                    streamMap = OpenSegmentOutput(
                        inputContext,
                        BuildSegmentPath(targetPattern, segmentIndex),
                        metadata,
                        &outputContext,
                        out outputOpened,
                        out headerWritten);
                    lastPacketEnds = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)outputContext->nb_streams).ToArray();
                    segmentClockStartTimestamp = packetClockTimestamp;
                    segmentOutputTimestampBase = packetSourceTimestamp == ffmpeg.AV_NOPTS_VALUE
                        ? ffmpeg.AV_NOPTS_VALUE
                        : AddSaturated(packetSourceTimestamp, segmentClock.CurrentCorrection);
                    segmentPayloadBytes = 0;
                    segmentHasPackets = false;
                }

                int outputStreamIndex = streamMap[inputStreamIndex];
                AVStream* outputStream = outputContext->streams[outputStreamIndex];
                int packetSize = Math.Max(0, packet->size);
                NormalizeSegmentPacketTimestamps(
                    packet,
                    inputStream,
                    segmentOutputTimestampBase,
                    segmentClock.CurrentCorrection);
                ffmpeg.av_packet_rescale_ts(packet, inputStream->time_base, outputStream->time_base);
                NormalizePacketDts(packet, outputStreamIndex, lastPacketEnds);
                packet->stream_index = outputStreamIndex;
                packet->pos = -1;

                int writeResult = ffmpeg.av_interleaved_write_frame(outputContext, packet);
                ffmpeg.av_packet_unref(packet);
                if (writeResult < 0)
                {
                    return CreateNativeFailureResult(writeResult, token, hadProgress);
                }

                segmentPayloadBytes += packetSize;
                segmentHasPackets = true;
                hadProgress = true;
                onProgress?.Invoke(packetSize);
                onPacketProgress?.Invoke(new FfmpegPacketProgress(
                    packetSize,
                    mediaType == AVMediaType.AVMEDIA_TYPE_VIDEO,
                    mediaType == AVMediaType.AVMEDIA_TYPE_AUDIO));
            }

            if (token.IsCancellationRequested)
            {
                return new FfmpegMediaRunResult(255, true, hadProgress, string.Empty);
            }

            int finalResult = CloseSegmentOutput(&outputContext, ref outputOpened, ref headerWritten);
            return finalResult < 0
                ? new FfmpegMediaRunResult(finalResult, false, hadProgress, ErrorToString(finalResult))
                : new FfmpegMediaRunResult(0, false, hadProgress, string.Empty);
        }
        catch (Exception e)
        {
            return token.IsCancellationRequested
                ? CreateCanceledResult(hadProgress)
                : new FfmpegMediaRunResult(1, false, hadProgress, e.ToString());
        }
        finally
        {
            if (packet != null)
            {
                AVPacket* packetPointer = packet;
                ffmpeg.av_packet_free(&packetPointer);
            }

            _ = CloseSegmentOutput(&outputContext, ref outputOpened, ref headerWritten);

            if (inputContext != null)
            {
                AVFormatContext* context = inputContext;
                ffmpeg.avformat_close_input(&context);
            }

            if (options != null)
            {
                ffmpeg.av_dict_free(&options);
            }

            if (interruptHandle.IsAllocated)
            {
                interruptHandle.Free();
            }
        }
    }

    private static int GetSegmentReferenceStreamIndex(AVFormatContext* inputContext)
    {
        int audioStreamIndex = -1;
        for (int index = 0; index < inputContext->nb_streams; index++)
        {
            AVMediaType mediaType = inputContext->streams[index]->codecpar->codec_type;
            if (mediaType == AVMediaType.AVMEDIA_TYPE_VIDEO
                && (inputContext->streams[index]->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
            {
                return index;
            }

            if (mediaType == AVMediaType.AVMEDIA_TYPE_AUDIO && audioStreamIndex < 0)
            {
                audioStreamIndex = index;
            }
        }

        return audioStreamIndex;
    }

    private static string BuildSegmentPath(string targetPattern, int segmentIndex)
    {
        return targetPattern.Replace(
            "%03d",
            segmentIndex.ToString("000", CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private static int[] OpenSegmentOutput(
        AVFormatContext* inputContext,
        string targetFileName,
        VideoRecordingMetadata metadata,
        AVFormatContext** outputContext,
        out bool outputOpened,
        out bool headerWritten)
    {
        AVFormatContext* context = null;
        outputOpened = false;
        headerWritten = false;

        try
        {
            ThrowIfError(ffmpeg.avformat_alloc_output_context2(&context, null, GetOutputFormatName(targetFileName), targetFileName), "create segment output");
            if (context == null)
            {
                throw new InvalidOperationException("segment output context could not be created");
            }

            AddMetadata(context, metadata);
            context->avoid_negative_ts = ffmpeg.AVFMT_AVOID_NEG_TS_MAKE_NON_NEGATIVE;
            int[] streamMap = CreateOutputStreams(inputContext, context);
            if (!streamMap.Any(index => index >= 0))
            {
                throw new InvalidOperationException("input contains no supported audio or video streams");
            }

            if ((context->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                ThrowIfError(ffmpeg.avio_open(&context->pb, targetFileName, ffmpeg.AVIO_FLAG_WRITE), "open segment output");
                outputOpened = true;
            }

            AVDictionary* writeOptions = null;
            try
            {
                if (VideoRecordingMetadataStore.UsesMovMetadataTags(targetFileName))
                {
                    ffmpeg.av_dict_set(&writeOptions, "movflags", "use_metadata_tags", 0);
                }

                ThrowIfError(ffmpeg.avformat_write_header(context, &writeOptions), "write segment header");
                headerWritten = true;
            }
            finally
            {
                if (writeOptions != null)
                {
                    ffmpeg.av_dict_free(&writeOptions);
                }
            }

            *outputContext = context;
            return streamMap;
        }
        catch
        {
            _ = CloseSegmentOutput(&context, ref outputOpened, ref headerWritten);
            throw;
        }
    }

    private static long GetPacketTimestamp(AVPacket* packet, AVStream* inputStream)
    {
        long timestamp = packet->dts != ffmpeg.AV_NOPTS_VALUE ? packet->dts : packet->pts;
        return timestamp == ffmpeg.AV_NOPTS_VALUE
            ? ffmpeg.AV_NOPTS_VALUE
            : ffmpeg.av_rescale_q(
                timestamp,
                inputStream->time_base,
                new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE });
    }

    private static void EnsurePacketDts(
        AVPacket* packet,
        AVStream* inputStream,
        int inputStreamIndex,
        long[] nextInputDts,
        long sourceTimestampBase)
    {
        if (packet->dts == ffmpeg.AV_NOPTS_VALUE)
        {
            long nextDts = nextInputDts[inputStreamIndex];
            if (nextDts == ffmpeg.AV_NOPTS_VALUE
                && inputStream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO
                && packet->pts != ffmpeg.AV_NOPTS_VALUE)
            {
                nextDts = packet->pts;
            }
            if (nextDts == ffmpeg.AV_NOPTS_VALUE && sourceTimestampBase != ffmpeg.AV_NOPTS_VALUE)
            {
                nextDts = ffmpeg.av_rescale_q(
                    sourceTimestampBase,
                    new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
                    inputStream->time_base);
            }
            if (nextDts == ffmpeg.AV_NOPTS_VALUE)
            {
                nextDts = packet->pts;
            }
            packet->dts = nextDts;
        }

        if (packet->dts != ffmpeg.AV_NOPTS_VALUE)
        {
            long duration = GetPacketDuration(packet, inputStream);
            nextInputDts[inputStreamIndex] = packet->dts > long.MaxValue - duration
                ? long.MaxValue
                : packet->dts + duration;
        }
    }

    private static long GetPacketDuration(AVPacket* packet, AVStream* stream)
    {
        if (packet->duration > 0)
        {
            return packet->duration;
        }

        AVCodecParameters* parameters = stream->codecpar;
        if (parameters->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
        {
            AVRational frameRate = stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0
                ? stream->avg_frame_rate
                : stream->r_frame_rate;
            if (frameRate.num > 0 && frameRate.den > 0)
            {
                return Math.Max(1, ffmpeg.av_rescale_q(
                    1,
                    new AVRational { num = frameRate.den, den = frameRate.num },
                    stream->time_base));
            }
        }
        else if (parameters->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO
            && parameters->frame_size > 0
            && parameters->sample_rate > 0)
        {
            return Math.Max(1, ffmpeg.av_rescale_q(
                1,
                new AVRational { num = parameters->frame_size, den = parameters->sample_rate },
                stream->time_base));
        }

        return 1;
    }

    private sealed class SegmentClock
    {
        private long lastSourceTimestamp = ffmpeg.AV_NOPTS_VALUE;
        private long firstSourceTimestamp = ffmpeg.AV_NOPTS_VALUE;
        private long lastPacketDuration = 1;
        private long monotonicTimestamp;

        public long CurrentCorrection { get; private set; }

        public long Observe(AVPacket* packet, AVStream* inputStream)
        {
            long sourceTimestamp = packet->dts != ffmpeg.AV_NOPTS_VALUE ? packet->dts : packet->pts;
            if (sourceTimestamp == ffmpeg.AV_NOPTS_VALUE)
            {
                return lastSourceTimestamp == ffmpeg.AV_NOPTS_VALUE ? ffmpeg.AV_NOPTS_VALUE : monotonicTimestamp;
            }

            long sourceTimestampMicroseconds = ffmpeg.av_rescale_q(
                sourceTimestamp,
                inputStream->time_base,
                new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE });
            long packetDuration = packet->duration > 0
                ? Math.Max(1, ffmpeg.av_rescale_q(
                    packet->duration,
                    inputStream->time_base,
                    new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }))
                : lastPacketDuration;

            if (lastSourceTimestamp == ffmpeg.AV_NOPTS_VALUE)
            {
                firstSourceTimestamp = sourceTimestampMicroseconds;
                lastSourceTimestamp = sourceTimestampMicroseconds;
                lastPacketDuration = packetDuration;
                return monotonicTimestamp;
            }

            long delta = sourceTimestampMicroseconds - lastSourceTimestamp;
            if (delta <= 0 || delta > 10L * ffmpeg.AV_TIME_BASE)
            {
                delta = lastPacketDuration;
            }

            monotonicTimestamp = monotonicTimestamp > long.MaxValue - delta
                ? long.MaxValue
                : monotonicTimestamp + delta;
            long correctedTimestamp = AddSaturated(firstSourceTimestamp, monotonicTimestamp);
            CurrentCorrection = SubtractSaturated(correctedTimestamp, sourceTimestampMicroseconds);
            lastSourceTimestamp = sourceTimestampMicroseconds;
            lastPacketDuration = packetDuration;
            return monotonicTimestamp;
        }
    }

    private static bool ShouldRotateSegment(
        AVPacket* packet,
        AVFormatContext* inputContext,
        int referenceStreamIndex,
        long packetTimestamp,
        long segmentStartTimestamp,
        long segmentPayloadBytes,
        FfmpegSegmentOptions segmentOptions)
    {
        if (packet->stream_index != referenceStreamIndex)
        {
            return false;
        }

        AVMediaType referenceType = inputContext->streams[referenceStreamIndex]->codecpar->codec_type;
        if (referenceType == AVMediaType.AVMEDIA_TYPE_VIDEO && (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) == 0)
        {
            return false;
        }

        if (segmentOptions.IsSizeBased)
        {
            return segmentPayloadBytes >= segmentOptions.Value;
        }

        if (packetTimestamp == ffmpeg.AV_NOPTS_VALUE || segmentStartTimestamp == ffmpeg.AV_NOPTS_VALUE)
        {
            return false;
        }

        long multiplier = segmentOptions.Unit == SegmentTimeUnitHelper.Milliseconds
            ? ffmpeg.AV_TIME_BASE / 1000
            : ffmpeg.AV_TIME_BASE;
        long threshold = segmentOptions.Value > long.MaxValue / multiplier
            ? long.MaxValue
            : segmentOptions.Value * multiplier;
        return packetTimestamp - segmentStartTimestamp >= threshold;
    }

    private static void NormalizeSegmentPacketTimestamps(
        AVPacket* packet,
        AVStream* inputStream,
        long segmentStartTimestamp,
        long timestampCorrection)
    {
        if (segmentStartTimestamp == ffmpeg.AV_NOPTS_VALUE)
        {
            return;
        }

        long timestampOffset = ffmpeg.av_rescale_q(
            segmentStartTimestamp,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
            inputStream->time_base);
        long inputCorrection = ffmpeg.av_rescale_q(
            timestampCorrection,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
            inputStream->time_base);
        if (packet->pts != ffmpeg.AV_NOPTS_VALUE)
        {
            packet->pts = AddSaturated(packet->pts, inputCorrection) - timestampOffset;
        }

        if (packet->dts != ffmpeg.AV_NOPTS_VALUE)
        {
            packet->dts = AddSaturated(packet->dts, inputCorrection) - timestampOffset;
        }
    }

    private static void NormalizePacketDts(
        AVPacket* packet,
        int outputStreamIndex,
        long[] lastPacketEnds)
    {
        if (packet->dts == ffmpeg.AV_NOPTS_VALUE)
        {
            return;
        }

        long previousPacketEnd = lastPacketEnds[outputStreamIndex];
        if (previousPacketEnd != ffmpeg.AV_NOPTS_VALUE && packet->dts < previousPacketEnd)
        {
            long shift = packet->dts - previousPacketEnd;
            packet->dts -= shift;
            if (packet->pts != ffmpeg.AV_NOPTS_VALUE)
            {
                packet->pts -= shift;
            }
        }

        long duration = Math.Max(1, packet->duration);
        lastPacketEnds[outputStreamIndex] = packet->dts > long.MaxValue - duration
            ? long.MaxValue
            : packet->dts + duration;
    }

    private static int CloseSegmentOutput(
        AVFormatContext** outputContext,
        ref bool outputOpened,
        ref bool headerWritten)
    {
        AVFormatContext* context = *outputContext;
        if (context == null)
        {
            outputOpened = false;
            headerWritten = false;
            return 0;
        }

        int trailerResult = 0;
        if (headerWritten)
        {
            trailerResult = ffmpeg.av_write_trailer(context);
            headerWritten = false;
        }

        if (outputOpened && context->pb != null)
        {
            int closeResult = ffmpeg.avio_closep(&context->pb);
            if (trailerResult >= 0 && closeResult < 0)
            {
                trailerResult = closeResult;
            }
        }

        outputOpened = false;
        ffmpeg.avformat_free_context(context);
        *outputContext = null;
        return trailerResult;
    }

    public static bool TryProbe(
        string sourceFileName,
        out FfmpegMediaProbeResult result,
        out string error,
        CancellationToken token = default)
    {
        result = new FfmpegMediaProbeResult(false, false, 0, 0, 0, 0, 0, 0, string.Empty, new VideoRecordingMetadata());
        error = string.Empty;
        AVFormatContext* inputContext = null;
        AVDictionary* options = null;
        GCHandle interruptHandle = default;

        try
        {
            EnsureInitialized();
            ConfigureInterruptCallback(&inputContext, token, out interruptHandle);
            AddInputOptions(&options, new FfmpegInputOptions(string.Empty, string.Empty, false, string.Empty, false));
            int openResult = ffmpeg.avformat_open_input(&inputContext, sourceFileName, null, &options);
            if (openResult < 0)
            {
                error = ErrorToString(openResult);
                return false;
            }
            ApplyInputRepairPolicy(inputContext);

            int streamInfoResult = ffmpeg.avformat_find_stream_info(inputContext, null);
            if (streamInfoResult < 0)
            {
                error = ErrorToString(streamInfoResult);
                return false;
            }

            bool hasAudio = false;
            bool hasVideo = false;
            int audioStreamCount = 0;
            int videoStreamCount = 0;
            int width = 0;
            int height = 0;
            List<string> streamSignatures = [];
            for (int index = 0; index < inputContext->nb_streams; index++)
            {
                AVStream* stream = inputContext->streams[index];
                AVCodecParameters* parameters = stream->codecpar;
                if (parameters->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    hasAudio = true;
                    audioStreamCount++;
                    streamSignatures.Add(BuildStreamSignature(stream));
                }
                else if (parameters->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    hasVideo = true;
                    videoStreamCount++;
                    streamSignatures.Add(BuildStreamSignature(stream));
                    if (width <= 0
                        && height <= 0
                        && (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                    {
                        width = parameters->width;
                        height = parameters->height;
                    }
                }
            }

            double durationSeconds = inputContext->duration > 0
                ? inputContext->duration / (double)ffmpeg.AV_TIME_BASE
                : 0;
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.FromTags(
                ReadMetadataTags(inputContext->metadata),
                Path.GetFileName(sourceFileName));
            result = new FfmpegMediaProbeResult(
                hasAudio,
                hasVideo,
                audioStreamCount,
                videoStreamCount,
                width,
                height,
                durationSeconds,
                inputContext->bit_rate,
                string.Join(";", streamSignatures.Order(StringComparer.Ordinal)),
                metadata);
            if (!hasAudio && !hasVideo)
            {
                error = "input contains no supported audio or video streams";
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
        finally
        {
            if (inputContext != null)
            {
                AVFormatContext* context = inputContext;
                ffmpeg.avformat_close_input(&context);
            }

            if (options != null)
            {
                ffmpeg.av_dict_free(&options);
            }

            if (interruptHandle.IsAllocated)
            {
                interruptHandle.Free();
            }
        }
    }

    private static string BuildStreamSignature(AVStream* stream)
    {
        AVCodecParameters* parameters = stream->codecpar;
        string channelLayout = string.Empty;
        if (parameters->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
        {
            byte* layoutBuffer = stackalloc byte[128];
            if (ffmpeg.av_channel_layout_describe(&parameters->ch_layout, layoutBuffer, 128) >= 0)
            {
                channelLayout = Marshal.PtrToStringAnsi((IntPtr)layoutBuffer) ?? string.Empty;
            }
        }

        string extraDataHash = parameters->extradata_size > 0 && parameters->extradata != null
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                new ReadOnlySpan<byte>(parameters->extradata, parameters->extradata_size)))
            : string.Empty;
        return string.Join(
            "|",
            (int)parameters->codec_type,
            (int)parameters->codec_id,
            parameters->format,
            parameters->profile,
            parameters->width,
            parameters->height,
            parameters->sample_rate,
            parameters->ch_layout.nb_channels,
            channelLayout,
            (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0,
            ReadMetadataValue(stream->metadata, "language"),
            ReadMetadataValue(stream->metadata, "title"),
            extraDataHash);
    }

    private static string ReadMetadataValue(AVDictionary* metadata, string key)
    {
        AVDictionaryEntry* entry = ffmpeg.av_dict_get(metadata, key, null, 0);
        return entry == null
            ? string.Empty
            : Marshal.PtrToStringUTF8((IntPtr)entry->value) ?? string.Empty;
    }

    private static Dictionary<string, string> ReadMetadataTags(AVDictionary* metadata)
    {
        Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);
        AVDictionaryEntry* entry = null;
        while ((entry = ffmpeg.av_dict_get(metadata, string.Empty, entry, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
        {
            string key = Marshal.PtrToStringUTF8((IntPtr)entry->key) ?? string.Empty;
            string value = Marshal.PtrToStringUTF8((IntPtr)entry->value) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                tags[key] = value;
            }
        }

        return tags;
    }

    private static FfmpegMediaRunResult Remux(
        IReadOnlyList<string> sourceFileNames,
        string targetFileName,
        VideoRecordingMetadata metadata,
        FfmpegInputOptions? inputOptions,
        CancellationToken token,
        Action<long>? onProgress,
        Action<FfmpegPacketProgress>? onPacketProgress = null,
        Action<bool, bool>? onStreamsDiscovered = null)
    {
        if (sourceFileNames.Count == 0 || string.IsNullOrWhiteSpace(targetFileName))
        {
            return new FfmpegMediaRunResult(1, false, false, "input or output is empty");
        }

        AVFormatContext* outputContext = null;
        bool outputOpened = false;
        bool headerWritten = false;
        int[]? streamMap = null;
        string[]? streamSignatures = null;
        long[]? lastPacketEnds = null;
        long timelineOffset = 0;
        bool hadProgress = false;

        try
        {
            EnsureInitialized();
            ThrowIfError(ffmpeg.avformat_alloc_output_context2(&outputContext, null, GetOutputFormatName(targetFileName), targetFileName), "create output");
            if (outputContext == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "output context could not be created");
            }

            AddMetadata(outputContext, metadata);
            outputContext->avoid_negative_ts = ffmpeg.AVFMT_AVOID_NEG_TS_MAKE_NON_NEGATIVE;

            for (int sourceIndex = 0; sourceIndex < sourceFileNames.Count; sourceIndex++)
            {
                if (token.IsCancellationRequested)
                {
                    return new FfmpegMediaRunResult(255, true, hadProgress, string.Empty);
                }

                AVFormatContext* inputContext = null;
                AVDictionary* options = null;
                AVPacket* packet = null;
                GCHandle interruptHandle = default;

                try
                {
                    FfmpegInputOptions effectiveOptions = inputOptions ?? new FfmpegInputOptions(string.Empty, string.Empty, false, string.Empty, false);
                    ConfigureInterruptCallback(&inputContext, token, out interruptHandle);
                    AddInputOptions(&options, effectiveOptions);
                    int openResult = ffmpeg.avformat_open_input(&inputContext, sourceFileNames[sourceIndex], null, &options);
                    if (openResult < 0)
                    {
                        return CreateNativeFailureResult(openResult, token, hadProgress);
                    }
                    ApplyInputRepairPolicy(inputContext);

                    int streamInfoResult = ffmpeg.avformat_find_stream_info(inputContext, null);
                    if (streamInfoResult < 0)
                    {
                        return CreateNativeFailureResult(streamInfoResult, token, hadProgress);
                    }

                    if (sourceIndex == 0)
                    {
                        streamMap = CreateOutputStreams(inputContext, outputContext);
                        if (!streamMap.Any(index => index >= 0))
                        {
                            return new FfmpegMediaRunResult(1, false, false, "input contains no supported audio or video streams");
                        }
                        NotifyStreamPresence(inputContext, onStreamsDiscovered);
                        streamSignatures = CreateStreamSignatures(inputContext, streamMap, (int)outputContext->nb_streams);
                        lastPacketEnds = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)outputContext->nb_streams).ToArray();

                        if ((outputContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                        {
                            ThrowIfError(ffmpeg.avio_open(&outputContext->pb, targetFileName, ffmpeg.AVIO_FLAG_WRITE), "open output");
                            outputOpened = true;
                        }

                        AVDictionary* writeOptions = null;
                        try
                        {
                            if (VideoRecordingMetadataStore.UsesMovMetadataTags(targetFileName))
                            {
                                ffmpeg.av_dict_set(&writeOptions, "movflags", "use_metadata_tags", 0);
                            }

                            ThrowIfError(ffmpeg.avformat_write_header(outputContext, &writeOptions), "write header");
                            headerWritten = true;
                        }
                        finally
                        {
                            if (writeOptions != null)
                            {
                                ffmpeg.av_dict_free(&writeOptions);
                            }
                        }
                    }
                    else if (streamMap == null || streamSignatures == null || lastPacketEnds == null)
                    {
                        return new FfmpegMediaRunResult(1, false, hadProgress, "output stream map is missing");
                    }
                    else
                    {
                        streamMap = CreateCompatibleStreamMap(inputContext, streamSignatures);
                        if (!streamMap.Any(index => index >= 0))
                        {
                            return new FfmpegMediaRunResult(1, false, hadProgress, "input streams are incompatible with the first source");
                        }
                    }

                    long sourceTimestampBase = inputContext->start_time;
                    long sourceDecodeEndTimestamp = timelineOffset;
                    int referenceStreamIndex = GetSegmentReferenceStreamIndex(inputContext);
                    if (referenceStreamIndex < 0)
                    {
                        return new FfmpegMediaRunResult(1, false, hadProgress, "input contains no supported audio or video streams");
                    }
                    bool sourceHadReferenceProgress = false;
                    long[] nextInputDts = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)inputContext->nb_streams).ToArray();
                    packet = ffmpeg.av_packet_alloc();
                    if (packet == null)
                    {
                        return new FfmpegMediaRunResult(1, false, hadProgress, "packet allocation failed");
                    }

                    while (!token.IsCancellationRequested)
                    {
                        int readResult = ffmpeg.av_read_frame(inputContext, packet);
                        if (readResult < 0)
                        {
                            if (readResult == ffmpeg.AVERROR_EOF)
                            {
                                break;
                            }

                            return CreateNativeFailureResult(readResult, token, hadProgress);
                        }

                        int inputStreamIndex = packet->stream_index;
                        if (inputStreamIndex < 0 || inputStreamIndex >= streamMap.Length || streamMap[inputStreamIndex] < 0)
                        {
                            ffmpeg.av_packet_unref(packet);
                            continue;
                        }

                        int outputStreamIndex = streamMap[inputStreamIndex];
                        AVStream* inputStream = inputContext->streams[inputStreamIndex];
                        AVStream* outputStream = outputContext->streams[outputStreamIndex];
                        AVMediaType mediaType = inputStream->codecpar->codec_type;
                        int packetSize = Math.Max(0, packet->size);

                        EnsurePacketDts(packet, inputStream, inputStreamIndex, nextInputDts, sourceTimestampBase);
                        if (sourceTimestampBase == ffmpeg.AV_NOPTS_VALUE)
                        {
                            sourceTimestampBase = GetPacketTimestamp(packet, inputStream);
                        }
                        NormalizeSourcePacketTimestamps(packet, inputStream, sourceTimestampBase);
                        ffmpeg.av_packet_rescale_ts(packet, inputStream->time_base, outputStream->time_base);
                        ApplyTimelineOffset(packet, outputStream, timelineOffset);
                        NormalizePacketDts(packet, outputStreamIndex, lastPacketEnds);
                        packet->stream_index = outputStreamIndex;
                        packet->pos = -1;
                        sourceDecodeEndTimestamp = Math.Max(
                            sourceDecodeEndTimestamp,
                            GetPacketDecodeEndTimestamp(packet, outputStream));

                        int writeResult = ffmpeg.av_interleaved_write_frame(outputContext, packet);
                        ffmpeg.av_packet_unref(packet);
                        if (writeResult < 0)
                        {
                            return CreateNativeFailureResult(writeResult, token, hadProgress);
                        }

                        sourceHadReferenceProgress |= inputStreamIndex == referenceStreamIndex;
                        hadProgress = true;
                        onProgress?.Invoke(packetSize);
                        onPacketProgress?.Invoke(new FfmpegPacketProgress(
                            packetSize,
                            mediaType == AVMediaType.AVMEDIA_TYPE_VIDEO,
                            mediaType == AVMediaType.AVMEDIA_TYPE_AUDIO));
                    }

                    if (token.IsCancellationRequested)
                    {
                        return new FfmpegMediaRunResult(255, true, hadProgress, string.Empty);
                    }

                    if (!sourceHadReferenceProgress)
                    {
                        return new FfmpegMediaRunResult(1, false, hadProgress, $"source {sourceIndex + 1} contains no readable media packets");
                    }
                    if (inputOptions == null && !IsFileInputFullyConsumed(inputContext, sourceFileNames[sourceIndex]))
                    {
                        return new FfmpegMediaRunResult(1, false, hadProgress, $"source {sourceIndex + 1} ended before the physical file end");
                    }

                    timelineOffset = Math.Max(timelineOffset, sourceDecodeEndTimestamp);
                }
                finally
                {
                    if (packet != null)
                    {
                        AVPacket* packetPointer = packet;
                        ffmpeg.av_packet_free(&packetPointer);
                    }

                    if (inputContext != null)
                    {
                        AVFormatContext* context = inputContext;
                        ffmpeg.avformat_close_input(&context);
                    }

                    if (options != null)
                    {
                        ffmpeg.av_dict_free(&options);
                    }

                    if (interruptHandle.IsAllocated)
                    {
                        interruptHandle.Free();
                    }
                }
            }

            int closeResult = CloseSegmentOutput(&outputContext, ref outputOpened, ref headerWritten);
            return closeResult < 0
                ? CreateNativeFailureResult(closeResult, token, hadProgress)
                : new FfmpegMediaRunResult(
                    0,
                    false,
                    hadProgress,
                    string.Empty,
                    Math.Max(0d, timelineOffset / (double)ffmpeg.AV_TIME_BASE));
        }
        catch (Exception e)
        {
            return token.IsCancellationRequested
                ? CreateCanceledResult(hadProgress)
                : new FfmpegMediaRunResult(1, false, hadProgress, e.ToString());
        }
        finally
        {
            if (outputContext != null)
            {
                if (headerWritten)
                {
                    _ = ffmpeg.av_write_trailer(outputContext);
                }

                if (outputOpened && outputContext->pb != null)
                {
                    ffmpeg.avio_closep(&outputContext->pb);
                }

                ffmpeg.avformat_free_context(outputContext);
            }
        }
    }

    private static void NormalizeSourcePacketTimestamps(
        AVPacket* packet,
        AVStream* inputStream,
        long sourceTimestampBase)
    {
        if (sourceTimestampBase == ffmpeg.AV_NOPTS_VALUE)
        {
            return;
        }

        long inputOffset = ffmpeg.av_rescale_q(
            sourceTimestampBase,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
            inputStream->time_base);
        if (packet->pts != ffmpeg.AV_NOPTS_VALUE)
        {
            packet->pts -= inputOffset;
        }

        if (packet->dts != ffmpeg.AV_NOPTS_VALUE)
        {
            packet->dts -= inputOffset;
        }
    }

    private static void ApplyTimelineOffset(AVPacket* packet, AVStream* outputStream, long timelineOffset)
    {
        long outputOffset = ffmpeg.av_rescale_q(
            timelineOffset,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
            outputStream->time_base);
        if (packet->pts != ffmpeg.AV_NOPTS_VALUE)
        {
            packet->pts += outputOffset;
        }

        if (packet->dts != ffmpeg.AV_NOPTS_VALUE)
        {
            packet->dts += outputOffset;
        }
    }

    private static long GetPacketDecodeEndTimestamp(AVPacket* packet, AVStream* outputStream)
    {
        if (packet->dts == ffmpeg.AV_NOPTS_VALUE)
        {
            return 0;
        }

        long duration = Math.Max(1, packet->duration);
        long endTimestamp = packet->dts > long.MaxValue - duration
            ? long.MaxValue
            : packet->dts + duration;
        return ffmpeg.av_rescale_q(
            endTimestamp,
            outputStream->time_base,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE });
    }

    private static bool IsFileInputFullyConsumed(AVFormatContext* inputContext, string sourceFileName)
    {
        string extension = Path.GetExtension(sourceFileName);
        if (!extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".flv", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        AVIOContext* input = inputContext->pb;
        if (input == null || input->error < 0)
        {
            return false;
        }
        long inputSize = ffmpeg.avio_size(input);
        if (inputSize <= 0)
        {
            return true;
        }
        return ffmpeg.avio_tell(input) >= inputSize;
    }

    private static long AddSaturated(long value, long offset)
    {
        if (offset > 0 && value > long.MaxValue - offset)
        {
            return long.MaxValue;
        }
        if (offset < 0 && value < long.MinValue - offset)
        {
            return long.MinValue;
        }

        return value + offset;
    }

    private static long SubtractSaturated(long value, long offset)
    {
        if (offset > 0 && value < long.MinValue + offset)
        {
            return long.MinValue;
        }
        if (offset < 0 && value > long.MaxValue + offset)
        {
            return long.MaxValue;
        }

        return value - offset;
    }

    private static void NotifyStreamPresence(AVFormatContext* inputContext, Action<bool, bool>? onStreamsDiscovered)
    {
        if (onStreamsDiscovered == null)
        {
            return;
        }
        bool hasVideo = false;
        bool hasAudio = false;
        for (int index = 0; index < inputContext->nb_streams; index++)
        {
            AVMediaType mediaType = inputContext->streams[index]->codecpar->codec_type;
            hasVideo |= mediaType == AVMediaType.AVMEDIA_TYPE_VIDEO;
            hasAudio |= mediaType == AVMediaType.AVMEDIA_TYPE_AUDIO;
        }
        onStreamsDiscovered(hasVideo, hasAudio);
    }

    private static int[] CreateOutputStreams(AVFormatContext* inputContext, AVFormatContext* outputContext)
    {
        int[] streamMap = Enumerable.Repeat(-1, (int)inputContext->nb_streams).ToArray();
        for (int index = 0; index < inputContext->nb_streams; index++)
        {
            AVStream* inputStream = inputContext->streams[index];
            AVCodecParameters* inputParameters = inputStream->codecpar;
            if (inputParameters->codec_type is not AVMediaType.AVMEDIA_TYPE_AUDIO and not AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                continue;
            }

            AVStream* outputStream = ffmpeg.avformat_new_stream(outputContext, null);
            if (outputStream == null)
            {
                throw new InvalidOperationException("output stream could not be created");
            }

            ThrowIfError(ffmpeg.avcodec_parameters_copy(outputStream->codecpar, inputParameters), "copy stream parameters");
            outputStream->codecpar->codec_tag = 0;
            outputStream->time_base = inputStream->time_base;
            outputStream->avg_frame_rate = inputStream->avg_frame_rate;
            outputStream->r_frame_rate = inputStream->r_frame_rate;
            outputStream->sample_aspect_ratio = inputStream->sample_aspect_ratio;
            outputStream->disposition = inputStream->disposition;
            outputStream->id = inputStream->id;
            ThrowIfError(ffmpeg.av_dict_copy(&outputStream->metadata, inputStream->metadata, 0), "copy stream metadata");
            streamMap[index] = outputStream->index;
        }

        return streamMap;
    }

    private static string[] CreateStreamSignatures(
        AVFormatContext* inputContext,
        int[] streamMap,
        int outputStreamCount)
    {
        string[] signatures = new string[outputStreamCount];
        for (int inputIndex = 0; inputIndex < streamMap.Length; inputIndex++)
        {
            int outputIndex = streamMap[inputIndex];
            if (outputIndex >= 0)
            {
                signatures[outputIndex] = BuildStreamSignature(inputContext->streams[inputIndex]);
            }
        }

        return signatures;
    }

    private static int[] CreateCompatibleStreamMap(AVFormatContext* inputContext, IReadOnlyList<string> outputStreamSignatures)
    {
        int[] streamMap = Enumerable.Repeat(-1, (int)inputContext->nb_streams).ToArray();
        bool[] matchedOutputs = new bool[outputStreamSignatures.Count];
        int mediaStreamCount = 0;
        for (int inputIndex = 0; inputIndex < inputContext->nb_streams; inputIndex++)
        {
            AVStream* inputStream = inputContext->streams[inputIndex];
            AVCodecParameters* inputParameters = inputStream->codecpar;
            if (inputParameters->codec_type is not AVMediaType.AVMEDIA_TYPE_AUDIO and not AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                continue;
            }
            mediaStreamCount++;

            string inputSignature = BuildStreamSignature(inputStream);
            for (int outputIndex = 0; outputIndex < outputStreamSignatures.Count; outputIndex++)
            {
                if (matchedOutputs[outputIndex])
                {
                    continue;
                }

                if (!string.Equals(inputSignature, outputStreamSignatures[outputIndex], StringComparison.Ordinal))
                {
                    continue;
                }

                streamMap[inputIndex] = outputIndex;
                matchedOutputs[outputIndex] = true;
                break;
            }
        }

        int matchedStreamCount = streamMap.Count(index => index >= 0);
        return mediaStreamCount == outputStreamSignatures.Count && matchedStreamCount == mediaStreamCount
            ? streamMap
            : Enumerable.Repeat(-1, streamMap.Length).ToArray();
    }

    private static void AddMetadata(AVFormatContext* outputContext, VideoRecordingMetadata metadata)
    {
        AVDictionary** dictionary = &outputContext->metadata;
        AddMetadata(dictionary, "title", metadata.Title);
        AddMetadata(dictionary, "artist", metadata.NickName);
        AddMetadata(dictionary, "date", FormatTimestamp(metadata.RecordedAt));
        AddMetadata(dictionary, "emerde_file_name", metadata.FileName);
        AddMetadata(dictionary, "emerde_nick_name", metadata.NickName);
        AddMetadata(dictionary, "emerde_room_url", metadata.RoomUrl);
        AddMetadata(dictionary, "emerde_platform", metadata.Platform);
        AddMetadata(dictionary, "emerde_title", metadata.Title);
        AddMetadata(dictionary, "emerde_resolution", metadata.Resolution);
        AddMetadata(dictionary, "emerde_bitrate", metadata.Bitrate);
        AddMetadata(dictionary, "emerde_cover_path", metadata.CoverPath);
        AddMetadata(dictionary, "emerde_recorded_at", FormatTimestamp(metadata.RecordedAt));
    }

    private static void AddMetadata(AVDictionary** dictionary, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ffmpeg.av_dict_set(dictionary, key, value, 0);
        }
    }

    private static string? GetOutputFormatName(string targetFileName)
    {
        return Path.GetExtension(targetFileName).ToLowerInvariant() switch
        {
            ".m4v" => "mp4",
            _ => null,
        };
    }

    private static string FormatTimestamp(DateTime timestamp)
    {
        return timestamp > DateTime.MinValue
            ? timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static void AddInputOptions(AVDictionary** options, FfmpegInputOptions inputOptions)
    {
        ffmpeg.av_dict_set(options, "fflags", "+genpts+discardcorrupt+sortdts", 0);
        ffmpeg.av_dict_set(options, "err_detect", "ignore_err", 0);
        ffmpeg.av_dict_set(options, "protocol_whitelist", "rtmp,crypto,file,http,https,tcp,tls,udp,rtp,httpproxy", 0);
        ffmpeg.av_dict_set(options, "analyzeduration", "20000000", 0);
        ffmpeg.av_dict_set(options, "probesize", "10000000", 0);
        ffmpeg.av_dict_set(options, "rw_timeout", inputOptions.IsLive ? "15000000" : "5000000", 0);
        if (inputOptions.IsLive)
        {
            ffmpeg.av_dict_set(options, "reconnect", "1", 0);
            ffmpeg.av_dict_set(options, "reconnect_streamed", "1", 0);
            ffmpeg.av_dict_set(options, "reconnect_at_eof", "1", 0);
            ffmpeg.av_dict_set(options, "reconnect_on_network_error", "1", 0);
            ffmpeg.av_dict_set(options, "reconnect_delay_max", "8", 0);
            ffmpeg.av_dict_set(options, "reconnect_delay_total_max", "90", 0);
            ffmpeg.av_dict_set(options, "reconnect_max_retries", "12", 0);
            ffmpeg.av_dict_set(options, "reconnect_on_http_error", "4xx,5xx", 0);
        }
        if (!string.IsNullOrWhiteSpace(inputOptions.UserAgent))
        {
            ffmpeg.av_dict_set(options, "user_agent", inputOptions.UserAgent, 0);
        }

        if (!string.IsNullOrWhiteSpace(inputOptions.Headers))
        {
            ffmpeg.av_dict_set(options, "headers", inputOptions.Headers, 0);
        }

        if (inputOptions.IsUseProxy && !string.IsNullOrWhiteSpace(inputOptions.HttpProxy))
        {
            ffmpeg.av_dict_set(options, "http_proxy", inputOptions.HttpProxy, 0);
        }
    }

    private static void ConfigureInterruptCallback(
        AVFormatContext** inputContext,
        CancellationToken token,
        out GCHandle interruptHandle)
    {
        interruptHandle = default;
        *inputContext = ffmpeg.avformat_alloc_context();
        if (*inputContext == null)
        {
            throw new InvalidOperationException("input context could not be created");
        }
        if (!token.CanBeCanceled)
        {
            return;
        }

        InterruptState state = new(token);
        interruptHandle = GCHandle.Alloc(state);
        (*inputContext)->interrupt_callback.callback = state.Callback;
        (*inputContext)->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(interruptHandle);
    }

    private static int InterruptCallback(void* opaque)
    {
        if (opaque == null)
        {
            return 0;
        }

        try
        {
            return GCHandle.FromIntPtr((IntPtr)opaque).Target is InterruptState state
                && state.Token.IsCancellationRequested
                    ? 1
                    : 0;
        }
        catch
        {
            return 1;
        }
    }

    private sealed class InterruptState
    {
        public InterruptState(CancellationToken token)
        {
            Token = token;
            Callback = InterruptCallback;
        }

        public CancellationToken Token { get; }

        public AVIOInterruptCB_callback Callback { get; }
    }

    private static void ApplyInputRepairPolicy(AVFormatContext* inputContext)
    {
        if (inputContext != null)
        {
            inputContext->flags |= InputFormatFlags;
            inputContext->error_recognition |= InputErrorRecognitionFlags;
        }
    }

    private static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        lock (InitializeLock)
        {
            if (initialized)
            {
                return;
            }

            if (Directory.Exists(LibraryDirectory))
            {
                ffmpeg.RootPath = Path.GetFullPath(LibraryDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
            }

            DynamicallyLoadedBindings.FunctionResolver = new UnicodeWindowsFunctionResolver(ffmpeg.RootPath);
            DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = false;
            DynamicallyLoadedBindings.Initialize();
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            initialized = true;
        }
    }

    private static void ThrowIfError(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"{operation}: {ErrorToString(result)}");
        }
    }

    private static FfmpegMediaRunResult CreateNativeFailureResult(int result, CancellationToken token, bool hadProgress)
    {
        return token.IsCancellationRequested
            ? CreateCanceledResult(hadProgress)
            : new FfmpegMediaRunResult(result, false, hadProgress, ErrorToString(result));
    }

    private static FfmpegMediaRunResult CreateCanceledResult(bool hadProgress)
    {
        return new FfmpegMediaRunResult(255, true, hadProgress, string.Empty);
    }

    private static string ErrorToString(int error)
    {
        byte* buffer = stackalloc byte[1024];
        ffmpeg.av_strerror(error, buffer, 1024);
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? error.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class UnicodeWindowsFunctionResolver(string rootPath) : IFunctionResolver
    {
        private static readonly IReadOnlyDictionary<string, string[]> Dependencies = new Dictionary<string, string[]>
        {
            ["avcodec"] = ["avutil", "swresample"],
            ["avdevice"] = ["avcodec", "avfilter", "avformat", "avutil"],
            ["avfilter"] = ["avcodec", "avformat", "avutil", "postproc", "swresample", "swscale"],
            ["avformat"] = ["avcodec", "avutil"],
            ["avutil"] = [],
            ["postproc"] = ["avutil"],
            ["swresample"] = ["avutil"],
            ["swscale"] = ["avutil"],
        };

        private readonly Dictionary<string, IntPtr> loadedLibraries = [];
        private readonly object syncRoot = new();

        public T? GetFunctionDelegate<T>(string libraryName, string functionName, bool throwOnError = true)
        {
            IntPtr library = GetOrLoadLibrary(libraryName, throwOnError);
            if (library == IntPtr.Zero)
            {
                return default;
            }

            if (!NativeLibrary.TryGetExport(library, functionName, out IntPtr address))
            {
                if (throwOnError)
                {
                    throw new EntryPointNotFoundException("Could not find the entrypoint for " + functionName + ".");
                }

                return default;
            }

            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        private IntPtr GetOrLoadLibrary(string libraryName, bool throwOnError)
        {
            if (loadedLibraries.TryGetValue(libraryName, out IntPtr library))
            {
                return library;
            }

            lock (syncRoot)
            {
                if (loadedLibraries.TryGetValue(libraryName, out library))
                {
                    return library;
                }

                if (Dependencies.TryGetValue(libraryName, out string[]? dependencies))
                {
                    foreach (string dependency in dependencies)
                    {
                        _ = GetOrLoadLibrary(dependency, false);
                    }
                }

                string path = GetLibraryPath(libraryName);
                try
                {
                    library = NativeLibrary.Load(path);
                    loadedLibraries[libraryName] = library;
                    return library;
                }
                catch (Exception e) when (!throwOnError && e is DllNotFoundException or BadImageFormatException or FileNotFoundException)
                {
                    return IntPtr.Zero;
                }
                catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or FileNotFoundException)
                {
                    throw new DllNotFoundException($"Unable to load DLL '{path}'.", e);
                }
            }
        }

        private string GetLibraryPath(string libraryName)
        {
            if (!ffmpeg.LibraryVersionMap.TryGetValue(libraryName, out int version))
            {
                return Path.Combine(rootPath, libraryName + ".dll");
            }

            return Path.Combine(rootPath, $"{libraryName}-{version}.dll");
        }
    }
}
