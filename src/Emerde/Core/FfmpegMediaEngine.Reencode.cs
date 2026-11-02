using FFmpeg.AutoGen;
using System.Runtime.InteropServices;

#pragma warning disable CS0618

namespace Emerde.Core;

internal static unsafe partial class FfmpegMediaEngine
{
    private const int AvErrorAgain = -11;

    public static FfmpegMediaRunResult ReencodeFile(
        string sourceFileName,
        string targetFileName,
        VideoRecordingMetadata metadata,
        CancellationToken token,
        double maximumTimelineEndSeconds = 0d)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName) || string.IsNullOrWhiteSpace(targetFileName))
        {
            return new FfmpegMediaRunResult(1, false, false, "reencode_path_invalid");
        }

        AVFormatContext* inputContext = null;
        AVFormatContext* outputContext = null;
        AVCodecContext* decoderContext = null;
        AVCodecContext* encoderContext = null;
        AVFrame* decodedFrame = null;
        AVFrame* convertedFrame = null;
        AVPacket* packet = null;
        AVPacket* encodedPacket = null;
        SwsContext* scaleContext = null;
        AVDictionary* inputOptions = null;
        AVDictionary* outputOptions = null;
        GCHandle interruptHandle = default;
        bool outputOpened = false;
        bool headerWritten = false;
        bool hadProgress = false;
        long writtenPackets = 0;
        double audioEndSeconds = 0d;
        double videoEndSeconds = 0d;
        int videoStreamIndex = -1;
        int audioStreamIndex = -1;
        int outputVideoStreamIndex = -1;
        int outputAudioStreamIndex = -1;

        try
        {
            EnsureInitialized();
            ConfigureInterruptCallback(&inputContext, token, out interruptHandle);
            AddInputOptions(&inputOptions, new FfmpegInputOptions(string.Empty, string.Empty, false, string.Empty, false));
            int openResult = ffmpeg.avformat_open_input(&inputContext, sourceFileName, null, &inputOptions);
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

            videoStreamIndex = ffmpeg.av_find_best_stream(inputContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            audioStreamIndex = ffmpeg.av_find_best_stream(inputContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            if (videoStreamIndex < 0)
            {
                return new FfmpegMediaRunResult(1, false, false, "reencode_video_stream_missing");
            }

            AVStream* inputVideoStream = inputContext->streams[videoStreamIndex];
            AVCodec* decoder = ffmpeg.avcodec_find_decoder(inputVideoStream->codecpar->codec_id);
            AVCodec* encoder = FindVideoEncoder();
            if (decoder == null || encoder == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "reencode_video_codec_unavailable");
            }

            decoderContext = ffmpeg.avcodec_alloc_context3(decoder);
            if (decoderContext == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "reencode_decoder_context_allocation_failed");
            }
            ThrowIfError(ffmpeg.avcodec_parameters_to_context(decoderContext, inputVideoStream->codecpar), "copy reencode decoder parameters");
            ThrowIfError(ffmpeg.avcodec_open2(decoderContext, decoder, null), "open reencode decoder");

            ThrowIfError(ffmpeg.avformat_alloc_output_context2(&outputContext, null, null, targetFileName), "create reencode output");
            if (outputContext == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "reencode_output_context_allocation_failed");
            }

            encoderContext = ffmpeg.avcodec_alloc_context3(encoder);
            if (encoderContext == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "reencode_encoder_context_allocation_failed");
            }
            encoderContext->codec_id = encoder->id;
            encoderContext->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
            encoderContext->width = Math.Max(1, decoderContext->width);
            encoderContext->height = Math.Max(1, decoderContext->height);
            encoderContext->pix_fmt = SelectEncoderPixelFormat(encoder, (AVPixelFormat)decoderContext->pix_fmt);
            encoderContext->time_base = SelectVideoTimeBase(inputVideoStream);
            encoderContext->framerate = inputVideoStream->avg_frame_rate.num > 0
                ? inputVideoStream->avg_frame_rate
                : inputVideoStream->r_frame_rate;
            encoderContext->bit_rate = Math.Max(1, inputVideoStream->codecpar->bit_rate > 0 ? inputVideoStream->codecpar->bit_rate : 4_000_000);
            encoderContext->gop_size = Math.Max(1, (int)Math.Round(Math.Max(1d, (encoderContext->framerate.num > 0 && encoderContext->framerate.den > 0 ? encoderContext->framerate.num / (double)encoderContext->framerate.den : 30d) * 2d)));
            encoderContext->max_b_frames = 2;
            if ((outputContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                encoderContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }
            ThrowIfError(ffmpeg.avcodec_open2(encoderContext, encoder, null), "open reencode encoder");

            AVStream* outputVideoStream = ffmpeg.avformat_new_stream(outputContext, null);
            if (outputVideoStream == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "reencode_video_output_stream_allocation_failed");
            }
            outputVideoStreamIndex = outputVideoStream->index;
            outputVideoStream->time_base = encoderContext->time_base;
            outputVideoStream->avg_frame_rate = encoderContext->framerate;
            ThrowIfError(ffmpeg.avcodec_parameters_from_context(outputVideoStream->codecpar, encoderContext), "copy reencode encoder parameters");

            if (audioStreamIndex >= 0)
            {
                AVStream* inputAudioStream = inputContext->streams[audioStreamIndex];
                AVStream* outputAudioStream = ffmpeg.avformat_new_stream(outputContext, null);
                if (outputAudioStream == null)
                {
                    return new FfmpegMediaRunResult(1, false, false, "reencode_audio_output_stream_allocation_failed");
                }
                outputAudioStreamIndex = outputAudioStream->index;
                ThrowIfError(ffmpeg.avcodec_parameters_copy(outputAudioStream->codecpar, inputAudioStream->codecpar), "copy reencode audio parameters");
                outputAudioStream->codecpar->codec_tag = 0;
                outputAudioStream->time_base = inputAudioStream->time_base;
            }

            AddMetadata(outputContext, metadata);
            if ((outputContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                ThrowIfError(ffmpeg.avio_open(&outputContext->pb, targetFileName, ffmpeg.AVIO_FLAG_WRITE), "open reencode output");
                outputOpened = true;
            }
            ThrowIfError(ffmpeg.avformat_write_header(outputContext, &outputOptions), "write reencode output header");
            headerWritten = true;

            decodedFrame = ffmpeg.av_frame_alloc();
            convertedFrame = ffmpeg.av_frame_alloc();
            packet = ffmpeg.av_packet_alloc();
            encodedPacket = ffmpeg.av_packet_alloc();
            if (decodedFrame == null || convertedFrame == null || packet == null || encodedPacket == null)
            {
                return new FfmpegMediaRunResult(1, false, hadProgress, "reencode_frame_or_packet_allocation_failed");
            }

            AVStream* inputAudioStreamForPackets = audioStreamIndex >= 0 ? inputContext->streams[audioStreamIndex] : null;
            while (!token.IsCancellationRequested)
            {
                int readResult = ffmpeg.av_read_frame(inputContext, packet);
                if (readResult == ffmpeg.AVERROR_EOF)
                {
                    break;
                }
                if (readResult < 0)
                {
                    return CreateNativeFailureResult(readResult, token, hadProgress);
                }

                if (packet->stream_index == audioStreamIndex && inputAudioStreamForPackets != null && outputAudioStreamIndex >= 0)
                {
                    AVStream* outputAudioStream = outputContext->streams[outputAudioStreamIndex];
                    ffmpeg.av_packet_rescale_ts(packet, inputAudioStreamForPackets->time_base, outputAudioStream->time_base);
                    packet->stream_index = outputAudioStreamIndex;
                    if (packet->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        audioEndSeconds = Math.Max(audioEndSeconds, packet->pts * ffmpeg.av_q2d(outputAudioStream->time_base));
                    }
                    if (ffmpeg.av_interleaved_write_frame(outputContext, packet) < 0)
                    {
                        ffmpeg.av_packet_unref(packet);
                        return new FfmpegMediaRunResult(1, false, hadProgress, "reencode_audio_packet_write_failed");
                    }
                    writtenPackets++;
                    hadProgress = true;
                    ffmpeg.av_packet_unref(packet);
                    continue;
                }

                if (packet->stream_index != videoStreamIndex)
                {
                    ffmpeg.av_packet_unref(packet);
                    continue;
                }

                int sendResult = ffmpeg.avcodec_send_packet(decoderContext, packet);
                ffmpeg.av_packet_unref(packet);
                if (sendResult < 0 && sendResult != AvErrorAgain)
                {
                    continue;
                }
                if (!DrainDecodedFrames(
                    decoderContext,
                    encoderContext,
                    inputVideoStream,
                    outputContext,
                    outputVideoStreamIndex,
                    decodedFrame,
                    convertedFrame,
                    encodedPacket,
                    ref scaleContext,
                    ref hadProgress,
                    ref writtenPackets,
                    ref videoEndSeconds,
                    token))
                {
                    return new FfmpegMediaRunResult(1, false, hadProgress, "reencode_video_decode_failed");
                }
            }

            if (token.IsCancellationRequested)
            {
                return CreateCanceledResult(hadProgress);
            }

            if (ffmpeg.avcodec_send_packet(decoderContext, null) >= 0
                && DrainDecodedFrames(
                    decoderContext,
                    encoderContext,
                    inputVideoStream,
                    outputContext,
                    outputVideoStreamIndex,
                    decodedFrame,
                    convertedFrame,
                    encodedPacket,
                    ref scaleContext,
                    ref hadProgress,
                    ref writtenPackets,
                    ref videoEndSeconds,
                    token))
            {
                if (ffmpeg.avcodec_send_frame(encoderContext, null) < 0)
                {
                    return new FfmpegMediaRunResult(1, false, hadProgress, "reencode_encoder_flush_failed");
                }
                if (!DrainEncodedPackets(encoderContext, outputContext, outputVideoStreamIndex, encodedPacket, ref writtenPackets, ref videoEndSeconds, token))
                {
                    return new FfmpegMediaRunResult(1, false, hadProgress, "reencode_video_flush_failed");
                }
            }

            int trailerResult = ffmpeg.av_write_trailer(outputContext);
            headerWritten = false;
            if (trailerResult < 0)
            {
                return CreateNativeFailureResult(trailerResult, token, hadProgress);
            }
            return new FfmpegMediaRunResult(
                0,
                false,
                hadProgress && writtenPackets > 0,
                string.Empty,
                Math.Max(audioEndSeconds, videoEndSeconds),
                AudioEndSeconds: audioEndSeconds,
                VideoEndSeconds: videoEndSeconds);
        }
        catch (OperationCanceledException)
        {
            return CreateCanceledResult(hadProgress);
        }
        catch (Exception exception)
        {
            return new FfmpegMediaRunResult(1, false, hadProgress, exception.Message);
        }
        finally
        {
            if (headerWritten && outputContext != null)
            {
                ffmpeg.av_write_trailer(outputContext);
            }
            if (outputOpened && outputContext != null)
            {
                ffmpeg.avio_closep(&outputContext->pb);
            }
            if (scaleContext != null)
            {
                ffmpeg.sws_freeContext(scaleContext);
            }
            if (encodedPacket != null)
            {
                AVPacket* pointer = encodedPacket;
                ffmpeg.av_packet_free(&pointer);
            }
            if (packet != null)
            {
                AVPacket* pointer = packet;
                ffmpeg.av_packet_free(&pointer);
            }
            if (convertedFrame != null)
            {
                AVFrame* pointer = convertedFrame;
                ffmpeg.av_frame_free(&pointer);
            }
            if (decodedFrame != null)
            {
                AVFrame* pointer = decodedFrame;
                ffmpeg.av_frame_free(&pointer);
            }
            if (encoderContext != null)
            {
                AVCodecContext* pointer = encoderContext;
                ffmpeg.avcodec_free_context(&pointer);
            }
            if (decoderContext != null)
            {
                AVCodecContext* pointer = decoderContext;
                ffmpeg.avcodec_free_context(&pointer);
            }
            if (outputContext != null)
            {
                ffmpeg.avformat_free_context(outputContext);
            }
            if (inputContext != null)
            {
                AVFormatContext* pointer = inputContext;
                ffmpeg.avformat_close_input(&pointer);
            }
            if (inputOptions != null)
            {
                ffmpeg.av_dict_free(&inputOptions);
            }
            if (outputOptions != null)
            {
                ffmpeg.av_dict_free(&outputOptions);
            }
            if (interruptHandle.IsAllocated)
            {
                interruptHandle.Free();
            }
        }
    }

    private static AVCodec* FindVideoEncoder()
    {
        foreach (AVCodecID codecId in new[] { AVCodecID.AV_CODEC_ID_H264, AVCodecID.AV_CODEC_ID_MPEG4, AVCodecID.AV_CODEC_ID_VP9 })
        {
            AVCodec* encoder = ffmpeg.avcodec_find_encoder(codecId);
            if (encoder != null)
            {
                return encoder;
            }
        }
        return null;
    }

    private static AVPixelFormat SelectEncoderPixelFormat(AVCodec* encoder, AVPixelFormat inputPixelFormat)
    {
        if (encoder->pix_fmts == null)
        {
            return inputPixelFormat == AVPixelFormat.AV_PIX_FMT_NONE ? AVPixelFormat.AV_PIX_FMT_YUV420P : inputPixelFormat;
        }

        for (AVPixelFormat* format = encoder->pix_fmts; *format != AVPixelFormat.AV_PIX_FMT_NONE; format++)
        {
            if (*format == inputPixelFormat)
            {
                return *format;
            }
        }
        return encoder->pix_fmts[0];
    }

    private static AVRational SelectVideoTimeBase(AVStream* stream)
    {
        return stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0
            ? new AVRational { num = stream->avg_frame_rate.den, den = stream->avg_frame_rate.num }
            : stream->time_base;
    }

    private static bool DrainDecodedFrames(
        AVCodecContext* decoderContext,
        AVCodecContext* encoderContext,
        AVStream* inputVideoStream,
        AVFormatContext* outputContext,
        int outputVideoStreamIndex,
        AVFrame* decodedFrame,
        AVFrame* convertedFrame,
        AVPacket* encodedPacket,
        ref SwsContext* scaleContext,
        ref bool hadProgress,
        ref long writtenPackets,
        ref double videoEndSeconds,
        CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int receiveResult = ffmpeg.avcodec_receive_frame(decoderContext, decodedFrame);
            if (receiveResult == ffmpeg.AVERROR_EOF || receiveResult == AvErrorAgain)
            {
                return true;
            }
            if (receiveResult < 0)
            {
                return false;
            }

            if (scaleContext == null)
            {
                scaleContext = ffmpeg.sws_getContext(
                    decodedFrame->width,
                    decodedFrame->height,
                    (AVPixelFormat)decodedFrame->format,
                    encoderContext->width,
                    encoderContext->height,
                    encoderContext->pix_fmt,
                    ffmpeg.SWS_BILINEAR,
                    null,
                    null,
                    null);
                if (scaleContext == null)
                {
                    return false;
                }
                convertedFrame->format = (int)encoderContext->pix_fmt;
                convertedFrame->width = encoderContext->width;
                convertedFrame->height = encoderContext->height;
                if (ffmpeg.av_frame_get_buffer(convertedFrame, 32) < 0)
                {
                    return false;
                }
            }

            if (ffmpeg.av_frame_make_writable(convertedFrame) < 0)
            {
                return false;
            }
            ffmpeg.sws_scale(
                scaleContext,
                decodedFrame->data,
                decodedFrame->linesize,
                0,
                decodedFrame->height,
                convertedFrame->data,
                convertedFrame->linesize);
            long timestamp = decodedFrame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE
                ? decodedFrame->best_effort_timestamp
                : decodedFrame->pts;
            convertedFrame->pts = timestamp == ffmpeg.AV_NOPTS_VALUE
                ? ffmpeg.AV_NOPTS_VALUE
                : ffmpeg.av_rescale_q(timestamp, inputVideoStream->time_base, encoderContext->time_base);
            int sendResult = ffmpeg.avcodec_send_frame(encoderContext, convertedFrame);
            ffmpeg.av_frame_unref(decodedFrame);
            if (sendResult < 0 && sendResult != AvErrorAgain)
            {
                return false;
            }
            if (!DrainEncodedPackets(encoderContext, outputContext, outputVideoStreamIndex, encodedPacket, ref writtenPackets, ref videoEndSeconds, token))
            {
                return false;
            }
            ffmpeg.av_frame_unref(convertedFrame);
            hadProgress = true;
        }
    }

    private static bool DrainEncodedPackets(
        AVCodecContext* encoderContext,
        AVFormatContext* outputContext,
        int outputVideoStreamIndex,
        AVPacket* encodedPacket,
        ref long writtenPackets,
        ref double videoEndSeconds,
        CancellationToken token)
    {
        AVStream* outputStream = outputContext->streams[outputVideoStreamIndex];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int receiveResult = ffmpeg.avcodec_receive_packet(encoderContext, encodedPacket);
            if (receiveResult == ffmpeg.AVERROR_EOF || receiveResult == AvErrorAgain)
            {
                return true;
            }
            if (receiveResult < 0)
            {
                return false;
            }
            ffmpeg.av_packet_rescale_ts(encodedPacket, encoderContext->time_base, outputStream->time_base);
            encodedPacket->stream_index = outputVideoStreamIndex;
            if (encodedPacket->pts != ffmpeg.AV_NOPTS_VALUE)
            {
                videoEndSeconds = Math.Max(videoEndSeconds, encodedPacket->pts * ffmpeg.av_q2d(outputStream->time_base));
            }
            if (ffmpeg.av_interleaved_write_frame(outputContext, encodedPacket) < 0)
            {
                ffmpeg.av_packet_unref(encodedPacket);
                return false;
            }
            writtenPackets++;
            ffmpeg.av_packet_unref(encodedPacket);
        }
    }
}

#pragma warning restore CS0618
