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

internal sealed record FfmpegMediaProbeResult(
    bool HasAudio,
    bool HasVideo,
    int Width,
    int Height,
    double DurationSeconds,
    long Bitrate);

internal sealed record FfmpegMediaRunResult(
    int ExitCode,
    bool WasCanceled,
    bool HadMediaProgress,
    string ErrorOutput);

internal static unsafe class FfmpegMediaEngine
{
    private static readonly object InitializeLock = new();
    private static bool initialized;

    public static string LibraryDirectory => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    public static bool IsAvailable => Directory.Exists(LibraryDirectory)
        && File.Exists(Path.Combine(LibraryDirectory, "avformat-61.dll"))
        && File.Exists(Path.Combine(LibraryDirectory, "avcodec-61.dll"))
        && File.Exists(Path.Combine(LibraryDirectory, "avutil-59.dll"))
        && File.Exists(Path.Combine(LibraryDirectory, "swresample-5.dll"))
        && File.Exists(Path.Combine(LibraryDirectory, "libwinpthread-1.dll"));

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
        CancellationToken token,
        Action<long>? onProgress = null)
    {
        return Remux([inputUrl], targetFileName, metadata, options, token, onProgress);
    }

    public static FfmpegMediaRunResult SplitFile(
        string sourceFileName,
        string targetPattern,
        int segmentSeconds,
        VideoRecordingMetadata metadata,
        CancellationToken token,
        Action<long>? onProgress = null)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName) || string.IsNullOrWhiteSpace(targetPattern) || segmentSeconds <= 0)
        {
            return new FfmpegMediaRunResult(1, false, false, "input, output, or segment duration is empty");
        }

        AVFormatContext* inputContext = null;
        AVFormatContext* outputContext = null;
        AVDictionary* inputOptions = null;
        AVDictionary* writeOptions = null;
        AVPacket* packet = null;
        bool headerWritten = false;
        bool hadProgress = false;

        try
        {
            EnsureInitialized();
            AddInputOptions(&inputOptions, new FfmpegInputOptions(string.Empty, string.Empty, false, string.Empty, false));
            int openResult = ffmpeg.avformat_open_input(&inputContext, sourceFileName, null, &inputOptions);
            if (openResult < 0)
            {
                return new FfmpegMediaRunResult(openResult, false, false, ErrorToString(openResult));
            }

            int streamInfoResult = ffmpeg.avformat_find_stream_info(inputContext, null);
            if (streamInfoResult < 0)
            {
                return new FfmpegMediaRunResult(streamInfoResult, false, false, ErrorToString(streamInfoResult));
            }

            ThrowIfError(ffmpeg.avformat_alloc_output_context2(&outputContext, null, "segment", targetPattern), "create segment output");
            if (outputContext == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "segment output context could not be created");
            }

            AddMetadata(outputContext, metadata);
            int[] streamMap = CreateOutputStreams(inputContext, outputContext);
            ffmpeg.av_dict_set(&writeOptions, "segment_time", segmentSeconds.ToString(CultureInfo.InvariantCulture), 0);
            ffmpeg.av_dict_set(&writeOptions, "reset_timestamps", "1", 0);
            ffmpeg.av_dict_set(&writeOptions, "segment_format", GetSegmentFormat(targetPattern), 0);
            ThrowIfError(ffmpeg.avformat_write_header(outputContext, &writeOptions), "write segment header");
            headerWritten = true;

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

                    return new FfmpegMediaRunResult(readResult, false, hadProgress, ErrorToString(readResult));
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
                int packetSize = Math.Max(0, packet->size);
                ffmpeg.av_packet_rescale_ts(packet, inputStream->time_base, outputStream->time_base);
                packet->stream_index = outputStreamIndex;
                packet->pos = -1;

                int writeResult = ffmpeg.av_interleaved_write_frame(outputContext, packet);
                ffmpeg.av_packet_unref(packet);
                if (writeResult < 0)
                {
                    return new FfmpegMediaRunResult(writeResult, false, hadProgress, ErrorToString(writeResult));
                }

                hadProgress = true;
                onProgress?.Invoke(packetSize);
            }

            if (token.IsCancellationRequested)
            {
                return new FfmpegMediaRunResult(255, true, hadProgress, string.Empty);
            }

            int trailerResult = ffmpeg.av_write_trailer(outputContext);
            if (trailerResult < 0)
            {
                return new FfmpegMediaRunResult(trailerResult, false, hadProgress, ErrorToString(trailerResult));
            }

            headerWritten = false;
            return new FfmpegMediaRunResult(0, false, hadProgress, string.Empty);
        }
        catch (Exception e)
        {
            return new FfmpegMediaRunResult(1, token.IsCancellationRequested, hadProgress, e.ToString());
        }
        finally
        {
            if (packet != null)
            {
                AVPacket* packetPointer = packet;
                ffmpeg.av_packet_free(&packetPointer);
            }

            if (headerWritten && outputContext != null)
            {
                _ = ffmpeg.av_write_trailer(outputContext);
            }

            if (inputContext != null)
            {
                AVFormatContext* context = inputContext;
                ffmpeg.avformat_close_input(&context);
            }

            if (outputContext != null)
            {
                ffmpeg.avformat_free_context(outputContext);
            }

            if (inputOptions != null)
            {
                ffmpeg.av_dict_free(&inputOptions);
            }

            if (writeOptions != null)
            {
                ffmpeg.av_dict_free(&writeOptions);
            }
        }
    }

    public static bool TryProbe(string sourceFileName, out FfmpegMediaProbeResult result, out string error)
    {
        result = new FfmpegMediaProbeResult(false, false, 0, 0, 0, 0);
        error = string.Empty;
        AVFormatContext* inputContext = null;
        AVDictionary* options = null;

        try
        {
            EnsureInitialized();
            AddInputOptions(&options, new FfmpegInputOptions(string.Empty, string.Empty, false, string.Empty, false));
            int openResult = ffmpeg.avformat_open_input(&inputContext, sourceFileName, null, &options);
            if (openResult < 0)
            {
                error = ErrorToString(openResult);
                return false;
            }

            int streamInfoResult = ffmpeg.avformat_find_stream_info(inputContext, null);
            if (streamInfoResult < 0)
            {
                error = ErrorToString(streamInfoResult);
                return false;
            }

            bool hasAudio = false;
            bool hasVideo = false;
            int width = 0;
            int height = 0;
            for (int index = 0; index < inputContext->nb_streams; index++)
            {
                AVStream* stream = inputContext->streams[index];
                AVCodecParameters* parameters = stream->codecpar;
                if (parameters->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    hasAudio = true;
                }
                else if (parameters->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    hasVideo = true;
                    if (width <= 0 && height <= 0)
                    {
                        width = parameters->width;
                        height = parameters->height;
                    }
                }
            }

            double durationSeconds = inputContext->duration > 0
                ? inputContext->duration / (double)ffmpeg.AV_TIME_BASE
                : 0;
            result = new FfmpegMediaProbeResult(hasAudio, hasVideo, width, height, durationSeconds, inputContext->bit_rate);
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
        }
    }

    private static FfmpegMediaRunResult Remux(
        IReadOnlyList<string> sourceFileNames,
        string targetFileName,
        VideoRecordingMetadata metadata,
        FfmpegInputOptions? inputOptions,
        CancellationToken token,
        Action<long>? onProgress)
    {
        if (sourceFileNames.Count == 0 || string.IsNullOrWhiteSpace(targetFileName))
        {
            return new FfmpegMediaRunResult(1, false, false, "input or output is empty");
        }

        AVFormatContext* outputContext = null;
        bool outputOpened = false;
        bool headerWritten = false;
        int[]? streamMap = null;
        long[]? timestampOffsets = null;
        long[]? lastDts = null;
        bool hadProgress = false;

        try
        {
            EnsureInitialized();
            ThrowIfError(ffmpeg.avformat_alloc_output_context2(&outputContext, null, null, targetFileName), "create output");
            if (outputContext == null)
            {
                return new FfmpegMediaRunResult(1, false, false, "output context could not be created");
            }

            AddMetadata(outputContext, metadata);

            for (int sourceIndex = 0; sourceIndex < sourceFileNames.Count; sourceIndex++)
            {
                if (token.IsCancellationRequested)
                {
                    return new FfmpegMediaRunResult(255, true, hadProgress, string.Empty);
                }

                AVFormatContext* inputContext = null;
                AVDictionary* options = null;
                AVPacket* packet = null;

                try
                {
                    FfmpegInputOptions effectiveOptions = inputOptions ?? new FfmpegInputOptions(string.Empty, string.Empty, false, string.Empty, false);
                    AddInputOptions(&options, effectiveOptions);
                    int openResult = ffmpeg.avformat_open_input(&inputContext, sourceFileNames[sourceIndex], null, &options);
                    if (openResult < 0)
                    {
                        return new FfmpegMediaRunResult(openResult, false, hadProgress, ErrorToString(openResult));
                    }

                    int streamInfoResult = ffmpeg.avformat_find_stream_info(inputContext, null);
                    if (streamInfoResult < 0)
                    {
                        return new FfmpegMediaRunResult(streamInfoResult, false, hadProgress, ErrorToString(streamInfoResult));
                    }

                    if (sourceIndex == 0)
                    {
                        streamMap = CreateOutputStreams(inputContext, outputContext);
                        timestampOffsets = new long[outputContext->nb_streams];
                        lastDts = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)outputContext->nb_streams).ToArray();

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
                    else if (streamMap == null || timestampOffsets == null || lastDts == null)
                    {
                        return new FfmpegMediaRunResult(1, false, hadProgress, "output stream map is missing");
                    }

                    long[] sourceTimestampBases = Enumerable.Repeat(ffmpeg.AV_NOPTS_VALUE, (int)outputContext->nb_streams).ToArray();
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

                            return new FfmpegMediaRunResult(readResult, false, hadProgress, ErrorToString(readResult));
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
                        NormalizePacketTimestamps(packet, outputStreamIndex, timestampOffsets, sourceTimestampBases);
                        int packetSize = Math.Max(0, packet->size);

                        ffmpeg.av_packet_rescale_ts(packet, inputStream->time_base, outputStream->time_base);
                        packet->stream_index = outputStreamIndex;
                        packet->pos = -1;

                        if (packet->dts != ffmpeg.AV_NOPTS_VALUE)
                        {
                            long previousDts = lastDts[outputStreamIndex];
                            if (previousDts != ffmpeg.AV_NOPTS_VALUE && ShouldAdjustPacketTimestampGap(packet, outputStream, previousDts))
                            {
                                long shift = packet->dts - previousDts - Math.Max(1, packet->duration);
                                packet->dts -= shift;
                                if (packet->pts != ffmpeg.AV_NOPTS_VALUE)
                                {
                                    packet->pts -= shift;
                                }
                            }

                            lastDts[outputStreamIndex] = packet->dts;
                        }

                        int writeResult = ffmpeg.av_interleaved_write_frame(outputContext, packet);
                        ffmpeg.av_packet_unref(packet);
                        if (writeResult < 0)
                        {
                            return new FfmpegMediaRunResult(writeResult, false, hadProgress, ErrorToString(writeResult));
                        }

                        hadProgress = true;
                        onProgress?.Invoke(packetSize);
                    }

                    if (token.IsCancellationRequested)
                    {
                        return new FfmpegMediaRunResult(255, true, hadProgress, string.Empty);
                    }

                    UpdateTimestampOffsets(inputContext, streamMap, outputContext, timestampOffsets, lastDts);
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
                }
            }

            if (headerWritten)
            {
                int trailerResult = ffmpeg.av_write_trailer(outputContext);
                if (trailerResult < 0)
                {
                    return new FfmpegMediaRunResult(trailerResult, false, hadProgress, ErrorToString(trailerResult));
                }
            }

            return new FfmpegMediaRunResult(0, false, hadProgress, string.Empty);
        }
        catch (Exception e)
        {
            return new FfmpegMediaRunResult(1, token.IsCancellationRequested, hadProgress, e.ToString());
        }
        finally
        {
            if (outputContext != null)
            {
                if (outputOpened && outputContext->pb != null)
                {
                    AVIOContext* ioContext = outputContext->pb;
                    ffmpeg.avio_closep(&ioContext);
                }

                ffmpeg.avformat_free_context(outputContext);
            }
        }
    }

    private static void NormalizePacketTimestamps(
        AVPacket* packet,
        int outputStreamIndex,
        long[] timestampOffsets,
        long[] sourceTimestampBases)
    {
        if (sourceTimestampBases[outputStreamIndex] == ffmpeg.AV_NOPTS_VALUE)
        {
            sourceTimestampBases[outputStreamIndex] = packet->dts != ffmpeg.AV_NOPTS_VALUE
                ? packet->dts
                : packet->pts;
        }

        long timestampBase = sourceTimestampBases[outputStreamIndex];
        if (timestampBase == ffmpeg.AV_NOPTS_VALUE)
        {
            return;
        }

        if (packet->pts != ffmpeg.AV_NOPTS_VALUE)
        {
            packet->pts = packet->pts - timestampBase + timestampOffsets[outputStreamIndex];
        }

        if (packet->dts != ffmpeg.AV_NOPTS_VALUE)
        {
            packet->dts = packet->dts - timestampBase + timestampOffsets[outputStreamIndex];
        }
    }

    private static bool ShouldAdjustPacketTimestampGap(AVPacket* packet, AVStream* outputStream, long previousDts)
    {
        long duration = Math.Max(1, packet->duration);
        long maximumForwardGap = ffmpeg.av_rescale_q(
            10 * ffmpeg.AV_TIME_BASE,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
            outputStream->time_base);

        return packet->dts <= previousDts || packet->dts - previousDts > Math.Max(maximumForwardGap, duration * 10);
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
            streamMap[index] = outputStream->index;
        }

        return streamMap;
    }

    private static void UpdateTimestampOffsets(
        AVFormatContext* inputContext,
        int[] streamMap,
        AVFormatContext* outputContext,
        long[] timestampOffsets,
        long[] lastDts)
    {
        for (int index = 0; index < streamMap.Length; index++)
        {
            int outputIndex = streamMap[index];
            if (outputIndex < 0)
            {
                continue;
            }

            AVStream* inputStream = inputContext->streams[index];
            AVStream* outputStream = outputContext->streams[outputIndex];
            long increment = 0;
            if (inputStream->duration > 0)
            {
                increment = ffmpeg.av_rescale_q(inputStream->duration, inputStream->time_base, outputStream->time_base);
            }
            else if (lastDts[outputIndex] != ffmpeg.AV_NOPTS_VALUE)
            {
                increment = lastDts[outputIndex] + 1 - timestampOffsets[outputIndex];
            }

            if (increment > 0)
            {
                timestampOffsets[outputIndex] += increment;
            }
        }
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

    private static string GetSegmentFormat(string targetPattern)
    {
        return Path.GetExtension(targetPattern).ToLowerInvariant() switch
        {
            ".ts" => "mpegts",
            ".mkv" => "matroska",
            ".mp4" => "mp4",
            ".flv" => "flv",
            string extension when extension.Length > 1 => extension[1..],
            _ => "mpegts",
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
        ffmpeg.av_dict_set(options, "rw_timeout", inputOptions.IsLive ? "15000000" : "5000000", 0);
        ffmpeg.av_dict_set(options, "reconnect", "1", 0);
        ffmpeg.av_dict_set(options, "reconnect_streamed", "1", 0);
        ffmpeg.av_dict_set(options, "reconnect_at_eof", "1", 0);
        ffmpeg.av_dict_set(options, "reconnect_on_network_error", "1", 0);
        ffmpeg.av_dict_set(options, "reconnect_delay_max", "8", 0);
        ffmpeg.av_dict_set(options, "reconnect_delay_total_max", "90", 0);
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
