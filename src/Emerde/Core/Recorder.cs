using CommunityToolkit.Mvvm.Messaging;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Emerde.Extensions;
using Emerde.Models;
using Emerde.Threading;

namespace Emerde.Core;

public sealed class Recorder
{
    private const int MaxRecordingAttempts = 4;
    internal const int OfflineRefreshConfirmationCount = 1;
    private const int ProcessOutputTailLimit = 8192;
    private static readonly TimeSpan ProgressStartupTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ProgressStallTimeout = TimeSpan.FromSeconds(10);
    private const string OptimizedAudioFilter = "[0:a:0]volume=30dB,acompressor=threshold=-10dB:ratio=3,alimiter=limit=0.316227766:level=false[aopt]";

    internal static readonly TimeSpan ProcessStopGracePeriod = TimeSpan.FromSeconds(3);

    private static readonly object OutputReservationLock = new();

    private static readonly HashSet<string> ReservedOutputPatterns = new(StringComparer.OrdinalIgnoreCase);

    public RecordStatus RecordStatus { get; internal set; } = RecordStatus.Initialized;

    public CancellationTokenSource? TokenSource { get; private set; } = null;

    private readonly object stateLock = new();

    private readonly object processLock = new();

    private Process? currentProcess;

    private Task? recordingTask;

    private int stopRequested;

    private int deferPostProcessing;

    private int hasMediaProgress;

    private string lastProcessErrorOutput = string.Empty;

    private bool lastStreamRefreshHadUrl;

    private DateTime lastLiveWithoutStreamLogAt = DateTime.MinValue;

    private readonly List<string> pendingRecordingPaths = [];

    private readonly List<string> unregisteredRecordingPatterns = [];

    private IDisposable? mediaOperationRegistration;

    public bool IsBusy => recordingTask is { IsCompleted: false };

    public bool HasMediaProgress => Volatile.Read(ref hasMediaProgress) != 0;

    public string? Url { get; set; } = null;

    public string? FileName { get; set; } = null;

    public string? Parameters { get; set; } = null;

    public string? MetadataPath { get; set; } = null;

    public DateTime StartTime { get; private set; } = DateTime.MinValue;

    public DateTime EndTime { get; private set; } = DateTime.MinValue;

    public bool IsToSegment { get; set; } = false;

    public Task Start(RecorderStartInfo startInfo, CancellationTokenSource? tokenSource = null)
    {
        lock (stateLock)
        {
            if (RecordStatus == RecordStatus.Recording || recordingTask is { IsCompleted: false })
            {
                return recordingTask ?? Task.CompletedTask;
            }

            Volatile.Write(ref stopRequested, 0);
            Volatile.Write(ref deferPostProcessing, 0);
            Volatile.Write(ref hasMediaProgress, 0);
            pendingRecordingPaths.Clear();
            unregisteredRecordingPatterns.Clear();
            FileName = null;
            MetadataPath = null;
            StartTime = DateTime.MinValue;
            EndTime = DateTime.MinValue;
            RecordStatus = RecordStatus.Recording;
            TokenSource = tokenSource ?? new CancellationTokenSource();
            mediaOperationRegistration = MediaOperationRegistry.Register(
                MediaOperationKind.Recording,
                () => [FileName],
                () => Stop(deferPostProcessing: true));
            try
            {
                recordingTask = Task.Factory.StartNew(
                    () => RunAsync(startInfo, TokenSource.Token),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                ).Unwrap();
            }
            catch
            {
                mediaOperationRegistration.Dispose();
                mediaOperationRegistration = null;
                throw;
            }
            return recordingTask;
        }
    }

    private async Task RunAsync(RecorderStartInfo startInfo, CancellationToken token)
    {
        RoomRecordingOptions recordingOptions = startInfo.Options;
        try
        {
            string? recorderPath = SearchFileHelper.SearchExecutable("ffmpeg.exe");

            if (recorderPath == null)
            {
                RecordStatus = RecordStatus.NotRecording;
                AppSessionLogger.Event("error", "recorder", "recorder_missing", "ffmpeg executable was not found", new
                {
                    startInfo.RoomUrl,
                    startInfo.NickName,
                });
                return;
            }

            SaveFolderResolution saveFolderResolution = SaveFolderHelper.ResolveForRecording(recordingOptions.SaveFolder);
            string saveFolder = saveFolderResolution.Folder;
            if (saveFolderResolution.UsedFallback)
            {
                AppSessionLogger.Event("warn", "storage", "recording_save_folder_fallback", saveFolderResolution.Error?.Message ?? string.Empty, new
                {
                    configuredFolder = recordingOptions.SaveFolder,
                    fallbackFolder = saveFolder,
                    startInfo.RoomUrl,
                });
                try
                {
                    Notifier.AddNotice("Emerde", "保存目录不可用", $"本次录制已临时保存到：{saveFolder}");
                }
                catch (Exception notificationError)
                {
                    AppSessionLogger.WriteException(notificationError);
                }
            }
            saveFolder = BuildSaveFolder(saveFolder, startInfo.NickName, DateTime.Now, recordingOptions.SaveFolderPathLevel);
            Directory.CreateDirectory(saveFolder);

            string userAgent = Configurations.UserAgent.Get();
            string httpProxy = ProxyAddress.Normalize(Configurations.ProxyUrl.Get());
            bool isUseProxy = Configurations.IsUseProxy.Get() && !string.IsNullOrWhiteSpace(httpProxy);
            long segmentTime = Math.Max(1, recordingOptions.SegmentTime);
            int segmentTimeUnit = SegmentTimeUnitHelper.NormalizeUnit(recordingOptions.SegmentTimeUnit);
            bool isToSegment = recordingOptions.IsToSegment && segmentTime > 0;
            bool isToSegmentBySize = isToSegment && SegmentTimeUnitHelper.IsSizeUnit(segmentTimeUnit);
            string headers = NormalizeHeaders(startInfo.Headers);

            IsToSegment = isToSegment;
            Url = SelectInputUrl(startInfo);

            if (string.IsNullOrWhiteSpace(Url))
            {
                RecordStatus = RecordStatus.NotRecording;
                AppSessionLogger.Event("warn", "recorder", "record_no_input", "recording has no input stream url", new
                {
                    startInfo.RoomUrl,
                    startInfo.NickName,
                    hasRecordUrl = !string.IsNullOrWhiteSpace(startInfo.RecordUrl),
                    hasFlvUrl = !string.IsNullOrWhiteSpace(startInfo.FlvUrl),
                    hasHlsUrl = !string.IsNullOrWhiteSpace(startInfo.HlsUrl),
                });
                return;
            }

            bool isHls = IsHlsUrl(Url, startInfo);
            string? targetFormat = GetTargetFormat(recordingOptions.RecordFormat);
            bool useTransportStream = ShouldUseTransportStream(isHls, isToSegment, targetFormat);
            bool disableOptimizedAudio = false;

            if (string.IsNullOrWhiteSpace(userAgent))
            {
                userAgent = "Mozilla/5.0 (Linux; Android 11; SAMSUNG SM-G973U) AppleWebKit/537.36 ("
                          + "KHTML, like Gecko) SamsungBrowser/14.2 Chrome/87.0.4280.141 Mobile "
                          + "Safari/537.36";
            }

            EndTime = DateTime.MinValue;
            int attempt = 0;
            int offlineRefreshChecks = 0;
            while (!token.IsCancellationRequested && Volatile.Read(ref stopRequested) == 0)
            {
                DateTime now = DateTime.Now;
                string requestedBaseFileName = BuildBaseFileName(startInfo, now).SanitizeFileName();
                using OutputReservation outputReservation = ReserveOutput(saveFolder, requestedBaseFileName, isToSegment, useTransportStream);
                string baseFileName = outputReservation.BaseFileName;
                FileName = outputReservation.OutputPattern;
                VideoRecordingMetadata metadata = BuildMetadata(baseFileName, useTransportStream ? "ts" : "flv", startInfo, now);
                MetadataPath = VideoRecordingMetadataStore.WriteSidecar(saveFolder, baseFileName, metadata);
                string? pendingRecordingPath = RecordingRecoveryService.Register(FileName, recordingOptions);
                if (!string.IsNullOrWhiteSpace(pendingRecordingPath))
                {
                    pendingRecordingPaths.Add(pendingRecordingPath);
                }
                else
                {
                    unregisteredRecordingPatterns.Add(FileName);
                }
                bool useOptimizedAudio = useTransportStream && !disableOptimizedAudio;

                List<string> arguments = BuildArguments(
                    FileName,
                    isUseProxy,
                    httpProxy,
                    headers,
                    userAgent,
                    isToSegment,
                    isToSegmentBySize,
                    segmentTime,
                    segmentTimeUnit,
                    metadata,
                    useOptimizedAudio);

                Parameters = FormatArguments(arguments);
                AppSessionLogger.Event("info", "recorder", "record_process_starting", "ffmpeg recording process is starting", new
                {
                    startInfo.RoomUrl,
                    startInfo.NickName,
                    startInfo.PlatformName,
                    inputKind = isHls ? "hls" : "flv",
                    hasHeaders = !string.IsNullOrWhiteSpace(headers),
                    FileName,
                    isToSegment,
                    useOptimizedAudio,
                    isUseProxy,
                    attempt,
                });

                int exitCode = await ExecuteRecorderAsync(recorderPath, arguments, isUseProxy, httpProxy, startInfo, token);
                DeleteMetadataIfNoOutput(FileName, MetadataPath);
                if (token.IsCancellationRequested || Volatile.Read(ref stopRequested) != 0)
                {
                    FinalizeMetadataForOutput(FileName, MetadataPath);
                    break;
                }

                if (useOptimizedAudio && IsMissingAudioError(lastProcessErrorOutput))
                {
                    disableOptimizedAudio = true;
                    DeleteFailedOutputFiles(FileName, MetadataPath);
                    AppSessionLogger.Event("warn", "recorder", "optimized_audio_unavailable", "recording source has no audio stream and will retry without audio processing", new
                    {
                        startInfo.RoomUrl,
                        startInfo.NickName,
                        FileName,
                    });
                    continue;
                }

                FinalizeMetadataForOutput(FileName, MetadataPath);

                bool hasStreamRefresh = startInfo.RefreshStreamAsync != null;
                bool? isLiveAfterRefresh = await TryRefreshInputAsync(startInfo, token);
                offlineRefreshChecks = isLiveAfterRefresh == false ? offlineRefreshChecks + 1 : 0;
                bool offlineConfirmed = isLiveAfterRefresh == false && offlineRefreshChecks >= OfflineRefreshConfirmationCount;
                if (!ShouldRetryRecording(exitCode, hasStreamRefresh, isLiveAfterRefresh, offlineRefreshChecks))
                {
                    if (offlineConfirmed)
                    {
                        startInfo.OfflineConfirmed?.Invoke();
                    }
                    break;
                }

                if (isLiveAfterRefresh == true)
                {
                    headers = NormalizeHeaders(startInfo.Headers);
                    isHls = IsHlsUrl(Url!, startInfo);
                    useTransportStream = ShouldUseTransportStream(isHls, isToSegment, targetFormat);
                }

                if (ShouldConsumeReconnectAttempt(isLiveAfterRefresh))
                {
                    attempt++;
                }
                else
                {
                    attempt = 0;
                }
                if (attempt > 0 && !CanRetryRecording(attempt))
                {
                    AppSessionLogger.Event("error", "recorder", "record_reconnect_exhausted", "record reconnect attempts exhausted", new
                    {
                        startInfo.RoomUrl,
                        startInfo.NickName,
                        exitCode,
                        attempt,
                    });
                    break;
                }

                TimeSpan delay = isLiveAfterRefresh == true && !lastStreamRefreshHadUrl
                    ? TimeSpan.FromSeconds(3)
                    : TimeSpan.FromSeconds(Math.Min(8, attempt switch
                {
                    0 => 1,
                    1 => 1,
                    2 => 3,
                    _ => 8,
                }));
                AppSessionLogger.Event("warn", "recorder", "record_reconnect_scheduled", "record reconnect scheduled", new
                {
                    startInfo.RoomUrl,
                    startInfo.NickName,
                    exitCode,
                    attempt,
                    delaySeconds = delay.TotalSeconds,
                });
                await Task.Delay(delay, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            AppSessionLogger.WriteException(e);
        }
        finally
        {
            try
            {
                EndTime = DateTime.Now;
                DeleteMetadataIfNoOutput(FileName ?? string.Empty, MetadataPath);
                lock (stateLock)
                {
                    if (RecordStatus == RecordStatus.Recording)
                    {
                        RecordStatus = RecordStatus.NotRecording;
                    }
                }
                AppSessionLogger.Event("info", "recorder", "record_finished", "recording task finished", new
                {
                    startInfo.RoomUrl,
                    startInfo.NickName,
                    FileName,
                    stopRequested = Volatile.Read(ref stopRequested) != 0,
                    startedAt = StartTime,
                    endedAt = EndTime,
                    durationSeconds = StartTime == DateTime.MinValue ? 0 : Math.Max(0, (EndTime - StartTime).TotalSeconds),
                });
                try
                {
                    _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(startInfo.RoomUrl));
                }
                catch (Exception e)
                {
                    AppSessionLogger.WriteException(e);
                }
                RoomRecordingOptions postProcessingOptions = startInfo.ResolveCurrentOptions?.Invoke() ?? recordingOptions;
                bool processNow = Volatile.Read(ref deferPostProcessing) == 0;
                foreach (string pendingRecordingPath in pendingRecordingPaths.ToArray())
                {
                    if (RecordingRecoveryService.UpdateOptions(pendingRecordingPath, postProcessingOptions) && processNow)
                    {
                        await RecordingRecoveryService.ProcessAsync(pendingRecordingPath);
                    }
                }

                foreach (string sourcePattern in unregisteredRecordingPatterns)
                {
                    string? pendingPath = RecordingRecoveryService.Register(sourcePattern, postProcessingOptions);
                    if (!string.IsNullOrWhiteSpace(pendingPath))
                    {
                        pendingRecordingPaths.Add(pendingPath);
                        if (processNow)
                        {
                            await RecordingRecoveryService.ProcessAsync(pendingPath);
                        }
                    }
                }

                if (!processNow)
                {
                    AppSessionLogger.Event("info", "recorder", "post_processing_deferred", "recording post-processing was deferred until the next startup", new
                    {
                        startInfo.RoomUrl,
                        startInfo.NickName,
                        pendingCount = pendingRecordingPaths.Count,
                    });
                }
                RecordingCleanupService.QueueRun();
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
            finally
            {
                mediaOperationRegistration?.Dispose();
                mediaOperationRegistration = null;
            }
        }
    }

    public void Stop(bool deferPostProcessing = false)
    {
        if (deferPostProcessing)
        {
            Interlocked.Exchange(ref this.deferPostProcessing, 1);
        }
        Interlocked.Exchange(ref stopRequested, 1);
        RequestCurrentProcessExit();
        lock (stateLock)
        {
            TokenSource?.Cancel();
            if (RecordStatus == RecordStatus.Recording)
            {
                EndTime = DateTime.Now;
                RecordStatus = RecordStatus.NotRecording;
            }
        }
    }

    public void EndNowIfRecording()
    {
        lock (stateLock)
        {
            if (EndTime == DateTime.MinValue)
            {
                EndTime = DateTime.Now;
            }

            if (RecordStatus == RecordStatus.Recording)
            {
                RecordStatus = RecordStatus.NotRecording;
            }
        }
    }

    internal List<string> BuildArguments(
        string outputFileName,
        bool isUseProxy,
        string httpProxy,
        string headers,
        string userAgent,
        bool isToSegment,
        bool isToSegmentBySize,
        long segmentTime,
        int segmentTimeUnit,
        VideoRecordingMetadata metadata,
        bool useOptimizedAudio)
    {
        List<string> arguments =
        [
            "-n",
            "-v", "verbose",
            "-rw_timeout", "15000000",
            "-loglevel", "error",
            "-hide_banner",
            "-progress", "pipe:1",
            "-stats_period", "1",
            "-user_agent", userAgent,
            "-protocol_whitelist", "rtmp,crypto,file,http,https,tcp,tls,udp,rtp,httpproxy",
            "-thread_queue_size", "1024",
            "-analyzeduration", "20000000",
            "-probesize", "10000000",
            "-fflags", "+discardcorrupt",
        ];

        arguments
            .AddIf(isUseProxy, "-http_proxy", httpProxy)
            .AddIf(!string.IsNullOrWhiteSpace(headers), "-headers", headers)
            .AddIf(true,
                "-reconnect_delay_max", "8",
                "-reconnect_delay_total_max", "10",
                "-reconnect_max_retries", "4",
                "-reconnect", "1",
                "-reconnect_streamed", "1",
                "-reconnect_at_eof", "1",
                "-reconnect_on_network_error", "1",
                "-reconnect_on_http_error", "4xx,5xx",
                "-i", Url ?? string.Empty,
                "-sn",
                "-dn",
                "-max_muxing_queue_size", "1024",
                "-correct_ts_overflow", "1",
                "-avoid_negative_ts", "1"
            )
            .AddIf(isToSegment && !isToSegmentBySize,
                "-f", "segment",
                "-segment_time", SegmentTimeUnitHelper.ToSegmentArgument(segmentTime, segmentTimeUnit),
                "-segment_time_delta", "0.05",
                "-segment_atclocktime", "0",
                "-segment_format", "mpegts"
            )
            .AddIf(isToSegmentBySize,
                "-f", "segment",
                "-segment_size", segmentTime.ToString(),
                "-segment_format", "mpegts"
            )
            .AddIf(isToSegment,
                "-reset_timestamps", "1"
            );

        arguments.AddRange(BuildAudioMappingArguments(useOptimizedAudio));

        arguments.AddRange(VideoRecordingMetadataStore.BuildFfmpegMetadataArguments(metadata));
        if (VideoRecordingMetadataStore.UsesMovMetadataTags(outputFileName))
        {
            arguments.AddRange(["-movflags", "use_metadata_tags"]);
        }

        arguments.Add(outputFileName);

        return arguments;
    }

    private async Task<int> ExecuteRecorderAsync(string recorderPath, List<string> arguments, bool isUseProxy, string httpProxy, RecorderStartInfo startInfo, CancellationToken token)
    {
        ProcessStartInfo processStartInfo = new()
        {
            FileName = recorderPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };

        foreach (string argument in arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        if (isUseProxy)
        {
            processStartInfo.Environment["http_proxy"] = httpProxy;
            processStartInfo.Environment["https_proxy"] = httpProxy;
        }

        using Process process = new() { StartInfo = processStartInfo };
        Stopwatch processLifetime = Stopwatch.StartNew();
        process.Start();
        TryTraceProcess(process);
        RuntimeResourceLogger.Register(process, "ffmpeg", "record", startInfo.RoomUrl, startInfo.NickName, new
        {
            startInfo.PlatformName,
            FileName,
        });

        lock (processLock)
        {
            currentProcess = process;
        }

        StringBuilder errorTail = new();
        Task errorTask = ReadPipeAsync(process.StandardError, async (data, readToken) =>
        {
            AppendOutputTail(errorTail, data);
            await OnStandardErrorReceived(data, readToken);
        }, CancellationToken.None);
        RecorderProgressTracker progressTracker = new(DateTime.UtcNow);
        Task outputTask = ReadPipeAsync(process.StandardOutput, (data, _) =>
        {
            if (progressTracker.Observe(data, DateTime.UtcNow))
            {
                ConfirmMediaProgress(startInfo);
            }
            return Task.CompletedTask;
        }, CancellationToken.None);
        bool wasCanceled = false;
        bool wasStalled = false;

        try
        {
            Task exitTask = process.WaitForExitAsync(CancellationToken.None);
            using CancellationTokenSource cancellationWaitSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            using CancellationTokenSource progressWaitSource = new();
            Task cancellationTask = WaitForCancellationAsync(cancellationWaitSource.Token);
            Task<bool> progressStallTask = WaitForProgressStallAsync(progressTracker, progressWaitSource.Token);
            Task completedTask = await Task.WhenAny(exitTask, cancellationTask, progressStallTask);

            if (token.IsCancellationRequested)
            {
                wasCanceled = true;
                await StopProcessGracefullyAsync(process);
            }
            else if (completedTask == progressStallTask && await progressStallTask)
            {
                wasStalled = true;
                AppSessionLogger.Event("warn", "recorder", "record_progress_stalled", "ffmpeg media progress stopped and the stream will be refreshed", new
                {
                    startInfo.RoomUrl,
                    startInfo.NickName,
                    process.Id,
                    FileName,
                    stalledSeconds = progressTracker.GetStalledDuration(DateTime.UtcNow).TotalSeconds,
                });
                await StopProcessGracefullyAsync(process);
            }
            else
            {
                await exitTask;
            }
            cancellationWaitSource.Cancel();
            progressWaitSource.Cancel();
            await cancellationTask;
            await progressStallTask;
        }
        finally
        {
            lock (processLock)
            {
                if (ReferenceEquals(currentProcess, process))
                {
                    currentProcess = null;
                }
            }
        }

        await Task.WhenAll(errorTask, outputTask);
        processLifetime.Stop();
        lastProcessErrorOutput = errorTail.ToString();
        double durationSeconds = processLifetime.Elapsed.TotalSeconds;
        AppSessionLogger.Event(GetProcessExitLogLevel(process.ExitCode, wasCanceled, wasStalled), "recorder", "record_process_exited", "ffmpeg recording process exited", new
        {
            startInfo.RoomUrl,
            startInfo.NickName,
            process.Id,
            process.ExitCode,
            wasCanceled,
            wasStalled,
            FileName,
            durationSeconds,
            errorOutput = process.ExitCode == 0 ? string.Empty : lastProcessErrorOutput,
        });
        if (ShouldLogRapidExit(wasCanceled, wasStalled, durationSeconds))
        {
            AppSessionLogger.Event("warn", "recorder", "record_rapid_exit", "ffmpeg recording process exited in less than one minute", new
            {
                startInfo.RoomUrl,
                startInfo.NickName,
                process.Id,
                process.ExitCode,
                FileName,
                durationSeconds,
                hasErrorOutput = !string.IsNullOrWhiteSpace(lastProcessErrorOutput),
            });
        }

        if (wasCanceled)
        {
            throw new OperationCanceledException(token);
        }

        return process.ExitCode;
    }

    private void ConfirmMediaProgress(RecorderStartInfo startInfo)
    {
        lock (stateLock)
        {
            if (RecordStatus != RecordStatus.Recording
                || Volatile.Read(ref stopRequested) != 0
                || Interlocked.Exchange(ref hasMediaProgress, 1) != 0)
            {
                return;
            }

            StartTime = DateTime.Now;
        }

        AppSessionLogger.Event("info", "recorder", "record_media_started", "ffmpeg started writing media", new
        {
            startInfo.RoomUrl,
            startInfo.NickName,
            startInfo.PlatformName,
            FileName,
        });
        _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(startInfo.RoomUrl));
    }

    internal static string GetProcessExitLogLevel(int exitCode, bool wasCanceled, bool wasStalled)
    {
        return exitCode == 0 || wasCanceled || wasStalled ? "info" : "warn";
    }

    internal static bool ShouldLogRapidExit(bool wasCanceled, bool wasStalled, double durationSeconds)
    {
        return !wasCanceled && !wasStalled && durationSeconds < 60;
    }

    private static async Task WaitForCancellationAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task ReadPipeAsync(StreamReader reader, Func<string, CancellationToken, Task> handler, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line == null)
                {
                    break;
                }

                await handler(line, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string[] GetRecordedSourceFilesForPattern(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return [];
        }

        if (!fileName.Contains("%03d", StringComparison.Ordinal))
        {
            return File.Exists(fileName) ? [fileName] : [];
        }

        string? directory = Path.GetDirectoryName(fileName);
        string pattern = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || string.IsNullOrWhiteSpace(pattern))
        {
            return [];
        }

        string regexPattern = "^" + Regex.Escape(pattern).Replace("%03d", @"\d{3,}") + "$";
        Regex regex = new(regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Directory.EnumerateFiles(directory)
            .Where(file => regex.IsMatch(Path.GetFileName(file)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void DeleteMetadataIfNoOutput(string fileName, string? metadataPath)
    {
        if (GetRecordedSourceFilesForPattern(fileName).Length > 0 || string.IsNullOrWhiteSpace(metadataPath))
        {
            return;
        }

        try
        {
            File.Delete(metadataPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void FinalizeMetadataForOutput(string fileName, string? metadataPath)
    {
        _ = VideoRecordingMetadataStore.FinalizeSidecarForMedia(GetRecordedSourceFilesForPattern(fileName), metadataPath);
    }

    private void RequestCurrentProcessExit()
    {
        Process? process;
        lock (processLock)
        {
            process = currentProcess;
        }

        if (process != null)
        {
            RequestProcessExit(process);
        }
    }

    private static async Task StopProcessGracefullyAsync(Process process)
    {
        RequestProcessExit(process);

        if (await WaitForExitAsync(process, ProcessStopGracePeriod))
        {
            return;
        }

        KillProcessTree(process);
        await process.WaitForExitAsync(CancellationToken.None);
    }

    private static void RequestProcessExit(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("q");
                process.StandardInput.Flush();
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ObjectDisposedException)
        {
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            Task exitTask = process.WaitForExitAsync(CancellationToken.None);
            Task timeoutTask = Task.Delay(timeout);
            return await Task.WhenAny(exitTask, timeoutTask) == exitTask;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
        }
    }

    private static void TryTraceProcess(Process process)
    {
        _ = ChildProcessTracerPeriodicTimer.Default.TryTraceProcess(process);
    }

    private static string FormatArguments(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(FormatArgument));
    }

    internal static bool CanRetryRecording(int completedAttempts)
    {
        return completedAttempts < MaxRecordingAttempts;
    }

    private static async Task<bool> WaitForProgressStallAsync(RecorderProgressTracker progressTracker, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                if (progressTracker.IsStalled(DateTime.UtcNow, ProgressStartupTimeout, ProgressStallTimeout))
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        return false;
    }

    internal static bool ShouldRetryRecording(int exitCode, bool hasStreamRefresh, bool? isLiveAfterRefresh, int offlineRefreshChecks)
    {
        bool offlineConfirmed = isLiveAfterRefresh == false && offlineRefreshChecks >= OfflineRefreshConfirmationCount;
        return !offlineConfirmed && (exitCode != 0 || hasStreamRefresh);
    }

    internal static bool ShouldConsumeReconnectAttempt(bool? isLiveAfterRefresh)
    {
        return isLiveAfterRefresh != true;
    }

    private static string FormatArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace) || argument.Contains('"')
            ? "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : argument;
    }

    private static string SelectInputUrl(RecorderStartInfo startInfo)
    {
        return SelectInputUrl(startInfo.RecordUrl, startInfo.HlsUrl, startInfo.FlvUrl);
    }

    private static string SelectInputUrl(string? recordUrl, string? hlsUrl, string? flvUrl)
    {
        if (!string.IsNullOrWhiteSpace(recordUrl))
        {
            return recordUrl;
        }

        if (!string.IsNullOrWhiteSpace(hlsUrl))
        {
            return hlsUrl;
        }

        return flvUrl ?? string.Empty;
    }

    private async Task<bool?> TryRefreshInputAsync(RecorderStartInfo startInfo, CancellationToken token)
    {
        lastStreamRefreshHadUrl = false;
        Func<CancellationToken, Task<RecorderStreamRefreshResult?>>? refreshStreamAsync = startInfo.RefreshStreamAsync;
        if (refreshStreamAsync == null)
        {
            return null;
        }

        RecorderStreamRefreshResult? refreshed;
        try
        {
            refreshed = await refreshStreamAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            AppSessionLogger.Event("warn", "recorder", "record_stream_refresh_failed", e.Message, new
            {
                startInfo.RoomUrl,
                startInfo.NickName,
                startInfo.PlatformName,
            });
            return null;
        }

        if (refreshed?.IsLiveStreaming == false)
        {
            AppSessionLogger.Event("info", "recorder", "record_stream_refresh_offline", "stream refresh returned an offline state", new
            {
                startInfo.RoomUrl,
                startInfo.NickName,
                startInfo.PlatformName,
            });
            return false;
        }

        if (refreshed == null)
        {
            return null;
        }

        string refreshedUrl = SelectInputUrl(refreshed.RecordUrl, refreshed.HlsUrl, refreshed.FlvUrl);
        if (string.IsNullOrWhiteSpace(refreshedUrl))
        {
            if (refreshed.IsLiveStreaming == true)
            {
                DateTime now = DateTime.UtcNow;
                if (now - lastLiveWithoutStreamLogAt >= TimeSpan.FromMinutes(1))
                {
                    lastLiveWithoutStreamLogAt = now;
                    AppSessionLogger.Event("warn", "recorder", "record_stream_refresh_live_without_url", "Douyin still reports the room as live but no new stream URL is available", new
                    {
                        startInfo.RoomUrl,
                        startInfo.NickName,
                        startInfo.PlatformName,
                        preservedInput = !string.IsNullOrWhiteSpace(Url),
                    });
                }
                return true;
            }
            return null;
        }

        bool urlChanged = !string.Equals(Url, refreshedUrl, StringComparison.Ordinal);
        lastStreamRefreshHadUrl = true;
        startInfo.RecordUrl = refreshed.RecordUrl;
        startInfo.HlsUrl = refreshed.HlsUrl;
        startInfo.FlvUrl = refreshed.FlvUrl;
        startInfo.Headers = refreshed.Headers;
        startInfo.Title = string.IsNullOrWhiteSpace(refreshed.Title) ? startInfo.Title : refreshed.Title;
        startInfo.Resolution = string.IsNullOrWhiteSpace(refreshed.Resolution) ? startInfo.Resolution : refreshed.Resolution;
        startInfo.Bitrate = string.IsNullOrWhiteSpace(refreshed.Bitrate) ? startInfo.Bitrate : refreshed.Bitrate;
        Url = refreshedUrl;

        AppSessionLogger.Event("info", "recorder", "record_stream_refreshed", "recording stream was refreshed after the media process exited", new
        {
            startInfo.RoomUrl,
            startInfo.NickName,
            startInfo.PlatformName,
            urlChanged,
            hasRecordUrl = !string.IsNullOrWhiteSpace(startInfo.RecordUrl),
            hasFlvUrl = !string.IsNullOrWhiteSpace(startInfo.FlvUrl),
            hasHlsUrl = !string.IsNullOrWhiteSpace(startInfo.HlsUrl),
        });
        return true;
    }

    private static string NormalizeHeaders(string? headers)
    {
        string value = (headers ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.EndsWith('\n') ? value : value + "\r\n";
    }

    private static bool IsHlsUrl(string url, RecorderStartInfo startInfo)
    {
        return url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || url == startInfo.RecordUrl && startInfo.RecordUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || url == startInfo.HlsUrl;
    }

    private static void DeleteFailedOutputFiles(string fileName, string? metadataPath)
    {
        foreach (string outputFile in GetRecordedSourceFilesForPattern(fileName))
        {
            try
            {
                File.Delete(outputFile);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (string.IsNullOrWhiteSpace(metadataPath))
        {
            return;
        }

        try
        {
            File.Delete(metadataPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public Task WaitForCompletionAsync()
    {
        lock (stateLock)
        {
            return recordingTask ?? Task.CompletedTask;
        }
    }

    internal static IReadOnlyList<string> BuildAudioMappingArguments(bool useOptimizedAudio)
    {
        return useOptimizedAudio
            ? [
                "-filter_complex", OptimizedAudioFilter,
                "-map", "0:v?",
                "-map", "0:a:0?",
                "-map", "[aopt]",
                "-c:v", "copy",
                "-c:a:0", "copy",
                "-c:a:1", "aac",
                "-metadata:s:a:0", "title=原音频",
                "-metadata:s:a:0", "handler_name=原音频",
                "-metadata:s:a:1", "title=优化音频",
                "-metadata:s:a:1", "handler_name=优化音频",
            ]
            : [
                "-map", "0",
                "-c:v", "copy",
                "-c:a", "copy",
            ];
    }

    internal static bool ShouldUseTransportStream(bool isHls, bool isToSegment, string? targetFormat)
    {
        return isHls || isToSegment || IsOptimizedTargetFormat(targetFormat);
    }

    internal static bool IsMissingAudioError(string? errorOutput)
    {
        return !string.IsNullOrWhiteSpace(errorOutput)
            && (errorOutput.Contains("matches no streams", StringComparison.OrdinalIgnoreCase)
                || errorOutput.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase)
                || errorOutput.Contains("cannot find a matching stream", StringComparison.OrdinalIgnoreCase)
                || errorOutput.Contains("streamcopy requested for output stream fed from a complex filtergraph", StringComparison.OrdinalIgnoreCase)
                || errorOutput.Contains("stream specifier ':a", StringComparison.OrdinalIgnoreCase));
    }

    internal static string? GetTargetFormat(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("->", StringComparison.Ordinal))
        {
            return null;
        }

        string target = value.Split("->", StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        return string.IsNullOrWhiteSpace(target) ? null : "." + target.TrimStart('.').ToLowerInvariant();
    }

    private static bool IsOptimizedTargetFormat(string? targetFormat)
    {
        return targetFormat is ".mkv" or ".mp4";
    }

    private static void AppendOutputTail(StringBuilder output, string data)
    {
        output.AppendLine(data);
        if (output.Length > ProcessOutputTailLimit)
        {
            output.Remove(0, output.Length - ProcessOutputTailLimit);
        }
    }

    private Task OnStandardErrorReceived(string data, CancellationToken token)
    {
        Debug.WriteLine(data);
        _ = WeakReferenceMessenger.Default.Send(new RecorderMessage()
        {
            DataType = StandardData.StandardError,
            Data = data,
        });
        return Task.CompletedTask;
    }

    private Task OnStandardOutputReceived(string data, CancellationToken token)
    {
        Debug.WriteLine(data);
        _ = WeakReferenceMessenger.Default.Send(new RecorderMessage()
        {
            DataType = StandardData.StandardOutput,
            Data = data,
        });
        return Task.CompletedTask;
    }

    private static string BuildSaveFolder(string saveFolder, string nickName, DateTime timestamp, int saveFolderPathLevel)
    {
        string safeNickName = nickName.SanitizeFileName().ReplaceTrailingDotsWithUnderscores();

        return Math.Clamp(saveFolderPathLevel, 0, 3) switch
        {
            2 => Path.Combine(saveFolder, safeNickName, timestamp.ToString("yyyy-MM")),
            3 => Path.Combine(saveFolder, safeNickName, timestamp.ToString("yyyy-MM"), timestamp.ToString("dd")),
            1 => Path.Combine(saveFolder, safeNickName),
            0 or _ => saveFolder,
        };
    }

    internal static string BuildOutputFileName(string saveFolder, RecorderStartInfo startInfo, DateTime timestamp, bool isToSegment, bool isHls)
    {
        string fileName = BuildBaseFileName(startInfo, timestamp).SanitizeFileName();
        return BuildOutputFileName(saveFolder, fileName, isToSegment, isHls);
    }

    private static string BuildOutputFileName(string saveFolder, string fileName, bool isToSegment, bool isHls)
    {
        string suffix = isToSegment ? "_%03d.ts" : isHls ? ".ts" : ".flv";
        return Path.Combine(saveFolder, $"{fileName}{suffix}");
    }

    internal static OutputReservation ReserveOutput(string saveFolder, string requestedBaseFileName, bool isToSegment, bool isHls)
    {
        lock (OutputReservationLock)
        {
            int suffix = 1;
            while (true)
            {
                string baseFileName = suffix == 1 ? requestedBaseFileName : $"{requestedBaseFileName}_{suffix}";
                string outputPattern = BuildOutputFileName(saveFolder, baseFileName, isToSegment, isHls);
                if (!ReservedOutputPatterns.Contains(outputPattern) && !OutputExists(saveFolder, baseFileName, outputPattern, isToSegment))
                {
                    ReservedOutputPatterns.Add(outputPattern);
                    return new OutputReservation(baseFileName, outputPattern);
                }

                suffix++;
            }
        }
    }

    private static bool OutputExists(string saveFolder, string baseFileName, string outputPattern, bool isToSegment)
    {
        if (File.Exists(Path.Combine(saveFolder, $"{baseFileName}.mplr.json")))
        {
            return true;
        }

        return isToSegment
            ? Directory.EnumerateFiles(saveFolder, $"{baseFileName}_*.ts", SearchOption.TopDirectoryOnly).Any()
            : File.Exists(outputPattern);
    }

    internal sealed class OutputReservation(string baseFileName, string outputPattern) : IDisposable
    {
        public string BaseFileName { get; } = baseFileName;

        public string OutputPattern { get; } = outputPattern;

        public void Dispose()
        {
            lock (OutputReservationLock)
            {
                ReservedOutputPatterns.Remove(OutputPattern);
            }
        }
    }

    internal static string BuildOutputFileName(string saveFolder, string nickName, DateTime timestamp, bool isToSegment, bool isHls)
    {
        return BuildOutputFileName(saveFolder, new RecorderStartInfo { NickName = nickName }, timestamp, isToSegment, isHls);
    }

    private static string BuildBaseFileName(RecorderStartInfo startInfo, DateTime timestamp)
    {
        const string defaultRule = "{主播名}_{录制时间}";
        string configuredRule = startInfo.Options.SaveFileNameCustomRule;
        string rule = string.IsNullOrWhiteSpace(configuredRule) ? defaultRule : configuredRule;

        return ApplyFileNameRule(rule, startInfo.NickName, timestamp, string.IsNullOrWhiteSpace(startInfo.PlatformName) ? "Emerde" : startInfo.PlatformName, startInfo.Resolution);
    }

    private static string BuildBaseFileName(string nickName, DateTime timestamp)
    {
        const string defaultRule = "{主播名}_{录制时间}";
        string configuredRule = Configurations.SaveFileNameCustomRule.Get();
        string rule = string.IsNullOrWhiteSpace(configuredRule) ? defaultRule : configuredRule;

        return ApplyFileNameRule(rule, nickName, timestamp, "Emerde", string.Empty);
    }

    private static string ApplyFileNameRule(string rule, string nickName, DateTime timestamp, string platformName, string resolution)
    {
        return rule
            .Replace("{主播名}", nickName, StringComparison.Ordinal)
            .Replace("{录制时间}", timestamp.ToString("yyyy-MM-dd_HH-mm-ss"), StringComparison.Ordinal)
            .Replace("{日期}", timestamp.ToString("yyyy-MM-dd"), StringComparison.Ordinal)
            .Replace("{时间}", timestamp.ToString("HH-mm-ss"), StringComparison.Ordinal)
            .Replace("{平台}", platformName, StringComparison.Ordinal)
            .Replace("{分辨率}", resolution, StringComparison.Ordinal)
            .Replace("{涓绘挱鍚峿", nickName, StringComparison.Ordinal)
            .Replace("{褰曞埗鏃堕棿}", timestamp.ToString("yyyy-MM-dd_HH-mm-ss"), StringComparison.Ordinal)
            .Replace("{鏃ユ湡}", timestamp.ToString("yyyy-MM-dd"), StringComparison.Ordinal)
            .Replace("{鏃堕棿}", timestamp.ToString("HH-mm-ss"), StringComparison.Ordinal)
            .Replace("{骞冲彴}", platformName, StringComparison.Ordinal)
            .Replace("{鍒嗚鲸鐜噠", resolution, StringComparison.Ordinal);
    }

    private static VideoRecordingMetadata BuildMetadata(string fileName, string outputExtension, RecorderStartInfo startInfo, DateTime timestamp)
    {
        return new VideoRecordingMetadata
        {
            FileName = $"{fileName}.{outputExtension}",
            NickName = startInfo.NickName,
            RoomUrl = startInfo.RoomUrl,
            Platform = startInfo.PlatformName,
            Title = startInfo.Title,
            Resolution = startInfo.Resolution,
            Bitrate = startInfo.Bitrate,
            CoverPath = startInfo.CoverPath,
            RecordedAt = timestamp,
        };
    }
}

public enum RecordStatus
{
    Initialized,
    Disabled,
    NotRecording,
    Recording,

    [Obsolete("Should retry recording instead of pushing an Error Status")]
    Error,
}

internal sealed class RecorderProgressTracker(DateTime startedAt)
{
    private readonly object syncRoot = new();
    private DateTime lastProgressAt = startedAt;
    private string lastMediaTime = string.Empty;
    private bool hasProgress;

    public bool Observe(string line, DateTime observedAt)
    {
        if (!line.StartsWith("out_time=", StringComparison.Ordinal))
        {
            return false;
        }

        string mediaTime = line["out_time=".Length..].Trim();
        if (string.IsNullOrWhiteSpace(mediaTime) || mediaTime.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        lock (syncRoot)
        {
            if (string.Equals(lastMediaTime, mediaTime, StringComparison.Ordinal))
            {
                return false;
            }

            bool isFirstProgress = !hasProgress;
            lastMediaTime = mediaTime;
            lastProgressAt = observedAt;
            hasProgress = true;
            return isFirstProgress;
        }
    }

    public bool IsStalled(DateTime now, TimeSpan startupTimeout, TimeSpan stallTimeout)
    {
        lock (syncRoot)
        {
            return now - lastProgressAt >= (hasProgress ? stallTimeout : startupTimeout);
        }
    }

    public TimeSpan GetStalledDuration(DateTime now)
    {
        lock (syncRoot)
        {
            return now > lastProgressAt ? now - lastProgressAt : TimeSpan.Zero;
        }
    }
}

public record RecorderStartInfo
{
    public string NickName { get; set; } = string.Empty;

    public string RoomUrl { get; set; } = string.Empty;

    public string PlatformName { get; set; } = string.Empty;

    public string Resolution { get; set; } = string.Empty;

    public string FlvUrl { get; set; } = string.Empty;

    public string HlsUrl { get; set; } = string.Empty;

    public string RecordUrl { get; set; } = string.Empty;

    public string Headers { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Bitrate { get; set; } = string.Empty;

    public string CoverPath { get; set; } = string.Empty;

    public RoomRecordingOptions Options { get; set; } = RoomRecordingSettings.GetGlobal();

    internal Func<RoomRecordingOptions>? ResolveCurrentOptions { get; set; }

    internal Func<CancellationToken, Task<RecorderStreamRefreshResult?>>? RefreshStreamAsync { get; set; }

    internal Action? OfflineConfirmed { get; set; }
}

internal sealed record RecorderStreamRefreshResult
{
    public bool? IsLiveStreaming { get; init; }

    public string RecordUrl { get; init; } = string.Empty;

    public string HlsUrl { get; init; } = string.Empty;

    public string FlvUrl { get; init; } = string.Empty;

    public string Headers { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Resolution { get; init; } = string.Empty;

    public string Bitrate { get; init; } = string.Empty;
}

public sealed class VideoRecordingMetadata
{
    public string FileName { get; set; } = string.Empty;

    public string NickName { get; set; } = string.Empty;

    public string RoomUrl { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Resolution { get; set; } = string.Empty;

    public string Bitrate { get; set; } = string.Empty;

    public string CoverPath { get; set; } = string.Empty;

    public DateTime RecordedAt { get; set; } = DateTime.MinValue;
}

file static class FileNameSanitizer
{
    private const int MaximumBaseFileNameLength = 120;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string SanitizeFileName(this string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Concat((fileName ?? string.Empty).Select(ch => invalidChars.Contains(ch) ? '_' : ch))
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "recording";
        }

        if (sanitized.Length > MaximumBaseFileNameLength)
        {
            int length = MaximumBaseFileNameLength;
            if (char.IsHighSurrogate(sanitized[length - 1]) && char.IsLowSurrogate(sanitized[length]))
            {
                length--;
            }
            sanitized = sanitized[..length].TrimEnd(' ', '.');
            if (sanitized.Length == 0)
            {
                sanitized = "recording";
            }
        }

        string reservedCandidate = sanitized.Split('.', 2)[0];
        return ReservedNames.Contains(reservedCandidate) ? $"_{sanitized}" : sanitized;
    }

    public static string ReplaceTrailingDotsWithUnderscores(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        int i = input.Length - 1;
        while (i >= 0 && input[i] == '.')
        {
            i--;
        }

        return string.Concat(input.AsSpan(0, i + 1), new string('_', input.Length - i - 1));
    }
}

file static class NoLinqExtension
{
    public static List<string> AddIf(this List<string> self, bool condition, params string[] items)
    {
        if (condition)
        {
            foreach (string item in items)
            {
                self.Add(item);
            }
        }

        return self;
    }
}
