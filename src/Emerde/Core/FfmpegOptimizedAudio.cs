using FFmpeg.AutoGen;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Emerde.Core;

internal static unsafe partial class FfmpegMediaEngine
{
    private const int OptimizedAudioSampleRate = 48000;
    private const int OptimizedAudioBitrate = 128000;
    private const int AvErrorAgain = -11;
    private const int MaximumContinuousSilenceSeconds = 10;

    public static FfmpegMediaRunResult RemuxFilesWithOptimizedAudio(
        IReadOnlyList<string> sourceFileNames,
        string targetFileName,
        VideoRecordingMetadata metadata,
        CancellationToken token,
        bool parallelizePreparation = false,
        IReadOnlyList<double>? sourceTimelineEndSeconds = null)
    {
        string baseVideoPath = BuildOptimizedAudioTemporaryPath(targetFileName, "base", ".mp4");
        string optimizedAudioPath = BuildOptimizedAudioTemporaryPath(targetFileName, "audio", ".m4a");
        Stopwatch totalTimer = Stopwatch.StartNew();
        long audioMilliseconds = 0;
        long baseMilliseconds = 0;
        long muxMilliseconds = 0;
        bool preparationWasParallel = parallelizePreparation && sourceFileNames.Count > 1;
        FfmpegMediaRunResult? finalResult = null;
        try
        {
            FfmpegMediaRunResult audioResult;
            if (preparationWasParallel)
            {
                OptimizedAudioPreparationResult preparation = RunParallelPreparation(
                    sourceFileNames,
                    baseVideoPath,
                    optimizedAudioPath,
                    metadata,
                    token,
                    sourceTimelineEndSeconds);
                audioResult = preparation.AudioResult;
                audioMilliseconds = preparation.AudioMilliseconds;
                baseMilliseconds = preparation.BaseMilliseconds;
                if (preparation.Failure != null)
                {
                    return finalResult = preparation.Failure;
                }
            }
            else
            {
                (audioResult, audioMilliseconds) = RunMeasured(
                    () => EncodeOptimizedAudio(sourceFileNames, optimizedAudioPath, token, sourceTimelineEndSeconds));
                if (!IsSuccessfulPreparation(audioResult))
                {
                    return finalResult = audioResult;
                }
            }

            if (!preparationWasParallel)
            {
                if (sourceFileNames.Count > 1)
                {
                    (FfmpegMediaRunResult baseResult, long elapsedMilliseconds) = RunMeasured(
                        () => RemuxFiles(
                            sourceFileNames,
                            baseVideoPath,
                            metadata,
                            token,
                            sourceTimelineEndSeconds: sourceTimelineEndSeconds));
                    baseMilliseconds = elapsedMilliseconds;
                    if (!IsSuccessfulPreparation(baseResult))
                    {
                        return finalResult = baseResult;
                    }
                }
            }

            string muxBasePath = sourceFileNames.Count == 1 ? sourceFileNames[0] : baseVideoPath;
            (finalResult, muxMilliseconds) = RunMeasured(
                () => MuxAdditionalAudio(muxBasePath, optimizedAudioPath, targetFileName, metadata, token));
            return finalResult;
        }
        finally
        {
            totalTimer.Stop();
            DeleteOptimizedAudioTemporaryFile(baseVideoPath);
            DeleteOptimizedAudioTemporaryFile(optimizedAudioPath);
            AppSessionLogger.Event("info", "converter", "optimized_audio_timing", "optimized audio conversion stages completed", new
            {
                sourceCount = sourceFileNames.Count,
                preparationWasParallel,
                audioMilliseconds,
                baseMilliseconds,
                muxMilliseconds,
                totalMilliseconds = totalTimer.ElapsedMilliseconds,
                exitCode = finalResult?.ExitCode,
                wasCanceled = finalResult?.WasCanceled,
                succeeded = finalResult != null && IsSuccessfulPreparation(finalResult),
            });
        }
    }

    private static OptimizedAudioPreparationResult RunParallelPreparation(
        IReadOnlyList<string> sourceFileNames,
        string baseVideoPath,
        string optimizedAudioPath,
        VideoRecordingMetadata metadata,
        CancellationToken token,
        IReadOnlyList<double>? sourceTimelineEndSeconds)
    {
        using CancellationTokenSource preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<(FfmpegMediaRunResult Result, long ElapsedMilliseconds)> audioTask = Task.Run(
            () => RunMeasured(() => EncodeOptimizedAudio(
                sourceFileNames,
                optimizedAudioPath,
                preparationCancellation.Token,
                sourceTimelineEndSeconds)));
        Task<(FfmpegMediaRunResult Result, long ElapsedMilliseconds)> baseTask = Task.Run(
            () => RunMeasured(() => RemuxFiles(
                sourceFileNames,
                baseVideoPath,
                metadata,
                preparationCancellation.Token,
                sourceTimelineEndSeconds: sourceTimelineEndSeconds)));
        try
        {
            Task<(FfmpegMediaRunResult Result, long ElapsedMilliseconds)> firstTask = Task.WhenAny(audioTask, baseTask).GetAwaiter().GetResult();
            (FfmpegMediaRunResult Result, long ElapsedMilliseconds) first = firstTask.GetAwaiter().GetResult();
            FfmpegMediaRunResult? firstFailure = IsSuccessfulPreparation(first.Result) ? null : first.Result;
            if (firstFailure != null)
            {
                preparationCancellation.Cancel();
            }

            (FfmpegMediaRunResult AudioResult, long AudioMilliseconds) audio = audioTask.GetAwaiter().GetResult();
            (FfmpegMediaRunResult BaseResult, long BaseMilliseconds) baseVideo = baseTask.GetAwaiter().GetResult();
            FfmpegMediaRunResult? failure = firstFailure
                ?? (IsSuccessfulPreparation(audio.AudioResult) ? null : audio.AudioResult)
                ?? (IsSuccessfulPreparation(baseVideo.BaseResult) ? null : baseVideo.BaseResult);
            return new OptimizedAudioPreparationResult(
                audio.AudioResult,
                baseVideo.BaseResult,
                audio.AudioMilliseconds,
                baseVideo.BaseMilliseconds,
                failure);
        }
        catch (Exception e)
        {
            preparationCancellation.Cancel();
            try
            {
                Task.WaitAll([audioTask, baseTask]);
            }
            catch
            {
            }
            return new OptimizedAudioPreparationResult(
                new FfmpegMediaRunResult(1, token.IsCancellationRequested, false, e.ToString()),
                new FfmpegMediaRunResult(1, token.IsCancellationRequested, false, e.ToString()),
                GetElapsedMilliseconds(audioTask),
                GetElapsedMilliseconds(baseTask),
                new FfmpegMediaRunResult(1, token.IsCancellationRequested, false, e.ToString()));
        }
    }

    private static long GetElapsedMilliseconds(
        Task<(FfmpegMediaRunResult Result, long ElapsedMilliseconds)> task)
    {
        return task.Status == TaskStatus.RanToCompletion ? task.Result.ElapsedMilliseconds : 0;
    }

    private static (FfmpegMediaRunResult Result, long ElapsedMilliseconds) RunMeasured(
        Func<FfmpegMediaRunResult> operation)
    {
        Stopwatch timer = Stopwatch.StartNew();
        FfmpegMediaRunResult result = operation();
        timer.Stop();
        return (result, timer.ElapsedMilliseconds);
    }

    private static bool IsSuccessfulPreparation(FfmpegMediaRunResult result)
    {
        return result.ExitCode == 0 && !result.WasCanceled && result.HadMediaProgress;
    }

    private sealed record OptimizedAudioPreparationResult(
        FfmpegMediaRunResult AudioResult,
        FfmpegMediaRunResult BaseResult,
        long AudioMilliseconds,
        long BaseMilliseconds,
        FfmpegMediaRunResult? Failure);

    private static FfmpegMediaRunResult EncodeOptimizedAudio(
        IReadOnlyList<string> sourceFileNames,
        string targetFileName,
        CancellationToken token,
        IReadOnlyList<double>? sourceTimelineEndSeconds)
    {
        AVFormatContext* outputContext = null;
        AVCodecContext* encoderContext = null;
        AVAudioFifo* audioFifo = null;
        AVPacket* encodedPacket = null;
        bool outputOpened = false;
        bool headerWritten = false;
        bool hadProgress = false;
        long nextPts = 0;

        try
        {
            EnsureInitialized();
            AVCodec* encoder = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
            if (encoder == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "AAC encoder is unavailable");
            }

            ThrowIfError(ffmpeg.avformat_alloc_output_context2(&outputContext, null, "mp4", targetFileName), "create optimized audio output");
            if (outputContext == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "optimized audio output context could not be created");
            }

            encoderContext = ffmpeg.avcodec_alloc_context3(encoder);
            if (encoderContext == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "AAC encoder context allocation failed");
            }

            encoderContext->sample_rate = OptimizedAudioSampleRate;
            encoderContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            encoderContext->bit_rate = OptimizedAudioBitrate;
            encoderContext->time_base = new AVRational { num = 1, den = OptimizedAudioSampleRate };
            ffmpeg.av_channel_layout_default(&encoderContext->ch_layout, 2);
            if ((outputContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                encoderContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }
            ThrowIfError(ffmpeg.avcodec_open2(encoderContext, encoder, null), "open AAC encoder");

            AVStream* outputStream = ffmpeg.avformat_new_stream(outputContext, null);
            if (outputStream == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "optimized audio stream allocation failed");
            }
            outputStream->time_base = encoderContext->time_base;
            ThrowIfError(ffmpeg.avcodec_parameters_from_context(outputStream->codecpar, encoderContext), "copy AAC parameters");
            string optimizedAudioTrack = "OptimizedAudioTrack".Tr();
            ffmpeg.av_dict_set(&outputStream->metadata, "title", optimizedAudioTrack, 0);
            ffmpeg.av_dict_set(&outputStream->metadata, "handler_name", optimizedAudioTrack, 0);

            if ((outputContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                ThrowIfError(ffmpeg.avio_open(&outputContext->pb, targetFileName, ffmpeg.AVIO_FLAG_WRITE), "open optimized audio output");
                outputOpened = true;
            }
            ThrowIfError(ffmpeg.avformat_write_header(outputContext, null), "write optimized audio header");
            headerWritten = true;

            audioFifo = ffmpeg.av_audio_fifo_alloc(encoderContext->sample_fmt, encoderContext->ch_layout.nb_channels, Math.Max(1, encoderContext->frame_size));
            encodedPacket = ffmpeg.av_packet_alloc();
            if (audioFifo == null || encodedPacket == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "optimized audio buffer allocation failed");
            }
            AudioDynamicsProcessor dynamicsProcessor = new(encoderContext->sample_rate);

            for (int sourceIndex = 0; sourceIndex < sourceFileNames.Count; sourceIndex++)
            {
                FfmpegMediaRunResult sourceResult = DecodeSourceAudioToFifo(
                    sourceFileNames[sourceIndex],
                    encoderContext,
                    outputContext,
                    outputStream,
                    audioFifo,
                    encodedPacket,
                    dynamicsProcessor,
                    ref nextPts,
                    ref hadProgress,
                    token,
                    sourceTimelineEndSeconds,
                    sourceIndex);
                if (sourceResult.ExitCode != 0 || sourceResult.WasCanceled)
                {
                    return sourceResult with { HadMediaProgress = hadProgress || sourceResult.HadMediaProgress };
                }
            }

            WriteRemainingAudioFifo(
                encoderContext,
                outputContext,
                outputStream,
                audioFifo,
                encodedPacket,
                ref nextPts,
                ref hadProgress);
            ThrowIfError(ffmpeg.avcodec_send_frame(encoderContext, null), "flush AAC encoder");
            DrainEncodedAudio(encoderContext, outputContext, outputStream, encodedPacket, ref hadProgress);

            int closeResult = CloseSegmentOutput(&outputContext, ref outputOpened, ref headerWritten);
            return closeResult < 0
                ? CreateNativeFailureResult(closeResult, token, hadProgress)
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
            if (encodedPacket != null)
            {
                AVPacket* packet = encodedPacket;
                ffmpeg.av_packet_free(&packet);
            }
            if (audioFifo != null)
            {
                ffmpeg.av_audio_fifo_free(audioFifo);
            }
            if (encoderContext != null)
            {
                AVCodecContext* context = encoderContext;
                ffmpeg.avcodec_free_context(&context);
            }
            _ = CloseSegmentOutput(&outputContext, ref outputOpened, ref headerWritten);
        }
    }

    private static FfmpegMediaRunResult DecodeSourceAudioToFifo(
        string sourceFileName,
        AVCodecContext* encoderContext,
        AVFormatContext* outputContext,
        AVStream* outputStream,
        AVAudioFifo* audioFifo,
        AVPacket* encodedPacket,
        AudioDynamicsProcessor dynamicsProcessor,
        ref long nextPts,
        ref bool hadProgress,
        CancellationToken token,
        IReadOnlyList<double>? sourceTimelineEndSeconds,
        int sourceIndex)
    {
        AVFormatContext* inputContext = null;
        AVCodecContext* decoderContext = null;
        SwrContext* resampleContext = null;
        AVPacket* inputPacket = null;
        AVFrame* decodedFrame = null;
        AVDictionary* options = null;
        GCHandle interruptHandle = default;

        try
        {
            ConfigureInterruptCallback(&inputContext, token, out interruptHandle);
            AddInputOptions(&options, new FfmpegInputOptions(string.Empty, string.Empty, false, string.Empty, false));
            ThrowIfError(ffmpeg.avformat_open_input(&inputContext, sourceFileName, null, &options), "open optimized audio source");
            ApplyInputRepairPolicy(inputContext);
            ThrowIfError(ffmpeg.avformat_find_stream_info(inputContext, null), "find optimized audio stream info");

            int audioStreamIndex = ffmpeg.av_find_best_stream(inputContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            if (audioStreamIndex < 0)
            {
                return new FfmpegMediaRunResult(audioStreamIndex, false, false, "input contains no audio stream");
            }

            AVStream* inputStream = inputContext->streams[audioStreamIndex];
            AVCodec* decoder = ffmpeg.avcodec_find_decoder(inputStream->codecpar->codec_id);
            if (decoder == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "audio decoder is unavailable");
            }
            decoderContext = ffmpeg.avcodec_alloc_context3(decoder);
            if (decoderContext == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "audio decoder context allocation failed");
            }
            ThrowIfError(ffmpeg.avcodec_parameters_to_context(decoderContext, inputStream->codecpar), "copy audio decoder parameters");
            decoderContext->pkt_timebase = inputStream->time_base;
            ThrowIfError(ffmpeg.avcodec_open2(decoderContext, decoder, null), "open audio decoder");

            ThrowIfError(ffmpeg.swr_alloc_set_opts2(
                &resampleContext,
                &encoderContext->ch_layout,
                encoderContext->sample_fmt,
                encoderContext->sample_rate,
                &decoderContext->ch_layout,
                decoderContext->sample_fmt,
                decoderContext->sample_rate,
                0,
                null), "configure audio resampler");
            ThrowIfError(ffmpeg.swr_init(resampleContext), "initialize audio resampler");

            inputPacket = ffmpeg.av_packet_alloc();
            decodedFrame = ffmpeg.av_frame_alloc();
            if (inputPacket == null || decodedFrame == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "audio decode buffer allocation failed");
            }
            long sourceOutputStartPts = nextPts + ffmpeg.av_audio_fifo_size(audioFifo);
            long sourceTimestampBaseUs = inputContext->start_time;
            double sourceTimelineLimitSeconds = GetSourceTimelineEndSeconds(
                0d,
                sourceTimelineEndSeconds,
                sourceIndex,
                inputContext);
            long sourceDurationUs = 0;
            long[] lastPacketEnds = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)inputContext->nb_streams).ToArray();
            int referenceStreamIndex = GetSegmentReferenceStreamIndex(inputContext);
            SegmentClock sourceClock = new();
            bool awaitingVideoKeyframe = false;

            while (!token.IsCancellationRequested)
            {
                int readResult = ffmpeg.av_read_frame(inputContext, inputPacket);
                if (readResult < 0)
                {
                    if (readResult != ffmpeg.AVERROR_EOF)
                    {
                        return CreateNativeFailureResult(readResult, token, hadProgress);
                    }
                    break;
                }

                int packetStreamIndex = inputPacket->stream_index;
                if (packetStreamIndex >= 0 && packetStreamIndex < inputContext->nb_streams)
                {
                    AVStream* packetStream = inputContext->streams[packetStreamIndex];
                    AVMediaType mediaType = packetStream->codecpar->codec_type;
                    if (mediaType is AVMediaType.AVMEDIA_TYPE_AUDIO or AVMediaType.AVMEDIA_TYPE_VIDEO)
                    {
                        if (packetStreamIndex == referenceStreamIndex)
                        {
                            _ = sourceClock.Observe(inputPacket, packetStream);
                            if (mediaType == AVMediaType.AVMEDIA_TYPE_VIDEO
                                && sourceClock.LastObservationWasDiscontinuity)
                            {
                                awaitingVideoKeyframe = true;
                            }
                        }
                        if (ShouldDiscardPacket(
                            inputPacket,
                            mediaType,
                            packetStreamIndex == referenceStreamIndex,
                            ref awaitingVideoKeyframe))
                        {
                            ffmpeg.av_packet_unref(inputPacket);
                            continue;
                        }
                        ApplyPacketTimestampCorrection(inputPacket, packetStream, sourceClock.CurrentCorrection);
                        NormalizePacketDts(inputPacket, packetStream, packetStreamIndex, lastPacketEnds);
                        if (sourceTimelineLimitSeconds > 0d
                            && PacketExceedsSourceTimelineLimit(
                                inputPacket,
                                packetStream,
                                sourceTimestampBaseUs,
                                sourceTimelineLimitSeconds))
                        {
                            ffmpeg.av_packet_unref(inputPacket);
                            continue;
                        }
                    }
                }
                UpdateSourceTimeline(inputContext, inputPacket, ref sourceTimestampBaseUs, ref sourceDurationUs);

                if (inputPacket->stream_index == audioStreamIndex)
                {
                    ThrowIfError(ffmpeg.avcodec_send_packet(decoderContext, inputPacket), "send audio packet");
                    DecodeAvailableAudioFrames(
                        decoderContext,
                        resampleContext,
                        decodedFrame,
                        encoderContext,
                        outputContext,
                        outputStream,
                        audioFifo,
                        encodedPacket,
                        dynamicsProcessor,
                        inputStream->time_base,
                        sourceTimestampBaseUs,
                        sourceOutputStartPts,
                        ref nextPts,
                        ref hadProgress,
                        token);
                }
                ffmpeg.av_packet_unref(inputPacket);
            }

            if (token.IsCancellationRequested)
            {
                return CreateCanceledResult(hadProgress);
            }
            if (!IsFileInputFullyConsumed(inputContext, sourceFileName))
            {
                return new FfmpegMediaRunResult(1, false, hadProgress, "optimized audio source ended before the physical file end");
            }

            ThrowIfError(ffmpeg.avcodec_send_packet(decoderContext, null), "flush audio decoder");
            DecodeAvailableAudioFrames(
                decoderContext,
                resampleContext,
                decodedFrame,
                encoderContext,
                outputContext,
                outputStream,
                audioFifo,
                encodedPacket,
                dynamicsProcessor,
                inputStream->time_base,
                sourceTimestampBaseUs,
                sourceOutputStartPts,
                ref nextPts,
                ref hadProgress,
                token);
            FlushResamplerToFifo(resampleContext, encoderContext, audioFifo, dynamicsProcessor);
            PadAudioFifoToSourceEnd(
                audioFifo,
                encoderContext,
                sourceOutputStartPts,
                sourceDurationUs,
                dynamicsProcessor,
                outputContext,
                outputStream,
                encodedPacket,
                ref nextPts,
                ref hadProgress,
                token);
            EncodeAvailableAudioFifo(
                encoderContext,
                outputContext,
                outputStream,
                audioFifo,
                encodedPacket,
                ref nextPts,
                ref hadProgress);
            return new FfmpegMediaRunResult(0, false, hadProgress, string.Empty);
        }
        catch (Exception e)
        {
            return token.IsCancellationRequested
                ? CreateCanceledResult(hadProgress)
                : new FfmpegMediaRunResult(1, false, hadProgress, e.ToString());
        }
        finally
        {
            if (decodedFrame != null)
            {
                AVFrame* frame = decodedFrame;
                ffmpeg.av_frame_free(&frame);
            }
            if (inputPacket != null)
            {
                AVPacket* packet = inputPacket;
                ffmpeg.av_packet_free(&packet);
            }
            if (resampleContext != null)
            {
                SwrContext* context = resampleContext;
                ffmpeg.swr_free(&context);
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

    private static void DecodeAvailableAudioFrames(
        AVCodecContext* decoderContext,
        SwrContext* resampleContext,
        AVFrame* decodedFrame,
        AVCodecContext* encoderContext,
        AVFormatContext* outputContext,
        AVStream* outputStream,
        AVAudioFifo* audioFifo,
        AVPacket* encodedPacket,
        AudioDynamicsProcessor dynamicsProcessor,
        AVRational inputTimeBase,
        long sourceTimestampBaseUs,
        long sourceOutputStartPts,
        ref long nextPts,
        ref bool hadProgress,
        CancellationToken token)
    {
        while (true)
        {
            int receiveResult = ffmpeg.avcodec_receive_frame(decoderContext, decodedFrame);
            if (receiveResult == AvErrorAgain || receiveResult == ffmpeg.AVERROR_EOF)
            {
                return;
            }
            ThrowIfError(receiveResult, "receive decoded audio frame");
            AlignAudioFrameTimeline(
                decodedFrame,
                resampleContext,
                encoderContext,
                audioFifo,
                inputTimeBase,
                sourceTimestampBaseUs,
                sourceOutputStartPts,
                dynamicsProcessor,
                outputContext,
                outputStream,
                encodedPacket,
                ref nextPts,
                ref hadProgress,
                token);
            ConvertAudioFrameToFifo(decodedFrame, resampleContext, encoderContext, audioFifo, dynamicsProcessor);
            ffmpeg.av_frame_unref(decodedFrame);
            EncodeAvailableAudioFifo(
                encoderContext,
                outputContext,
                outputStream,
                audioFifo,
                encodedPacket,
                ref nextPts,
                ref hadProgress);
        }
    }

    private static void UpdateSourceTimeline(
        AVFormatContext* inputContext,
        AVPacket* packet,
        ref long sourceTimestampBaseUs,
        ref long sourceDurationUs)
    {
        int streamIndex = packet->stream_index;
        if (streamIndex < 0 || streamIndex >= inputContext->nb_streams)
        {
            return;
        }
        AVStream* stream = inputContext->streams[streamIndex];
        if (stream->codecpar->codec_type is not AVMediaType.AVMEDIA_TYPE_AUDIO and not AVMediaType.AVMEDIA_TYPE_VIDEO)
        {
            return;
        }
        long timestamp = packet->dts != ffmpeg.AV_NOPTS_VALUE ? packet->dts : packet->pts;
        if (timestamp == ffmpeg.AV_NOPTS_VALUE)
        {
            return;
        }
        long timestampUs = ffmpeg.av_rescale_q(
            timestamp,
            stream->time_base,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE });
        if (sourceTimestampBaseUs == ffmpeg.AV_NOPTS_VALUE)
        {
            sourceTimestampBaseUs = timestampUs;
        }
        long durationUs = packet->duration > 0
            ? ffmpeg.av_rescale_q(
                packet->duration,
                stream->time_base,
                new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE })
            : 0;
        long relativeStartUs = Math.Max(0, SubtractSaturated(timestampUs, sourceTimestampBaseUs));
        long relativeEndUs = AddSaturated(relativeStartUs, Math.Max(0, durationUs));
        sourceDurationUs = Math.Max(sourceDurationUs, relativeEndUs);
    }

    private static void AlignAudioFrameTimeline(
        AVFrame* decodedFrame,
        SwrContext* resampleContext,
        AVCodecContext* encoderContext,
        AVAudioFifo* audioFifo,
        AVRational inputTimeBase,
        long sourceTimestampBaseUs,
        long sourceOutputStartPts,
        AudioDynamicsProcessor dynamicsProcessor,
        AVFormatContext* outputContext,
        AVStream* outputStream,
        AVPacket* encodedPacket,
        ref long nextPts,
        ref bool hadProgress,
        CancellationToken token)
    {
        long frameTimestamp = decodedFrame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE
            ? decodedFrame->best_effort_timestamp
            : decodedFrame->pts;
        if (frameTimestamp == ffmpeg.AV_NOPTS_VALUE || sourceTimestampBaseUs == ffmpeg.AV_NOPTS_VALUE)
        {
            return;
        }
        long frameTimestampUs = ffmpeg.av_rescale_q(
            frameTimestamp,
            inputTimeBase,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE });
        long desiredPts = AddSaturated(sourceOutputStartPts, ffmpeg.av_rescale_q(
            Math.Max(0, SubtractSaturated(frameTimestampUs, sourceTimestampBaseUs)),
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
            encoderContext->time_base));
        long pendingResamplerSamples = ffmpeg.swr_get_delay(resampleContext, encoderContext->sample_rate);
        long currentPts = AddSaturated(
            AddSaturated(nextPts, ffmpeg.av_audio_fifo_size(audioFifo)),
            Math.Max(0, pendingResamplerSamples));
        long gapSamples = SubtractSaturated(desiredPts, currentPts);
        if (gapSamples > 1)
        {
            if (gapSamples > (long)encoderContext->sample_rate * MaximumContinuousSilenceSeconds)
            {
                token.ThrowIfCancellationRequested();
                FlushResamplerToFifo(resampleContext, encoderContext, audioFifo, dynamicsProcessor);
                WriteRemainingAudioFifo(
                    encoderContext,
                    outputContext,
                    outputStream,
                    audioFifo,
                    encodedPacket,
                    ref nextPts,
                    ref hadProgress);
                gapSamples = SubtractSaturated(desiredPts, nextPts);
                if (gapSamples > 1)
                {
                    nextPts = AddSaturated(nextPts, gapSamples);
                }
                return;
            }
            WriteSilenceToFifo(
                audioFifo,
                encoderContext,
                gapSamples,
                dynamicsProcessor,
                outputContext,
                outputStream,
                encodedPacket,
                ref nextPts,
                ref hadProgress,
                token);
        }
    }

    private static void PadAudioFifoToSourceEnd(
        AVAudioFifo* audioFifo,
        AVCodecContext* encoderContext,
        long sourceOutputStartPts,
        long sourceDurationUs,
        AudioDynamicsProcessor dynamicsProcessor,
        AVFormatContext* outputContext,
        AVStream* outputStream,
        AVPacket* encodedPacket,
        ref long nextPts,
        ref bool hadProgress,
        CancellationToken token)
    {
        if (sourceDurationUs <= 0)
        {
            return;
        }
        long expectedEndPts = AddSaturated(sourceOutputStartPts, ffmpeg.av_rescale_q(
            sourceDurationUs,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
            encoderContext->time_base));
        long currentEndPts = AddSaturated(nextPts, ffmpeg.av_audio_fifo_size(audioFifo));
        long gapSamples = SubtractSaturated(expectedEndPts, currentEndPts);
        if (gapSamples > 0)
        {
            if (gapSamples > (long)encoderContext->sample_rate * MaximumContinuousSilenceSeconds)
            {
                token.ThrowIfCancellationRequested();
                WriteRemainingAudioFifo(
                    encoderContext,
                    outputContext,
                    outputStream,
                    audioFifo,
                    encodedPacket,
                    ref nextPts,
                    ref hadProgress);
                gapSamples = SubtractSaturated(expectedEndPts, nextPts);
                int anchorSamples = (int)Math.Min(gapSamples, Math.Max(1, encoderContext->frame_size));
                if (gapSamples > anchorSamples)
                {
                    nextPts = AddSaturated(nextPts, gapSamples - anchorSamples);
                }
                if (anchorSamples > 0)
                {
                    WriteSilenceToFifo(
                        audioFifo,
                        encoderContext,
                        anchorSamples,
                        dynamicsProcessor,
                        outputContext,
                        outputStream,
                        encodedPacket,
                        ref nextPts,
                        ref hadProgress,
                        token);
                }
                return;
            }
            WriteSilenceToFifo(
                audioFifo,
                encoderContext,
                gapSamples,
                dynamicsProcessor,
                outputContext,
                outputStream,
                encodedPacket,
                ref nextPts,
                ref hadProgress,
                token);
        }
    }

    private static void WriteSilenceToFifo(
        AVAudioFifo* audioFifo,
        AVCodecContext* encoderContext,
        long sampleCount,
        AudioDynamicsProcessor dynamicsProcessor,
        AVFormatContext* outputContext,
        AVStream* outputStream,
        AVPacket* encodedPacket,
        ref long nextPts,
        ref bool hadProgress,
        CancellationToken token)
    {
        int maximumChunk = Math.Max(encoderContext->frame_size, encoderContext->sample_rate);
        while (sampleCount > 0)
        {
            token.ThrowIfCancellationRequested();
            int chunk = (int)Math.Min(sampleCount, maximumChunk);
            AVFrame* silenceFrame = AllocateAudioFrame(encoderContext, chunk);
            try
            {
                ThrowIfError(ffmpeg.av_samples_set_silence(
                    silenceFrame->extended_data,
                    0,
                    chunk,
                    encoderContext->ch_layout.nb_channels,
                    encoderContext->sample_fmt), "create optimized audio silence");
                dynamicsProcessor.Process(silenceFrame, chunk);
                WriteAudioFrameToFifo(audioFifo, silenceFrame, chunk);
                EncodeAvailableAudioFifo(
                    encoderContext,
                    outputContext,
                    outputStream,
                    audioFifo,
                    encodedPacket,
                    ref nextPts,
                    ref hadProgress);
            }
            finally
            {
                ffmpeg.av_frame_free(&silenceFrame);
            }
            sampleCount -= chunk;
        }
    }

    private static void ConvertAudioFrameToFifo(
        AVFrame* decodedFrame,
        SwrContext* resampleContext,
        AVCodecContext* encoderContext,
        AVAudioFifo* audioFifo,
        AudioDynamicsProcessor dynamicsProcessor)
    {
        int outputCapacity = (int)ffmpeg.av_rescale_rnd(
            ffmpeg.swr_get_delay(resampleContext, decodedFrame->sample_rate) + decodedFrame->nb_samples,
            encoderContext->sample_rate,
            decodedFrame->sample_rate,
            AVRounding.AV_ROUND_UP);
        AVFrame* convertedFrame = AllocateAudioFrame(encoderContext, outputCapacity);
        try
        {
            int convertedSamples = ffmpeg.swr_convert(
                resampleContext,
                convertedFrame->extended_data,
                outputCapacity,
                decodedFrame->extended_data,
                decodedFrame->nb_samples);
            ThrowIfError(convertedSamples, "resample audio frame");
            if (convertedSamples == 0)
            {
                return;
            }
            dynamicsProcessor.Process(convertedFrame, convertedSamples);
            WriteAudioFrameToFifo(audioFifo, convertedFrame, convertedSamples);
        }
        finally
        {
            ffmpeg.av_frame_free(&convertedFrame);
        }
    }

    private static void FlushResamplerToFifo(
        SwrContext* resampleContext,
        AVCodecContext* encoderContext,
        AVAudioFifo* audioFifo,
        AudioDynamicsProcessor dynamicsProcessor)
    {
        while (true)
        {
            int delayedSamples = (int)ffmpeg.av_rescale_rnd(
                ffmpeg.swr_get_delay(resampleContext, encoderContext->sample_rate),
                encoderContext->sample_rate,
                encoderContext->sample_rate,
                AVRounding.AV_ROUND_UP);
            if (delayedSamples <= 0)
            {
                return;
            }
            AVFrame* convertedFrame = AllocateAudioFrame(encoderContext, delayedSamples);
            try
            {
                int convertedSamples = ffmpeg.swr_convert(resampleContext, convertedFrame->extended_data, delayedSamples, null, 0);
                ThrowIfError(convertedSamples, "flush audio resampler");
                if (convertedSamples <= 0)
                {
                    return;
                }
                dynamicsProcessor.Process(convertedFrame, convertedSamples);
                WriteAudioFrameToFifo(audioFifo, convertedFrame, convertedSamples);
            }
            finally
            {
                ffmpeg.av_frame_free(&convertedFrame);
            }
        }
    }

    private static AVFrame* AllocateAudioFrame(AVCodecContext* encoderContext, int sampleCount)
    {
        AVFrame* frame = ffmpeg.av_frame_alloc();
        if (frame == null)
        {
            throw new InvalidOperationException("audio frame allocation failed");
        }
        try
        {
            frame->nb_samples = sampleCount;
            frame->format = (int)encoderContext->sample_fmt;
            frame->sample_rate = encoderContext->sample_rate;
            ThrowIfError(ffmpeg.av_channel_layout_copy(&frame->ch_layout, &encoderContext->ch_layout), "copy audio channel layout");
            ThrowIfError(ffmpeg.av_frame_get_buffer(frame, 0), "allocate audio frame buffer");
            return frame;
        }
        catch
        {
            ffmpeg.av_frame_free(&frame);
            throw;
        }
    }

    internal sealed class AudioDynamicsProcessor
    {
        private const double ThresholdDb = -18d;
        private const double Ratio = 3d;
        private const double MakeupGainDb = 6d;
        private const double CeilingDb = -1d;
        private readonly double attackCoefficient;
        private readonly double releaseCoefficient;
        private double envelope;

        public AudioDynamicsProcessor(int sampleRate)
        {
            int normalizedSampleRate = Math.Max(1, sampleRate);
            attackCoefficient = Math.Exp(-1d / (0.01d * normalizedSampleRate));
            releaseCoefficient = Math.Exp(-1d / (0.25d * normalizedSampleRate));
        }

        public void Process(AVFrame* frame, int sampleCount)
        {
            int channelCount = frame->ch_layout.nb_channels;
            double ceilingAmplitude = Math.Pow(10d, CeilingDb / 20d);
            for (int index = 0; index < sampleCount; index++)
            {
                double peak = 0;
                for (int channel = 0; channel < channelCount; channel++)
                {
                    peak = Math.Max(peak, Math.Abs(((float*)frame->extended_data[channel])[index]));
                }
                double coefficient = peak > envelope ? attackCoefficient : releaseCoefficient;
                envelope = coefficient * envelope + (1d - coefficient) * peak;
                double gain = CalculateLinearGain(envelope);
                for (int channel = 0; channel < channelCount; channel++)
                {
                    float* samples = (float*)frame->extended_data[channel];
                    samples[index] = (float)Math.Clamp(samples[index] * gain, -ceilingAmplitude, ceilingAmplitude);
                }
            }
        }

        internal static double CalculateLinearGain(double envelope)
        {
            double inputDb = 20d * Math.Log10(Math.Max(envelope, 1e-9d));
            double reductionDb = inputDb > ThresholdDb
                ? ThresholdDb + (inputDb - ThresholdDb) / Ratio - inputDb
                : 0d;
            double gainDb = Math.Min(MakeupGainDb + reductionDb, CeilingDb - inputDb);
            return Math.Pow(10d, gainDb / 20d);
        }
    }

    private static void WriteAudioFrameToFifo(AVAudioFifo* audioFifo, AVFrame* frame, int sampleCount)
    {
        int currentSize = ffmpeg.av_audio_fifo_size(audioFifo);
        ThrowIfError(ffmpeg.av_audio_fifo_realloc(audioFifo, currentSize + sampleCount), "grow optimized audio fifo");
        int written = ffmpeg.av_audio_fifo_write(audioFifo, (void**)frame->extended_data, sampleCount);
        if (written != sampleCount)
        {
            throw new InvalidOperationException("optimized audio fifo write was incomplete");
        }
    }

    private static void EncodeAvailableAudioFifo(
        AVCodecContext* encoderContext,
        AVFormatContext* outputContext,
        AVStream* outputStream,
        AVAudioFifo* audioFifo,
        AVPacket* encodedPacket,
        ref long nextPts,
        ref bool hadProgress)
    {
        int frameSize = Math.Max(1, encoderContext->frame_size);
        while (ffmpeg.av_audio_fifo_size(audioFifo) >= frameSize)
        {
            EncodeAudioFifoFrame(
                encoderContext,
                outputContext,
                outputStream,
                audioFifo,
                encodedPacket,
                frameSize,
                ref nextPts,
                ref hadProgress);
        }
    }

    private static void WriteRemainingAudioFifo(
        AVCodecContext* encoderContext,
        AVFormatContext* outputContext,
        AVStream* outputStream,
        AVAudioFifo* audioFifo,
        AVPacket* encodedPacket,
        ref long nextPts,
        ref bool hadProgress)
    {
        int remaining = ffmpeg.av_audio_fifo_size(audioFifo);
        if (remaining <= 0)
        {
            return;
        }
        int sampleCount = GetPaddedAudioFrameSampleCount(remaining, encoderContext->frame_size);
        EncodeAudioFifoFrame(
            encoderContext,
            outputContext,
            outputStream,
            audioFifo,
            encodedPacket,
            sampleCount,
            ref nextPts,
            ref hadProgress);
    }

    internal static int GetPaddedAudioFrameSampleCount(int remaining, int frameSize)
    {
        return remaining <= 0 ? 0 : Math.Max(remaining, frameSize);
    }

    private static void EncodeAudioFifoFrame(
        AVCodecContext* encoderContext,
        AVFormatContext* outputContext,
        AVStream* outputStream,
        AVAudioFifo* audioFifo,
        AVPacket* encodedPacket,
        int sampleCount,
        ref long nextPts,
        ref bool hadProgress)
    {
        AVFrame* frame = AllocateAudioFrame(encoderContext, sampleCount);
        try
        {
            int available = Math.Min(sampleCount, ffmpeg.av_audio_fifo_size(audioFifo));
            int read = ffmpeg.av_audio_fifo_read(audioFifo, (void**)frame->extended_data, available);
            if (read != available)
            {
                throw new InvalidOperationException("optimized audio fifo read was incomplete");
            }
            if (available < sampleCount)
            {
                ThrowIfError(ffmpeg.av_samples_set_silence(
                    frame->extended_data,
                    available,
                    sampleCount - available,
                    encoderContext->ch_layout.nb_channels,
                    encoderContext->sample_fmt), "pad optimized audio frame");
            }
            frame->pts = nextPts;
            nextPts += sampleCount;
            ThrowIfError(ffmpeg.avcodec_send_frame(encoderContext, frame), "send optimized audio frame");
            DrainEncodedAudio(encoderContext, outputContext, outputStream, encodedPacket, ref hadProgress);
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
        }
    }

    private static void DrainEncodedAudio(
        AVCodecContext* encoderContext,
        AVFormatContext* outputContext,
        AVStream* outputStream,
        AVPacket* encodedPacket,
        ref bool hadProgress)
    {
        while (true)
        {
            int receiveResult = ffmpeg.avcodec_receive_packet(encoderContext, encodedPacket);
            if (receiveResult == AvErrorAgain || receiveResult == ffmpeg.AVERROR_EOF)
            {
                return;
            }
            ThrowIfError(receiveResult, "receive encoded audio packet");
            ffmpeg.av_packet_rescale_ts(encodedPacket, encoderContext->time_base, outputStream->time_base);
            encodedPacket->stream_index = outputStream->index;
            encodedPacket->pos = -1;
            ThrowIfError(ffmpeg.av_interleaved_write_frame(outputContext, encodedPacket), "write optimized audio packet");
            ffmpeg.av_packet_unref(encodedPacket);
            hadProgress = true;
        }
    }

    private static FfmpegMediaRunResult MuxAdditionalAudio(
        string baseVideoPath,
        string optimizedAudioPath,
        string targetFileName,
        VideoRecordingMetadata metadata,
        CancellationToken token)
    {
        AVFormatContext* baseContext = null;
        AVFormatContext* audioContext = null;
        AVFormatContext* outputContext = null;
        AVPacket* basePacket = null;
        AVPacket* audioPacket = null;
        bool outputOpened = false;
        bool headerWritten = false;
        bool hadProgress = false;
        long baseTimelineEnd = 0;
        GCHandle baseInterruptHandle = default;
        GCHandle audioInterruptHandle = default;

        try
        {
            ConfigureInterruptCallback(&baseContext, token, out baseInterruptHandle);
            ThrowIfError(ffmpeg.avformat_open_input(&baseContext, baseVideoPath, null, null), "open base MP4");
            ApplyInputRepairPolicy(baseContext);
            ThrowIfError(ffmpeg.avformat_find_stream_info(baseContext, null), "find base MP4 streams");
            ConfigureInterruptCallback(&audioContext, token, out audioInterruptHandle);
            ThrowIfError(ffmpeg.avformat_open_input(&audioContext, optimizedAudioPath, null, null), "open optimized audio file");
            ApplyInputRepairPolicy(audioContext);
            ThrowIfError(ffmpeg.avformat_find_stream_info(audioContext, null), "find optimized audio stream");
            int audioStreamIndex = ffmpeg.av_find_best_stream(audioContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            ThrowIfError(audioStreamIndex, "find optimized audio track");

            ThrowIfError(ffmpeg.avformat_alloc_output_context2(&outputContext, null, "mp4", targetFileName), "create final MP4 output");
            AddMetadata(outputContext, metadata);
            outputContext->avoid_negative_ts = ffmpeg.AVFMT_AVOID_NEG_TS_MAKE_NON_NEGATIVE;
            int[] baseStreamMap = CreateOutputStreams(baseContext, outputContext);
            int originalAudioTrackNumber = 0;
            for (int index = 0; index < baseStreamMap.Length; index++)
            {
                int outputIndex = baseStreamMap[index];
                if (outputIndex >= 0
                    && baseContext->streams[index]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    AVStream* originalAudioStream = outputContext->streams[outputIndex];
                    originalAudioTrackNumber++;
                    string originalAudioTitle = originalAudioTrackNumber == 1
                        ? "OriginalAudioTrack".Tr()
                        : "OriginalAudioTrackNumbered".Tr(originalAudioTrackNumber);
                    ffmpeg.av_dict_set(&originalAudioStream->metadata, "title", originalAudioTitle, 0);
                    ffmpeg.av_dict_set(&originalAudioStream->metadata, "handler_name", originalAudioTitle, 0);
                    originalAudioStream->disposition &= ~ffmpeg.AV_DISPOSITION_DEFAULT;
                }
            }
            AVStream* optimizedInputStream = audioContext->streams[audioStreamIndex];
            AVStream* optimizedOutputStream = ffmpeg.avformat_new_stream(outputContext, null);
            if (optimizedOutputStream == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "final optimized audio stream allocation failed");
            }
            ThrowIfError(ffmpeg.avcodec_parameters_copy(optimizedOutputStream->codecpar, optimizedInputStream->codecpar), "copy optimized audio stream parameters");
            optimizedOutputStream->time_base = optimizedInputStream->time_base;
            optimizedOutputStream->codecpar->codec_tag = 0;
            string optimizedAudioTrack = "OptimizedAudioTrack".Tr();
            ffmpeg.av_dict_set(&optimizedOutputStream->metadata, "title", optimizedAudioTrack, 0);
            ffmpeg.av_dict_set(&optimizedOutputStream->metadata, "handler_name", optimizedAudioTrack, 0);
            optimizedOutputStream->disposition |= ffmpeg.AV_DISPOSITION_DEFAULT;

            if ((outputContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                ThrowIfError(ffmpeg.avio_open(&outputContext->pb, targetFileName, ffmpeg.AVIO_FLAG_WRITE), "open final MP4 output");
                outputOpened = true;
            }
            AVDictionary* writeOptions = null;
            ffmpeg.av_dict_set(&writeOptions, "movflags", "use_metadata_tags", 0);
            try
            {
                ThrowIfError(ffmpeg.avformat_write_header(outputContext, &writeOptions), "write final MP4 header");
                headerWritten = true;
            }
            finally
            {
                ffmpeg.av_dict_free(&writeOptions);
            }

            basePacket = ffmpeg.av_packet_alloc();
            audioPacket = ffmpeg.av_packet_alloc();
            if (basePacket == null || audioPacket == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "final MP4 packet allocation failed");
            }

            long sourceTimestampBase = baseContext->start_time;
            long[] nextInputDts = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)baseContext->nb_streams).ToArray();
            long[] lastBasePacketEnds = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)baseContext->nb_streams).ToArray();
            bool hasBasePacket = ReadNextMappedPacket(baseContext, basePacket, baseStreamMap, -1);
            if (hasBasePacket)
            {
                PrepareSourcePacket(baseContext, basePacket, nextInputDts, lastBasePacketEnds, ref sourceTimestampBase);
            }
            bool hasAudioPacket = ReadNextMappedPacket(audioContext, audioPacket, null, audioStreamIndex);
            while ((hasBasePacket || hasAudioPacket) && !token.IsCancellationRequested)
            {
                bool writeBase = hasBasePacket && (!hasAudioPacket || ffmpeg.av_compare_ts(
                    basePacket->dts,
                    baseContext->streams[basePacket->stream_index]->time_base,
                    audioPacket->dts,
                    optimizedInputStream->time_base) <= 0);
                if (writeBase)
                {
                    int inputIndex = basePacket->stream_index;
                    int outputIndex = baseStreamMap[inputIndex];
                    AVStream* inputStream = baseContext->streams[inputIndex];
                    AVStream* outputStream = outputContext->streams[outputIndex];
                    baseTimelineEnd = Math.Max(baseTimelineEnd, GetPacketDecodeEndTimestamp(basePacket, inputStream));
                    ffmpeg.av_packet_rescale_ts(basePacket, inputStream->time_base, outputStream->time_base);
                    basePacket->stream_index = outputIndex;
                    basePacket->pos = -1;
                    ThrowIfError(ffmpeg.av_interleaved_write_frame(outputContext, basePacket), "write base MP4 packet");
                    ffmpeg.av_packet_unref(basePacket);
                    hasBasePacket = ReadNextMappedPacket(baseContext, basePacket, baseStreamMap, -1);
                    if (hasBasePacket)
                    {
                        PrepareSourcePacket(baseContext, basePacket, nextInputDts, lastBasePacketEnds, ref sourceTimestampBase);
                    }
                }
                else
                {
                    ffmpeg.av_packet_rescale_ts(audioPacket, optimizedInputStream->time_base, optimizedOutputStream->time_base);
                    audioPacket->stream_index = optimizedOutputStream->index;
                    audioPacket->pos = -1;
                    ThrowIfError(ffmpeg.av_interleaved_write_frame(outputContext, audioPacket), "write optimized audio into MP4");
                    ffmpeg.av_packet_unref(audioPacket);
                    hasAudioPacket = ReadNextMappedPacket(audioContext, audioPacket, null, audioStreamIndex);
                }
                hadProgress = true;
            }

            if (token.IsCancellationRequested)
            {
                return CreateCanceledResult(hadProgress);
            }
            if (!IsFileInputFullyConsumed(baseContext, baseVideoPath))
            {
                return new FfmpegMediaRunResult(1, false, hadProgress, "base video ended before the physical file end");
            }
            int closeResult = CloseSegmentOutput(&outputContext, ref outputOpened, ref headerWritten);
            return closeResult < 0
                ? CreateNativeFailureResult(closeResult, token, hadProgress)
                : new FfmpegMediaRunResult(
                    0,
                    false,
                    hadProgress,
                    string.Empty,
                    Math.Max(0d, baseTimelineEnd / (double)ffmpeg.AV_TIME_BASE));
        }
        catch (Exception e)
        {
            return token.IsCancellationRequested
                ? CreateCanceledResult(hadProgress)
                : new FfmpegMediaRunResult(1, false, hadProgress, e.ToString());
        }
        finally
        {
            if (basePacket != null)
            {
                AVPacket* packet = basePacket;
                ffmpeg.av_packet_free(&packet);
            }
            if (audioPacket != null)
            {
                AVPacket* packet = audioPacket;
                ffmpeg.av_packet_free(&packet);
            }
            _ = CloseSegmentOutput(&outputContext, ref outputOpened, ref headerWritten);
            if (baseContext != null)
            {
                AVFormatContext* context = baseContext;
                ffmpeg.avformat_close_input(&context);
            }
            if (audioContext != null)
            {
                AVFormatContext* context = audioContext;
                ffmpeg.avformat_close_input(&context);
            }
            if (baseInterruptHandle.IsAllocated)
            {
                baseInterruptHandle.Free();
            }
            if (audioInterruptHandle.IsAllocated)
            {
                audioInterruptHandle.Free();
            }
        }
    }

    private static bool ReadNextMappedPacket(
        AVFormatContext* inputContext,
        AVPacket* packet,
        int[]? streamMap,
        int requiredStreamIndex)
    {
        int readResult;
        while ((readResult = ffmpeg.av_read_frame(inputContext, packet)) >= 0)
        {
            int streamIndex = packet->stream_index;
            bool accepted = requiredStreamIndex >= 0
                ? streamIndex == requiredStreamIndex
                : streamMap != null && streamIndex >= 0 && streamIndex < streamMap.Length && streamMap[streamIndex] >= 0;
            if (accepted)
            {
                return true;
            }
            ffmpeg.av_packet_unref(packet);
        }
        if (readResult != ffmpeg.AVERROR_EOF)
        {
            ThrowIfError(readResult, "read final MP4 packet");
        }
        return false;
    }

    private static void PrepareSourcePacket(
        AVFormatContext* inputContext,
        AVPacket* packet,
        long[] nextInputDts,
        long[] lastPacketEnds,
        ref long sourceTimestampBase)
    {
        int inputStreamIndex = packet->stream_index;
        AVStream* inputStream = inputContext->streams[inputStreamIndex];
        EnsurePacketDts(packet, inputStream, inputStreamIndex, nextInputDts, sourceTimestampBase);
        if (sourceTimestampBase == ffmpeg.AV_NOPTS_VALUE)
        {
            sourceTimestampBase = GetPacketTimestamp(packet, inputStream);
        }
        NormalizeSourcePacketTimestamps(packet, inputStream, sourceTimestampBase);
        NormalizePacketDts(packet, inputStream, inputStreamIndex, lastPacketEnds);
    }

    private static string BuildOptimizedAudioTemporaryPath(string targetFileName, string role, string extension)
    {
        string directory = Path.GetDirectoryName(targetFileName) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(targetFileName);
        return Path.Combine(directory, $".emerde-{stem}-{role}-{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteOptimizedAudioTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
