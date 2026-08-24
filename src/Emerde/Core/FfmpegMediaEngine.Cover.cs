using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Emerde.Core;

internal static unsafe partial class FfmpegMediaEngine
{
    internal static bool TryExtractCoverFrame(
        string sourceFileName,
        double positionSeconds,
        CancellationToken token,
        out BitmapSource frame,
        out string error)
    {
        frame = null!;
        error = string.Empty;
        AVFormatContext* inputContext = null;
        AVCodecContext* decoderContext = null;
        AVPacket* packet = null;
        AVFrame* decodedFrame = null;
        SwsContext* scaleContext = null;
        GCHandle interruptHandle = default;
        try
        {
            EnsureInitialized();
            ConfigureInterruptCallback(&inputContext, token, out interruptHandle);
            int openResult = ffmpeg.avformat_open_input(&inputContext, sourceFileName, null, null);
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

            int videoStreamIndex = FindCoverVideoStream(inputContext);
            if (videoStreamIndex < 0)
            {
                error = "video stream was not found";
                return false;
            }

            AVStream* videoStream = inputContext->streams[videoStreamIndex];
            AVCodec* decoder = ffmpeg.avcodec_find_decoder(videoStream->codecpar->codec_id);
            if (decoder == null)
            {
                error = "video decoder was not found";
                return false;
            }

            decoderContext = ffmpeg.avcodec_alloc_context3(decoder);
            if (decoderContext == null)
            {
                error = "video decoder context could not be allocated";
                return false;
            }
            ThrowIfError(ffmpeg.avcodec_parameters_to_context(decoderContext, videoStream->codecpar), "copy cover decoder parameters");
            ThrowIfError(ffmpeg.avcodec_open2(decoderContext, decoder, null), "open cover decoder");

            long targetTimestamp = positionSeconds <= 0
                ? 0
                : (long)Math.Round(positionSeconds / ffmpeg.av_q2d(videoStream->time_base));
            if (targetTimestamp > 0 && ffmpeg.av_seek_frame(inputContext, videoStreamIndex, targetTimestamp, ffmpeg.AVSEEK_FLAG_BACKWARD) >= 0)
            {
                ffmpeg.avcodec_flush_buffers(decoderContext);
            }

            packet = ffmpeg.av_packet_alloc();
            decodedFrame = ffmpeg.av_frame_alloc();
            if (packet == null || decodedFrame == null)
            {
                error = "cover frame buffers could not be allocated";
                return false;
            }

            int packetCount = 0;
            while (!token.IsCancellationRequested && packetCount++ < 6000)
            {
                int readResult = ffmpeg.av_read_frame(inputContext, packet);
                if (readResult < 0)
                {
                    error = ErrorToString(readResult);
                    break;
                }

                if (packet->stream_index == videoStreamIndex
                    && ffmpeg.avcodec_send_packet(decoderContext, packet) >= 0)
                {
                    while (ffmpeg.avcodec_receive_frame(decoderContext, decodedFrame) >= 0)
                    {
                        long timestamp = decodedFrame->best_effort_timestamp;
                        if (timestamp == ffmpeg.AV_NOPTS_VALUE || timestamp >= targetTimestamp)
                        {
                            frame = ConvertCoverFrame(decodedFrame, ref scaleContext);
                            return true;
                        }
                        ffmpeg.av_frame_unref(decodedFrame);
                    }
                }
                ffmpeg.av_packet_unref(packet);
            }

            token.ThrowIfCancellationRequested();
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            if (packet != null)
            {
                AVPacket* packetPointer = packet;
                ffmpeg.av_packet_free(&packetPointer);
            }
            if (decodedFrame != null)
            {
                AVFrame* framePointer = decodedFrame;
                ffmpeg.av_frame_free(&framePointer);
            }
            if (decoderContext != null)
            {
                AVCodecContext* decoderPointer = decoderContext;
                ffmpeg.avcodec_free_context(&decoderPointer);
            }
            if (scaleContext != null)
            {
                ffmpeg.sws_freeContext(scaleContext);
            }
            if (inputContext != null)
            {
                AVFormatContext* context = inputContext;
                ffmpeg.avformat_close_input(&context);
            }
            if (interruptHandle.IsAllocated)
            {
                interruptHandle.Free();
            }
        }
    }

    private static int FindCoverVideoStream(AVFormatContext* inputContext)
    {
        int preferredStreamIndex = ffmpeg.av_find_best_stream(
            inputContext,
            AVMediaType.AVMEDIA_TYPE_VIDEO,
            -1,
            -1,
            null,
            0);
        if (preferredStreamIndex >= 0
            && (inputContext->streams[preferredStreamIndex]->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
        {
            return preferredStreamIndex;
        }

        for (uint index = 0; index < inputContext->nb_streams; index++)
        {
            AVStream* stream = inputContext->streams[index];
            if (stream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO
                && (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
            {
                return (int)index;
            }
        }

        return -1;
    }

    private static BitmapSource ConvertCoverFrame(AVFrame* source, ref SwsContext* scaleContext)
    {
        int sourceWidth = source->width;
        int sourceHeight = source->height;
        double scale = Math.Min(1d, Math.Min(960d / sourceWidth, 640d / sourceHeight));
        int width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        int height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];

        scaleContext = ffmpeg.sws_getCachedContext(
            scaleContext,
            sourceWidth,
            sourceHeight,
            (AVPixelFormat)source->format,
            width,
            height,
            AVPixelFormat.AV_PIX_FMT_BGRA,
            ffmpeg.SWS_BILINEAR,
            null,
            null,
            null);
        if (scaleContext == null)
        {
            throw new InvalidOperationException("cover frame scaler could not be created");
        }

        fixed (byte* destinationPointer = pixels)
        {
            byte_ptrArray4 destinationData = default;
            int_array4 destinationLines = default;
            destinationData[0] = destinationPointer;
            destinationLines[0] = stride;
            int scaled = ffmpeg.sws_scale(
                scaleContext,
                source->data,
                source->linesize,
                0,
                sourceHeight,
                destinationData,
                destinationLines);
            if (scaled <= 0)
            {
                throw new InvalidOperationException("cover frame could not be scaled");
            }
        }

        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}
