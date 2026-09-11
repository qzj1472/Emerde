using FFmpeg.AutoGen;
using System.Runtime.InteropServices;

namespace Emerde.Core;

internal sealed record FfmpegAudioContentDiagnostic(
    bool IsConclusive,
    double SampledSeconds,
    double Rms,
    double Peak,
    double LongestNearSilenceSeconds,
    int PeakFrameCount,
    int DecodeErrorCount,
    string Error);

internal readonly record struct FfmpegAudioContentSample(
    double StartSeconds,
    double EndSeconds,
    double Rms,
    double Peak,
    double LongestNearSilenceSeconds,
    double ClippingRatio,
    int DecodeErrorCount,
    bool IsConclusive,
    string State);

internal static unsafe partial class FfmpegMediaEngine
{
    private const double NearSilenceRmsThreshold = 0.0005d;
    private const double PeakSampleThreshold = 0.9995d;

    public static bool TryAnalyzeAudioContent(
        string sourceFileName,
        out FfmpegAudioContentDiagnostic result,
        CancellationToken token = default)
    {
        result = new(false, 0d, 0d, 0d, 0d, 0, 0, string.Empty);
        AVFormatContext* inputContext = null;
        AVCodecContext* decoderContext = null;
        AVFrame* frame = null;
        AVPacket* packet = null;
        AVDictionary* options = null;
        GCHandle interruptHandle = default;
        AudioContentAccumulator accumulator = new();

        try
        {
            EnsureInitialized();
            ConfigureInterruptCallback(&inputContext, token, out interruptHandle);
            AddInputOptions(&options, new FfmpegInputOptions(string.Empty, string.Empty, false, string.Empty, false));
            int openResult = ffmpeg.avformat_open_input(&inputContext, sourceFileName, null, &options);
            if (openResult < 0)
            {
                result = new(false, 0d, 0d, 0d, 0d, 0, 0, ErrorToString(openResult));
                return false;
            }
            ApplyInputRepairPolicy(inputContext);
            int streamInfoResult = ffmpeg.avformat_find_stream_info(inputContext, null);
            if (streamInfoResult < 0)
            {
                result = new(false, 0d, 0d, 0d, 0d, 0, 0, ErrorToString(streamInfoResult));
                return false;
            }

            int audioStreamIndex = ffmpeg.av_find_best_stream(inputContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            if (audioStreamIndex < 0)
            {
                result = new(true, 0d, 0d, 0d, 0d, 0, 0, "audio_stream_missing");
                return true;
            }

            AVCodecParameters* parameters = inputContext->streams[audioStreamIndex]->codecpar;
            AVCodec* decoder = ffmpeg.avcodec_find_decoder(parameters->codec_id);
            if (decoder == null)
            {
                result = new(false, 0d, 0d, 0d, 0d, 0, 0, "audio_decoder_missing");
                return false;
            }

            decoderContext = ffmpeg.avcodec_alloc_context3(decoder);
            if (decoderContext == null || ffmpeg.avcodec_parameters_to_context(decoderContext, parameters) < 0)
            {
                result = new(false, 0d, 0d, 0d, 0d, 0, 0, "audio_decoder_context_failed");
                return false;
            }
            if (ffmpeg.avcodec_open2(decoderContext, decoder, null) < 0)
            {
                result = new(false, 0d, 0d, 0d, 0d, 0, 0, "audio_decoder_open_failed");
                return false;
            }

            frame = ffmpeg.av_frame_alloc();
            packet = ffmpeg.av_packet_alloc();
            if (frame == null || packet == null)
            {
                result = new(false, 0d, 0d, 0d, 0d, 0, 0, "audio_diagnostic_allocation_failed");
                return false;
            }

            while (!token.IsCancellationRequested)
            {
                int readResult = ffmpeg.av_read_frame(inputContext, packet);
                if (readResult < 0)
                {
                    break;
                }
                if (packet->stream_index == audioStreamIndex)
                {
                    int sendResult = ffmpeg.avcodec_send_packet(decoderContext, packet);
                    if (sendResult < 0)
                    {
                        accumulator.DecodeErrorCount++;
                    }
                    else
                    {
                        ReceiveAudioContentFrames(decoderContext, frame, accumulator, token);
                    }
                }
                ffmpeg.av_packet_unref(packet);
            }

            if (!token.IsCancellationRequested)
            {
                int flushResult = ffmpeg.avcodec_send_packet(decoderContext, null);
                if (flushResult >= 0)
                {
                    ReceiveAudioContentFrames(decoderContext, frame, accumulator, token);
                }
                else
                {
                    accumulator.DecodeErrorCount++;
                }
            }

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            result = accumulator.ToResult();
            return result.IsConclusive;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            result = accumulator.ToResult(exception.Message);
            return false;
        }
        finally
        {
            if (packet != null)
            {
                AVPacket* packetPointer = packet;
                ffmpeg.av_packet_free(&packetPointer);
            }
            if (frame != null)
            {
                AVFrame* framePointer = frame;
                ffmpeg.av_frame_free(&framePointer);
            }
            if (decoderContext != null)
            {
                AVCodecContext* context = decoderContext;
                ffmpeg.avcodec_free_context(&context);
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

    private static void ReceiveAudioContentFrames(
        AVCodecContext* decoderContext,
        AVFrame* frame,
        AudioContentAccumulator accumulator,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            int receiveResult = ffmpeg.avcodec_receive_frame(decoderContext, frame);
            if (receiveResult < 0)
            {
                return;
            }

            accumulator.Observe(frame);
            ffmpeg.av_frame_unref(frame);
        }
    }

    private static bool IsPlanarSampleFormat(AVSampleFormat format)
    {
        return format is AVSampleFormat.AV_SAMPLE_FMT_U8P
            or AVSampleFormat.AV_SAMPLE_FMT_S16P
            or AVSampleFormat.AV_SAMPLE_FMT_S32P
            or AVSampleFormat.AV_SAMPLE_FMT_FLTP
            or AVSampleFormat.AV_SAMPLE_FMT_DBLP
            or AVSampleFormat.AV_SAMPLE_FMT_S64P;
    }

    private static double ReadAudioSample(byte* data, AVSampleFormat format, int index)
    {
        return format switch
        {
            AVSampleFormat.AV_SAMPLE_FMT_U8 or AVSampleFormat.AV_SAMPLE_FMT_U8P => (data[index] - 128d) / 128d,
            AVSampleFormat.AV_SAMPLE_FMT_S16 or AVSampleFormat.AV_SAMPLE_FMT_S16P => *(short*)(data + index * sizeof(short)) / 32768d,
            AVSampleFormat.AV_SAMPLE_FMT_S32 or AVSampleFormat.AV_SAMPLE_FMT_S32P => *(int*)(data + index * sizeof(int)) / 2147483648d,
            AVSampleFormat.AV_SAMPLE_FMT_S64 or AVSampleFormat.AV_SAMPLE_FMT_S64P => *(long*)(data + index * sizeof(long)) / 9.223372036854776E18d,
            AVSampleFormat.AV_SAMPLE_FMT_FLT or AVSampleFormat.AV_SAMPLE_FMT_FLTP => *(float*)(data + index * sizeof(float)),
            AVSampleFormat.AV_SAMPLE_FMT_DBL or AVSampleFormat.AV_SAMPLE_FMT_DBLP => *(double*)(data + index * sizeof(double)),
            _ => 0d,
        };
    }

    private sealed class AudioContentAccumulator
    {
        private double squaredSum;
        private long sampledCount;
        private double currentNearSilenceSeconds;

        public double SampledSeconds { get; private set; }

        public double Peak { get; private set; }

        public double LongestNearSilenceSeconds { get; private set; }

        public int PeakFrameCount { get; private set; }

        public int DecodeErrorCount { get; set; }

        public void Observe(AVFrame* frame)
        {
            int sampleRate = Math.Max(1, frame->sample_rate);
            int channelCount = Math.Max(1, frame->ch_layout.nb_channels);
            int sampleCount = Math.Max(0, frame->nb_samples);
            if (sampleCount == 0 || frame->extended_data == null)
            {
                return;
            }

            AVSampleFormat format = (AVSampleFormat)frame->format;
            bool planar = IsPlanarSampleFormat(format);
            int stride = Math.Max(1, sampleCount / 4096);
            double frameSquaredSum = 0d;
            double framePeak = 0d;
            long frameSampleCount = 0;
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex += stride)
            {
                for (int channel = 0; channel < channelCount; channel++)
                {
                    byte* data = planar
                        ? frame->extended_data[channel]
                        : frame->extended_data[0];
                    if (data == null)
                    {
                        continue;
                    }

                    int dataIndex = planar
                        ? sampleIndex
                        : sampleIndex * channelCount + channel;
                    double value = Math.Clamp(ReadAudioSample(data, format, dataIndex), -1d, 1d);
                    double absolute = Math.Abs(value);
                    frameSquaredSum += value * value;
                    framePeak = Math.Max(framePeak, absolute);
                    frameSampleCount++;
                }
            }

            if (frameSampleCount == 0)
            {
                return;
            }

            double frameSeconds = sampleCount / (double)sampleRate;
            double frameRms = Math.Sqrt(frameSquaredSum / frameSampleCount);
            SampledSeconds += frameSeconds;
            squaredSum += frameSquaredSum;
            sampledCount += frameSampleCount;
            Peak = Math.Max(Peak, framePeak);
            if (framePeak >= PeakSampleThreshold)
            {
                PeakFrameCount++;
            }
            if (frameRms <= NearSilenceRmsThreshold)
            {
                currentNearSilenceSeconds += frameSeconds;
                LongestNearSilenceSeconds = Math.Max(LongestNearSilenceSeconds, currentNearSilenceSeconds);
            }
            else
            {
                currentNearSilenceSeconds = 0d;
            }
        }

        public FfmpegAudioContentDiagnostic ToResult(string error = "")
        {
            double rms = sampledCount == 0 ? 0d : Math.Sqrt(squaredSum / sampledCount);
            bool conclusive = SampledSeconds > 0d;
            string resultError = string.IsNullOrWhiteSpace(error)
                ? DecodeErrorCount == 0
                    ? conclusive ? string.Empty : "audio_samples_missing"
                    : "audio_decode_errors"
                : error;
            return new(conclusive, SampledSeconds, rms, Peak, LongestNearSilenceSeconds, PeakFrameCount, DecodeErrorCount, resultError);
        }
    }

    private sealed unsafe class LiveAudioContentAnalyzer : IDisposable
    {
        private readonly AVCodecContext* decoderContext;
        private readonly AVFrame* frame;
        private readonly Action<FfmpegAudioContentSample> onSample;
        private readonly AudioContentWindowAccumulator accumulator = new();

        private LiveAudioContentAnalyzer(
            AVCodecContext* decoderContext,
            AVFrame* frame,
            Action<FfmpegAudioContentSample> onSample)
        {
            this.decoderContext = decoderContext;
            this.frame = frame;
            this.onSample = onSample;
        }

        public static LiveAudioContentAnalyzer? TryCreate(
            AVFormatContext* inputContext,
            Action<FfmpegAudioContentSample>? onSample)
        {
            if (onSample == null)
            {
                return null;
            }

            for (int index = 0; index < inputContext->nb_streams; index++)
            {
                AVStream* stream = inputContext->streams[index];
                if (stream->codecpar->codec_type != AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    continue;
                }

                AVCodec* decoder = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
                if (decoder == null)
                {
                    return null;
                }

                AVCodecContext* decoderContext = ffmpeg.avcodec_alloc_context3(decoder);
                if (decoderContext == null)
                {
                    return null;
                }

                if (ffmpeg.avcodec_parameters_to_context(decoderContext, stream->codecpar) < 0
                    || ffmpeg.avcodec_open2(decoderContext, decoder, null) < 0)
                {
                    ffmpeg.avcodec_free_context(&decoderContext);
                    return null;
                }

                AVFrame* frame = ffmpeg.av_frame_alloc();
                if (frame == null)
                {
                    ffmpeg.avcodec_free_context(&decoderContext);
                    return null;
                }

                return new LiveAudioContentAnalyzer(decoderContext, frame, onSample);
            }

            return null;
        }

        public void Observe(AVPacket* packet)
        {
            int sendResult = ffmpeg.avcodec_send_packet(decoderContext, packet);
            if (sendResult < 0)
            {
                accumulator.DecodeErrorCount++;
                return;
            }

            ReceiveFrames();
        }

        public void Dispose()
        {
            int flushResult = ffmpeg.avcodec_send_packet(decoderContext, null);
            if (flushResult >= 0)
            {
                ReceiveFrames();
            }
            else
            {
                accumulator.DecodeErrorCount++;
            }

            accumulator.Flush(onSample);
            AVFrame* framePointer = frame;
            ffmpeg.av_frame_free(&framePointer);
            AVCodecContext* context = decoderContext;
            ffmpeg.avcodec_free_context(&context);
        }

        private void ReceiveFrames()
        {
            while (true)
            {
                int receiveResult = ffmpeg.avcodec_receive_frame(decoderContext, frame);
                if (receiveResult < 0)
                {
                    return;
                }

                accumulator.Observe(frame, onSample);
                ffmpeg.av_frame_unref(frame);
            }
        }
    }

    private sealed class AudioContentWindowAccumulator
    {
        private const double WindowSeconds = 30d;
        private const double NearSilenceSeconds = 3d;
        private double windowStartSeconds;
        private double windowSeconds;
        private double squaredSum;
        private long sampledCount;
        private double peak;
        private double currentNearSilenceSeconds;
        private double longestNearSilenceSeconds;
        private long clippedSampleCount;
        private long totalSampleCount;
        private int decodeErrorCount;
        private bool clippingObserved;
        private double totalSeconds;

        public int DecodeErrorCount
        {
            get => decodeErrorCount;
            set => decodeErrorCount = Math.Max(0, value);
        }

        public void Observe(AVFrame* frame, Action<FfmpegAudioContentSample> onSample)
        {
            int sampleRate = Math.Max(1, frame->sample_rate);
            int channelCount = Math.Max(1, frame->ch_layout.nb_channels);
            int sampleCount = Math.Max(0, frame->nb_samples);
            if (sampleCount == 0 || frame->extended_data == null)
            {
                return;
            }

            AVSampleFormat format = (AVSampleFormat)frame->format;
            bool planar = IsPlanarSampleFormat(format);
            int stride = Math.Max(1, sampleCount / 4096);
            double frameSquaredSum = 0d;
            double framePeak = 0d;
            long frameSampleCount = 0;
            long frameClippedSampleCount = 0;
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex += stride)
            {
                for (int channel = 0; channel < channelCount; channel++)
                {
                    byte* data = planar ? frame->extended_data[channel] : frame->extended_data[0];
                    if (data == null)
                    {
                        continue;
                    }

                    int dataIndex = planar ? sampleIndex : sampleIndex * channelCount + channel;
                    double value = Math.Clamp(ReadAudioSample(data, format, dataIndex), -1d, 1d);
                    double absolute = Math.Abs(value);
                    frameSquaredSum += value * value;
                    framePeak = Math.Max(framePeak, absolute);
                    frameSampleCount++;
                    if (absolute >= PeakSampleThreshold)
                    {
                        frameClippedSampleCount++;
                    }
                }
            }

            if (frameSampleCount == 0)
            {
                return;
            }

            double frameSeconds = sampleCount / (double)sampleRate;
            double frameRms = Math.Sqrt(frameSquaredSum / frameSampleCount);
            totalSeconds += frameSeconds;
            windowSeconds += frameSeconds;
            squaredSum += frameSquaredSum;
            sampledCount += frameSampleCount;
            peak = Math.Max(peak, framePeak);
            clippedSampleCount += frameClippedSampleCount;
            totalSampleCount += frameSampleCount;
            if (frameClippedSampleCount > 0)
            {
                clippingObserved = true;
            }

            if (frameRms <= NearSilenceRmsThreshold)
            {
                currentNearSilenceSeconds += frameSeconds;
                longestNearSilenceSeconds = Math.Max(longestNearSilenceSeconds, currentNearSilenceSeconds);
            }
            else
            {
                currentNearSilenceSeconds = 0d;
            }

            if (windowSeconds >= WindowSeconds)
            {
                Emit(onSample, force: false);
            }
        }

        public void Flush(Action<FfmpegAudioContentSample> onSample)
        {
            if (windowSeconds >= 2d)
            {
                Emit(onSample, force: true);
            }
        }

        private void Emit(Action<FfmpegAudioContentSample> onSample, bool force)
        {
            double endSeconds = totalSeconds;
            double rms = sampledCount == 0 ? 0d : Math.Sqrt(squaredSum / sampledCount);
            double clippingRatio = totalSampleCount == 0 ? 0d : clippedSampleCount / (double)totalSampleCount;
            string state = decodeErrorCount > 0
                ? "decode_error"
                : clippingObserved && longestNearSilenceSeconds >= NearSilenceSeconds
                    ? "clipping_then_silence"
                    : longestNearSilenceSeconds >= NearSilenceSeconds
                        ? "near_silence"
                        : clippingRatio > 0d
                            ? "clipping"
                            : "normal";
            onSample(new FfmpegAudioContentSample(
                windowStartSeconds,
                endSeconds,
                rms,
                peak,
                longestNearSilenceSeconds,
                clippingRatio,
                decodeErrorCount,
                sampledCount > 0,
                state));
            windowStartSeconds = force ? endSeconds : windowStartSeconds + windowSeconds;
            windowSeconds = 0d;
            squaredSum = 0d;
            sampledCount = 0;
            peak = 0d;
            longestNearSilenceSeconds = 0d;
            clippedSampleCount = 0;
            totalSampleCount = 0;
            decodeErrorCount = 0;
            clippingObserved = false;
        }
    }
}
