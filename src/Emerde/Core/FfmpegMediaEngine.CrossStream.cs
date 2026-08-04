using FFmpeg.AutoGen;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Emerde.Core;

internal sealed record FfmpegCrossStreamAnalysisResult(
    bool IsConclusive,
    bool ShouldRestart,
    double TimelineDifferenceSeconds,
    double Confidence,
    string Reason,
    string Error);

internal static partial class FfmpegMediaEngine
{
    private const double CrossStreamTimelineRestartThresholdSeconds = 0.5d;
    private const double CrossStreamTimelineStableThresholdSeconds = 0.2d;
    private static readonly TimeSpan CrossStreamMismatchDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CrossStreamStableDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CrossStreamSampleInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CrossStreamSampleFreshness = TimeSpan.FromSeconds(2);

    internal static async Task<FfmpegCrossStreamAnalysisResult> CompareLiveStreamsAsync(
        string selectedUrl,
        string referenceUrl,
        FfmpegInputOptions inputOptions,
        TimeSpan maximumDuration,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(selectedUrl) || string.IsNullOrWhiteSpace(referenceUrl))
        {
            return new(false, false, 0, 0, string.Empty, "cross-stream input is missing");
        }

        TimeSpan boundedDuration = maximumDuration <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(15)
            : maximumDuration > TimeSpan.FromSeconds(15)
                ? TimeSpan.FromSeconds(15)
                : maximumDuration;
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutSource.CancelAfter(boundedDuration);
        CrossStreamSampleState selectedState = new();
        CrossStreamSampleState referenceState = new();
        Task<LiveTimelineProbeResult> selectedTask = Task.Run(
            () => ProbeLiveTimeline(selectedUrl, inputOptions, selectedState.Update, timeoutSource.Token),
            CancellationToken.None);
        Task<LiveTimelineProbeResult> referenceTask = Task.Run(
            () => ProbeLiveTimeline(referenceUrl, inputOptions, referenceState.Update, timeoutSource.Token),
            CancellationToken.None);
        Stopwatch observation = Stopwatch.StartNew();
        CrossStreamDecisionTracker decisionTracker = new(
            CrossStreamTimelineRestartThresholdSeconds,
            CrossStreamTimelineStableThresholdSeconds,
            CrossStreamMismatchDuration,
            CrossStreamStableDuration);
        double latestDifference = 0;
        string latestReason = string.Empty;

        try
        {
            while (!timeoutSource.IsCancellationRequested)
            {
                CrossStreamSnapshot selected = selectedState.Snapshot();
                CrossStreamSnapshot reference = referenceState.Snapshot();
                if (selected.HasTimeline && reference.HasTimeline)
                {
                    latestDifference = selected.DriftSeconds - reference.DriftSeconds;
                    bool visualMismatch = selected.VideoHashCount >= 4
                        && reference.VideoHashCount >= 4
                        && selected.VideoTransitions == 0
                        && reference.VideoTransitions >= 2;
                    CrossStreamDecision decision = decisionTracker.Observe(
                        observation.Elapsed,
                        latestDifference,
                        visualMismatch);
                    if (decision == CrossStreamDecision.Restart)
                    {
                        latestReason = visualMismatch ? "selected video remained frozen while reference video changed" : "audio-video drift differed between qualities";
                        double confidence = CalculateCrossStreamConfidence(selected, reference, visualMismatch);
                        return new(true, true, latestDifference, confidence, latestReason, string.Empty);
                    }
                    if (decision == CrossStreamDecision.Cancel)
                    {
                        double confidence = CalculateCrossStreamConfidence(selected, reference, false);
                        return new(true, false, latestDifference, confidence, "both qualities remained aligned", string.Empty);
                    }
                }
                else
                {
                    decisionTracker.Reset();
                }

                if (selectedTask.IsCompleted || referenceTask.IsCompleted)
                {
                    LiveTimelineProbeResult selectedResult = selectedTask.IsCompleted
                        ? await selectedTask
                        : default;
                    LiveTimelineProbeResult referenceResult = referenceTask.IsCompleted
                        ? await referenceTask
                        : default;
                    string error = string.Join("; ", new[] { selectedResult.Error, referenceResult.Error }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return new(false, false, latestDifference, 0, latestReason, error);
                    }
                }

                await Task.Delay(CrossStreamSampleInterval, timeoutSource.Token);
            }
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
        }
        finally
        {
            timeoutSource.Cancel();
            try
            {
                await Task.WhenAll(selectedTask, referenceTask);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (token.IsCancellationRequested)
        {
            return new(false, false, latestDifference, 0, latestReason, "cross-stream analysis was canceled");
        }

        CrossStreamSnapshot finalSelected = selectedState.Snapshot();
        CrossStreamSnapshot finalReference = referenceState.Snapshot();
        string timeoutError = finalSelected.HasTimeline && finalReference.HasTimeline
            ? "cross-stream analysis did not reach a stable decision"
            : "cross-stream analysis did not receive both audio and video timelines";
        return new(false, false, latestDifference, 0, latestReason, timeoutError);
    }

    private static unsafe LiveTimelineProbeResult ProbeLiveTimeline(
        string url,
        FfmpegInputOptions inputOptions,
        Action<LiveTimelineSample> onSample,
        CancellationToken token)
    {
        AVFormatContext* inputContext = null;
        AVCodecContext* videoDecoderContext = null;
        AVFrame* videoFrame = null;
        AVPacket* packet = null;
        AVDictionary* options = null;
        GCHandle interruptHandle = default;
        try
        {
            EnsureInitialized();
            ConfigureInterruptCallback(&inputContext, token, out interruptHandle);
            AddInputOptions(&options, inputOptions);
            int openResult = ffmpeg.avformat_open_input(&inputContext, url, null, &options);
            if (openResult < 0)
            {
                return new(ErrorToString(openResult));
            }

            ApplyInputRepairPolicy(inputContext);
            int streamInfoResult = ffmpeg.avformat_find_stream_info(inputContext, null);
            if (streamInfoResult < 0)
            {
                return new(ErrorToString(streamInfoResult));
            }

            int videoStreamIndex = ffmpeg.av_find_best_stream(inputContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            int audioStreamIndex = ffmpeg.av_find_best_stream(inputContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            if (videoStreamIndex < 0 || audioStreamIndex < 0)
            {
                return new("input does not contain both audio and video streams");
            }

            AVCodecParameters* videoParameters = inputContext->streams[videoStreamIndex]->codecpar;
            AVCodec* videoDecoder = ffmpeg.avcodec_find_decoder(videoParameters->codec_id);
            if (videoDecoder != null)
            {
                videoDecoderContext = ffmpeg.avcodec_alloc_context3(videoDecoder);
                if (videoDecoderContext != null
                    && ffmpeg.avcodec_parameters_to_context(videoDecoderContext, videoParameters) >= 0
                    && ffmpeg.avcodec_open2(videoDecoderContext, videoDecoder, null) >= 0)
                {
                    videoFrame = ffmpeg.av_frame_alloc();
                }
                else if (videoDecoderContext != null)
                {
                    AVCodecContext* failedContext = videoDecoderContext;
                    ffmpeg.avcodec_free_context(&failedContext);
                    videoDecoderContext = null;
                }
            }

            packet = ffmpeg.av_packet_alloc();
            if (packet == null)
            {
                return new("packet allocation failed");
            }

            Stopwatch elapsed = Stopwatch.StartNew();
            TimeSpan? lastSampleAt = null;
            TimeSpan? lastVideoHashAt = null;
            long firstVideoTimestamp = ffmpeg.AV_NOPTS_VALUE;
            long firstAudioTimestamp = ffmpeg.AV_NOPTS_VALUE;
            long videoEndTimestamp = ffmpeg.AV_NOPTS_VALUE;
            long audioEndTimestamp = ffmpeg.AV_NOPTS_VALUE;
            ulong? latestVideoHash = null;

            while (!token.IsCancellationRequested)
            {
                int readResult = ffmpeg.av_read_frame(inputContext, packet);
                if (readResult < 0)
                {
                    return token.IsCancellationRequested ? default : new(ErrorToString(readResult));
                }

                int streamIndex = packet->stream_index;
                if (streamIndex == videoStreamIndex || streamIndex == audioStreamIndex)
                {
                    AVStream* stream = inputContext->streams[streamIndex];
                    long timestamp = GetPacketTimestamp(packet, stream);
                    long endTimestamp = timestamp == ffmpeg.AV_NOPTS_VALUE
                        ? ffmpeg.AV_NOPTS_VALUE
                        : AddSaturated(timestamp, GetPacketDurationMicroseconds(packet, stream));
                    if (streamIndex == videoStreamIndex && timestamp != ffmpeg.AV_NOPTS_VALUE)
                    {
                        firstVideoTimestamp = firstVideoTimestamp == ffmpeg.AV_NOPTS_VALUE ? timestamp : firstVideoTimestamp;
                        videoEndTimestamp = videoEndTimestamp == ffmpeg.AV_NOPTS_VALUE ? endTimestamp : Math.Max(videoEndTimestamp, endTimestamp);
                        if (videoDecoderContext != null && videoFrame != null)
                        {
                            DecodeVideoHashes(videoDecoderContext, videoFrame, packet, elapsed.Elapsed, ref lastVideoHashAt, ref latestVideoHash);
                        }
                    }
                    else if (streamIndex == audioStreamIndex && timestamp != ffmpeg.AV_NOPTS_VALUE)
                    {
                        firstAudioTimestamp = firstAudioTimestamp == ffmpeg.AV_NOPTS_VALUE ? timestamp : firstAudioTimestamp;
                        audioEndTimestamp = audioEndTimestamp == ffmpeg.AV_NOPTS_VALUE ? endTimestamp : Math.Max(audioEndTimestamp, endTimestamp);
                    }

                    if (firstVideoTimestamp != ffmpeg.AV_NOPTS_VALUE
                        && firstAudioTimestamp != ffmpeg.AV_NOPTS_VALUE
                        && IsCrossStreamSampleDue(elapsed.Elapsed, lastSampleAt, CrossStreamSampleInterval))
                    {
                        lastSampleAt = elapsed.Elapsed;
                        double driftSeconds = SubtractSaturated(audioEndTimestamp, videoEndTimestamp) / 1_000_000d;
                        onSample(new(elapsed.Elapsed, driftSeconds, latestVideoHash));
                    }
                }

                ffmpeg.av_packet_unref(packet);
            }

            return default;
        }
        catch (Exception e)
        {
            return token.IsCancellationRequested ? default : new(e.Message);
        }
        finally
        {
            if (packet != null)
            {
                AVPacket* packetPointer = packet;
                ffmpeg.av_packet_free(&packetPointer);
            }
            if (videoFrame != null)
            {
                AVFrame* framePointer = videoFrame;
                ffmpeg.av_frame_free(&framePointer);
            }
            if (videoDecoderContext != null)
            {
                AVCodecContext* decoderPointer = videoDecoderContext;
                ffmpeg.avcodec_free_context(&decoderPointer);
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

    private static unsafe void DecodeVideoHashes(
        AVCodecContext* decoderContext,
        AVFrame* frame,
        AVPacket* packet,
        TimeSpan elapsed,
        ref TimeSpan? lastVideoHashAt,
        ref ulong? latestVideoHash)
    {
        if (ffmpeg.avcodec_send_packet(decoderContext, packet) < 0)
        {
            return;
        }

        while (ffmpeg.avcodec_receive_frame(decoderContext, frame) >= 0)
        {
            if (IsCrossStreamSampleDue(elapsed, lastVideoHashAt, TimeSpan.FromMilliseconds(500)))
            {
                ulong? hash = CalculateVideoDifferenceHash(frame);
                if (hash.HasValue)
                {
                    latestVideoHash = hash;
                    lastVideoHashAt = elapsed;
                }
            }
            ffmpeg.av_frame_unref(frame);
        }
    }

    internal static bool IsCrossStreamSampleDue(TimeSpan elapsed, TimeSpan? lastSampleAt, TimeSpan interval)
    {
        return !lastSampleAt.HasValue || elapsed - lastSampleAt.Value >= interval;
    }

    private static unsafe ulong? CalculateVideoDifferenceHash(AVFrame* frame)
    {
        byte* plane = frame->data[0];
        int width = frame->width;
        int height = frame->height;
        int lineSize = frame->linesize[0];
        if (plane == null || width < 9 || height < 8 || lineSize == 0)
        {
            return null;
        }

        int absoluteLineSize = Math.Abs(lineSize);
        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < 8; y++)
        {
            int sourceY = Math.Min(height - 1, (y * height + height / 2) / 8);
            byte* row = lineSize > 0
                ? plane + sourceY * lineSize
                : plane + (height - 1 - sourceY) * absoluteLineSize;
            for (int x = 0; x < 8; x++)
            {
                int leftOffset = Math.Min(absoluteLineSize - 1, (x * absoluteLineSize + absoluteLineSize / 2) / 9);
                int rightOffset = Math.Min(absoluteLineSize - 1, ((x + 1) * absoluteLineSize + absoluteLineSize / 2) / 9);
                if (row[leftOffset] > row[rightOffset])
                {
                    hash |= 1UL << bit;
                }
                bit++;
            }
        }
        return hash;
    }

    private static double CalculateCrossStreamConfidence(
        CrossStreamSnapshot selected,
        CrossStreamSnapshot reference,
        bool visualMismatch)
    {
        double sampleConfidence = Math.Clamp(Math.Min(selected.SampleCount, reference.SampleCount) / 25d, 0, 1);
        if (!visualMismatch)
        {
            return sampleConfidence;
        }

        double visualConfidence = Math.Clamp(Math.Min(selected.VideoHashCount, reference.VideoHashCount) / 8d, 0, 1);
        return Math.Min(sampleConfidence, visualConfidence);
    }

    private readonly record struct LiveTimelineSample(TimeSpan Elapsed, double DriftSeconds, ulong? VideoHash);

    private readonly record struct LiveTimelineProbeResult(string Error);

    private readonly record struct CrossStreamSnapshot(
        bool HasTimeline,
        double DriftSeconds,
        int SampleCount,
        int VideoHashCount,
        int VideoTransitions);

    internal enum CrossStreamDecision
    {
        Pending,
        Restart,
        Cancel,
    }

    internal sealed class CrossStreamDecisionTracker(
        double restartThresholdSeconds,
        double stableThresholdSeconds,
        TimeSpan mismatchDuration,
        TimeSpan stableDuration)
    {
        private TimeSpan? mismatchSince;
        private TimeSpan? stableSince;

        public void Reset()
        {
            mismatchSince = null;
            stableSince = null;
        }

        public CrossStreamDecision Observe(TimeSpan elapsed, double timelineDifferenceSeconds, bool visualMismatch)
        {
            if (visualMismatch || Math.Abs(timelineDifferenceSeconds) > restartThresholdSeconds)
            {
                mismatchSince ??= elapsed;
                stableSince = null;
                return elapsed - mismatchSince.Value >= mismatchDuration
                    ? CrossStreamDecision.Restart
                    : CrossStreamDecision.Pending;
            }

            mismatchSince = null;
            if (Math.Abs(timelineDifferenceSeconds) <= stableThresholdSeconds)
            {
                stableSince ??= elapsed;
                return elapsed - stableSince.Value >= stableDuration
                    ? CrossStreamDecision.Cancel
                    : CrossStreamDecision.Pending;
            }

            stableSince = null;
            return CrossStreamDecision.Pending;
        }
    }

    private sealed class CrossStreamSampleState
    {
        private readonly object syncRoot = new();
        private readonly Queue<(TimeSpan Elapsed, ulong Hash)> videoHashes = new();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private double driftSeconds;
        private int sampleCount;
        private TimeSpan lastSampleElapsed = TimeSpan.MinValue;

        public void Update(LiveTimelineSample sample)
        {
            lock (syncRoot)
            {
                TimeSpan observedAt = elapsed.Elapsed;
                driftSeconds = sample.DriftSeconds;
                sampleCount++;
                lastSampleElapsed = observedAt;
                if (sample.VideoHash.HasValue
                    && (videoHashes.Count == 0 || observedAt - videoHashes.Last().Elapsed >= TimeSpan.FromMilliseconds(400)))
                {
                    videoHashes.Enqueue((observedAt, sample.VideoHash.Value));
                }
                Trim(observedAt);
            }
        }

        public CrossStreamSnapshot Snapshot()
        {
            lock (syncRoot)
            {
                TimeSpan observedAt = elapsed.Elapsed;
                Trim(observedAt);
                ulong[] hashes = videoHashes.Select(item => item.Hash).ToArray();
                int transitions = hashes.Skip(1)
                    .Select((hash, index) => BitOperations.PopCount(hash ^ hashes[index]))
                    .Count(distance => distance >= 6);
                bool hasTimeline = sampleCount > 0
                    && observedAt - lastSampleElapsed <= CrossStreamSampleFreshness;
                return new(hasTimeline, driftSeconds, sampleCount, hashes.Length, transitions);
            }
        }

        private void Trim(TimeSpan elapsed)
        {
            TimeSpan threshold = elapsed - CrossStreamMismatchDuration;
            while (videoHashes.Count > 0 && videoHashes.Peek().Elapsed < threshold)
            {
                videoHashes.Dequeue();
            }
        }
    }
}
