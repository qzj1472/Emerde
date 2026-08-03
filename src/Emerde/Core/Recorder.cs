using CommunityToolkit.Mvvm.Messaging;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Emerde.Extensions;
using Emerde.Models;
using Emerde.Plugins;
using Emerde.Threading;

namespace Emerde.Core;

public sealed class Recorder
{
    private const int MaxRecordingAttempts = 4;
    internal const int OfflineRefreshConfirmationCount = 1;
    private const int ProcessOutputTailLimit = 8192;
    private static readonly TimeSpan ProgressStartupTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan ProgressStallTimeout = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan VideoProgressStallTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan MediaSpeedSummaryInterval = TimeSpan.FromSeconds(30);
    private const string OptimizedAudioFilter = "[0:a:0]volume=30dB,acompressor=threshold=-10dB:ratio=3,alimiter=limit=0.316227766:level=false[aopt]";

    internal static readonly TimeSpan ProcessStopGracePeriod = TimeSpan.FromSeconds(3);

    private static readonly object OutputReservationLock = new();

    private static readonly HashSet<string> ReservedOutputPatterns = new(StringComparer.OrdinalIgnoreCase);

    public RecordStatus RecordStatus { get; internal set; } = RecordStatus.Initialized;

    public CancellationTokenSource? TokenSource { get; private set; } = null;

    private readonly object stateLock = new();

    private RecorderStartInfo? activeStartInfo;

    private string activeRecordingId = string.Empty;

    private Task? recordingTask;

    private bool ownsTokenSource;

    private int stopRequested;

    private int deferPostProcessing;

    private int hasMediaProgress;

    private bool lastAttemptHadMediaProgress;

    private bool lastAttemptWasCanceled;

    private bool lastAttemptWasStalled;

    private bool lastAttemptWasRejectedByCrossStreamVerification;

    private double lastAttemptDurationSeconds;

    private string lastProcessErrorOutput = string.Empty;

    private bool lastStreamRefreshHadUrl;

    private DateTime lastLiveWithoutStreamLogAt = DateTime.MinValue;

    private readonly List<string> pendingRecordingPaths = [];

    private readonly List<string> unregisteredRecordingPatterns = [];

    private readonly List<(string SourcePattern, string TargetFormat)> unregisteredSessionRecordings = [];

    private IDisposable? mediaOperationRegistration;

    public bool IsBusy => recordingTask is { IsCompleted: false };

    public bool HasMediaProgress => Volatile.Read(ref hasMediaProgress) != 0;

    public string? Url { get; set; } = null;

    public string? FileName { get; set; } = null;

    public string? Parameters { get; set; } = null;

    public string? MetadataPath { get; set; } = null;

    public DateTime StartTime { get; private set; } = DateTime.MinValue;

    public DateTime RequestedAt { get; private set; } = DateTime.MinValue;

    public DateTime EndTime { get; private set; } = DateTime.MinValue;

    public bool IsToSegment { get; set; } = false;

    public int MediaWorkerProcessId { get; private set; }

    public string MediaWorkerProcessName { get; private set; } = string.Empty;

    public double MediaWorkerWriteBytesPerSecond { get; private set; }

    public double MediaWorkerReadBytesPerSecond { get; private set; }

    private long lastMediaProgressBytes;

    private long lastMediaInputBytes;

    private DateTime lastMediaProgressAt = DateTime.MinValue;

    private readonly MediaSpeedSummaryWindow mediaSpeedSummaryWindow = new(MediaSpeedSummaryInterval);

    private readonly object crossStreamVerificationLock = new();

    private Task<FfmpegCrossStreamAnalysisResult>? crossStreamVerificationTask;

    private CancellationTokenSource? crossStreamVerificationCancellation;

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
            unregisteredSessionRecordings.Clear();
            FileName = null;
            MetadataPath = null;
            MediaWorkerProcessId = 0;
            MediaWorkerProcessName = string.Empty;
            MediaWorkerWriteBytesPerSecond = 0;
            MediaWorkerReadBytesPerSecond = 0;
            lastMediaProgressBytes = 0;
            lastMediaInputBytes = 0;
            lastMediaProgressAt = DateTime.MinValue;
            mediaSpeedSummaryWindow.Reset();
            lastAttemptWasRejectedByCrossStreamVerification = false;
            CancelCrossStreamVerification();
            RequestedAt = DateTime.Now;
            StartTime = DateTime.MinValue;
            EndTime = DateTime.MinValue;
            RecordStatus = RecordStatus.Recording;
            activeStartInfo = startInfo;
            activeRecordingId = Guid.NewGuid().ToString("N");
            TokenSource = tokenSource ?? new CancellationTokenSource();
            ownsTokenSource = tokenSource == null;
            CancellationToken recordingToken = TokenSource.Token;
            mediaOperationRegistration = MediaOperationRegistry.Register(
                MediaOperationKind.Recording,
                () => [FileName],
                () => Stop(deferPostProcessing: true));
            try
            {
                recordingTask = Task.Factory.StartNew(
                    () => RunAsync(startInfo, recordingToken),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                ).Unwrap();
            }
            catch
            {
                mediaOperationRegistration.Dispose();
                mediaOperationRegistration = null;
                if (ownsTokenSource)
                {
                    TokenSource.Dispose();
                }
                TokenSource = null;
                ownsTokenSource = false;
                activeStartInfo = null;
                activeRecordingId = string.Empty;
                RecordStatus = RecordStatus.NotRecording;
                throw;
            }
            return recordingTask;
        }
    }

    private async Task RunAsync(RecorderStartInfo startInfo, CancellationToken token)
    {
        RoomRecordingOptions recordingOptions = startInfo.Options;
        OutputReservation? sessionOutputReservation = null;
        List<string> queuedPostProcessingPaths = [];
        try
        {
            if (!FfmpegMediaEngine.IsAvailable)
            {
                RecordStatus = RecordStatus.NotRecording;
                AppSessionLogger.Event("error", "recorder", "recorder_missing", "ffmpeg native libraries were not found", new
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
            FfmpegSegmentOptions? segmentOptions = isToSegment
                ? new FfmpegSegmentOptions(segmentTime, segmentTimeUnit)
                : null;
            string headers = NormalizeHeaders(startInfo.Headers);
            string? targetFormat = GetTargetFormat(recordingOptions.RecordFormat);

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
            bool useTransportStream = ShouldUseTransportStream(isHls, isToSegment, targetFormat);
            bool useSessionPartFiles = !isToSegment;
            string sessionSourceExtension = useTransportStream ? "ts" : "flv";
            string sessionTargetFormat = targetFormat ?? "." + sessionSourceExtension;

            if (string.IsNullOrWhiteSpace(userAgent))
            {
                userAgent = "Mozilla/5.0 (Linux; Android 11; SAMSUNG SM-G973U) AppleWebKit/537.36 ("
                          + "KHTML, like Gecko) SamsungBrowser/14.2 Chrome/87.0.4280.141 Mobile "
                          + "Safari/537.36";
            }

            EndTime = DateTime.MinValue;
            int attempt = 0;
            int offlineRefreshChecks = 0;
            int sessionPartIndex = 0;
            bool hasTriedInputFallback = false;
            DateTime sessionTimestamp = DateTime.Now;
            string? sessionOutputPattern = null;
            string? sessionBaseFileName = null;
            string? sessionMetadataPath = null;
            VideoRecordingMetadata? sessionMetadata = null;
            if (useSessionPartFiles)
            {
                string sessionRequestedBaseFileName = BuildBaseFileName(startInfo, sessionTimestamp).SanitizeFileName();
                sessionOutputReservation = ReserveSessionOutput(saveFolder, sessionRequestedBaseFileName, sessionSourceExtension);
                sessionBaseFileName = sessionOutputReservation.BaseFileName;
                sessionOutputPattern = sessionOutputReservation.OutputPattern;
                sessionMetadata = BuildMetadata(sessionBaseFileName, sessionSourceExtension, startInfo, sessionTimestamp);
                sessionMetadataPath = VideoRecordingMetadataStore.WriteSidecar(saveFolder, sessionBaseFileName, sessionMetadata);
                string? pendingRecordingPath = RecordingRecoveryService.RegisterSessionParts(
                    sessionOutputPattern,
                    sessionTargetFormat,
                    recordingOptions.IsRemoveTs,
                    startInfo.RoomUrl,
                    recordingOptions.IsOptimizeAudio);
                if (!string.IsNullOrWhiteSpace(pendingRecordingPath))
                {
                    pendingRecordingPaths.Add(pendingRecordingPath);
                }
                else
                {
                    unregisteredSessionRecordings.Add((sessionOutputPattern, sessionTargetFormat));
                }
            }
            while (!token.IsCancellationRequested && Volatile.Read(ref stopRequested) == 0)
            {
                DateTime now = DateTime.Now;
                using OutputReservation? outputReservation = useSessionPartFiles
                    ? null
                    : ReserveOutput(saveFolder, BuildBaseFileName(startInfo, now).SanitizeFileName(), isToSegment, useTransportStream);
                string outputFileName;
                VideoRecordingMetadata metadata;
                if (useSessionPartFiles)
                {
                    FileName = sessionOutputPattern!;
                    MetadataPath = sessionMetadataPath;
                    metadata = sessionMetadata!;
                    outputFileName = BuildSessionPartOutputFileName(sessionOutputPattern!, sessionPartIndex);
                }
                else
                {
                    string baseFileName = outputReservation!.BaseFileName;
                    FileName = outputReservation.OutputPattern;
                    metadata = BuildMetadata(baseFileName, useTransportStream ? "ts" : "flv", startInfo, now);
                    MetadataPath = VideoRecordingMetadataStore.WriteSidecar(saveFolder, baseFileName, metadata);
                    string? pendingRecordingPath = RecordingRecoveryService.Register(FileName, recordingOptions, startInfo.RoomUrl);
                    if (!string.IsNullOrWhiteSpace(pendingRecordingPath))
                    {
                        pendingRecordingPaths.Add(pendingRecordingPath);
                    }
                    else
                    {
                        unregisteredRecordingPatterns.Add(FileName);
                    }
                    outputFileName = FileName;
                }
                bool useOptimizedAudio = false;

                List<string> arguments = BuildArguments(
                    outputFileName,
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
                    outputFileName,
                    isToSegment,
                    useSessionPartFiles,
                    useOptimizedAudio,
                    isUseProxy,
                    attempt,
                });

                int exitCode = await ExecuteRecorderAsync(
                    outputFileName,
                    isUseProxy,
                    httpProxy,
                    headers,
                    userAgent,
                    metadata,
                    segmentOptions,
                    startInfo,
                    token);
                if (lastAttemptWasRejectedByCrossStreamVerification)
                {
                    DeleteFailedOutputFiles(outputFileName, useSessionPartFiles ? null : MetadataPath);
                    AppSessionLogger.Event("warn", "recorder", "record_quarantine_segment_discarded", "cross-stream verification rejected the temporary recording part", new
                    {
                        startInfo.RoomUrl,
                        startInfo.NickName,
                        outputFileName,
                    });
                }
                DeleteEmptyOutputFiles(outputFileName);
                bool hasSessionOutput = useSessionPartFiles && HasUsableOutput(outputFileName);
                if (!useSessionPartFiles)
                {
                    DeleteMetadataIfNoOutput(FileName, MetadataPath);
                }
                if (token.IsCancellationRequested || Volatile.Read(ref stopRequested) != 0)
                {
                    if (!useSessionPartFiles)
                    {
                        FinalizeMetadataForOutput(FileName, MetadataPath);
                    }
                    break;
                }

                string? fallbackUrl = SelectInputFallback(
                    startInfo.PlatformName,
                    Url,
                    startInfo.HlsUrl,
                    startInfo.FlvUrl,
                    lastAttemptHadMediaProgress,
                    hasTriedInputFallback);
                if (!string.IsNullOrWhiteSpace(fallbackUrl))
                {
                    string failedUrl = Url;
                    DeleteFailedOutputFiles(outputFileName, useSessionPartFiles ? null : MetadataPath);
                    Url = fallbackUrl;
                    startInfo.RecordUrl = fallbackUrl;
                    hasTriedInputFallback = true;
                    isHls = IsHlsUrl(Url, startInfo);
                    if (!useSessionPartFiles)
                    {
                        useTransportStream = ShouldUseTransportStream(isHls, isToSegment, targetFormat);
                    }
                    AppSessionLogger.Event("warn", "recorder", "record_input_fallback", "recording switched to the fallback stream before media started", new
                    {
                        startInfo.RoomUrl,
                        startInfo.NickName,
                        startInfo.PlatformName,
                        failedInputKind = IsHlsUrl(failedUrl, startInfo) ? "hls" : "flv",
                        fallbackInputKind = isHls ? "hls" : "flv",
                    });
                    continue;
                }

                if (hasSessionOutput)
                {
                    sessionPartIndex++;
                }

                if (!useSessionPartFiles)
                {
                    FinalizeMetadataForOutput(FileName, MetadataPath);
                }

                bool hasStreamRefresh = startInfo.RefreshStreamAsync != null;
                bool? isLiveAfterRefresh = await TryRefreshInputAsync(startInfo, token);
                if (ShouldSuppressRapidRetry(exitCode, lastAttemptWasCanceled, lastAttemptWasStalled, lastAttemptDurationSeconds, isLiveAfterRefresh))
                {
                    AppSessionLogger.Event("warn", "recorder", "record_rapid_retry_suppressed", "rapid recording retry was suppressed", new
                    {
                        startInfo.RoomUrl,
                        startInfo.NickName,
                        exitCode,
                        durationSeconds = lastAttemptDurationSeconds,
                        isLiveAfterRefresh,
                    });
                    startInfo.RapidExitDetected?.Invoke();
                    break;
                }
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
                    if (hasTriedInputFallback
                        && string.Equals(startInfo.PlatformName, "Bilibili", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(startInfo.FlvUrl))
                    {
                        Url = startInfo.FlvUrl;
                        startInfo.RecordUrl = startInfo.FlvUrl;
                    }
                    headers = NormalizeHeaders(startInfo.Headers);
                    isHls = IsHlsUrl(Url!, startInfo);
                    if (!useSessionPartFiles)
                    {
                        useTransportStream = ShouldUseTransportStream(isHls, isToSegment, targetFormat);
                    }
                }

                if (ShouldConsumeReconnectAttempt(isLiveAfterRefresh, lastAttemptHadMediaProgress))
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
                        lastAttemptHadMediaProgress,
                    });
                    PublishLifecycle("reconnect_exhausted", startInfo, attempt);
                    startInfo.ReconnectExhausted?.Invoke();
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
                ExtensionRecorderReconnectRequest reconnectRequest = new(
                    startInfo.RoomUrl,
                    startInfo.NickName,
                    startInfo.PlatformName,
                    FileName ?? string.Empty,
                    attempt,
                    exitCode,
                    lastAttemptHadMediaProgress,
                    delay);
                ExtensionRecorderReconnectDecision reconnectDecision = ExtensionHostRuntime.InvokeOverrideChain<ExtensionRecorderReconnectOverride, ExtensionRecorderReconnectDecision>(
                    ExtensionContractNames.RecorderReconnect,
                    (implementation, next) => implementation(reconnectRequest, next),
                    () => new ExtensionRecorderReconnectDecision(true, delay),
                    AppSessionLogger.WriteException)
                    ?? new ExtensionRecorderReconnectDecision(true, delay);
                if (!reconnectDecision.ShouldRetry)
                {
                    PublishLifecycle("reconnect_cancelled", startInfo, attempt);
                    break;
                }
                delay = reconnectDecision.Delay < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : reconnectDecision.Delay > TimeSpan.FromMinutes(5)
                        ? TimeSpan.FromMinutes(5)
                        : reconnectDecision.Delay;
                AppSessionLogger.Event("warn", "recorder", "record_reconnect_scheduled", "record reconnect scheduled", new
                {
                    startInfo.RoomUrl,
                    startInfo.NickName,
                    exitCode,
                    attempt,
                    useSessionPartFiles,
                    delaySeconds = delay.TotalSeconds,
                });
                PublishLifecycle("reconnect_scheduled", startInfo, attempt);
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
                FinalizeMetadataForOutput(FileName ?? string.Empty, MetadataPath);
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
                PublishLifecycle("stopped", startInfo);
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
                        queuedPostProcessingPaths.Add(pendingRecordingPath);
                    }
                }

                foreach (string sourcePattern in unregisteredRecordingPatterns)
                {
                    string? pendingPath = RecordingRecoveryService.Register(sourcePattern, postProcessingOptions, startInfo.RoomUrl);
                    if (!string.IsNullOrWhiteSpace(pendingPath))
                    {
                        pendingRecordingPaths.Add(pendingPath);
                        if (processNow)
                        {
                            queuedPostProcessingPaths.Add(pendingPath);
                        }
                    }
                }

                foreach ((string sourcePattern, string format) in unregisteredSessionRecordings)
                {
                    string latestTargetFormat = GetTargetFormat(postProcessingOptions.RecordFormat)
                        ?? Path.GetExtension(sourcePattern);
                    string? pendingPath = RecordingRecoveryService.RegisterSessionParts(
                        sourcePattern,
                        latestTargetFormat,
                        postProcessingOptions.IsRemoveTs,
                        startInfo.RoomUrl,
                        postProcessingOptions.IsOptimizeAudio);
                    if (!string.IsNullOrWhiteSpace(pendingPath))
                    {
                        pendingRecordingPaths.Add(pendingPath);
                        if (processNow)
                        {
                            queuedPostProcessingPaths.Add(pendingPath);
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
                    PublishLifecycle("post_processing_deferred", startInfo);
                }
                if (!processNow)
                {
                    RecordingCleanupService.QueueRun();
                }
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
            finally
            {
                sessionOutputReservation?.Dispose();
                bool postProcessingQueued = false;
                if (queuedPostProcessingPaths.Count > 0
                    && Volatile.Read(ref deferPostProcessing) == 0)
                {
                    try
                    {
                        string[] pendingPaths = queuedPostProcessingPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        ExtensionPostProcessingRequest request = new(
                            startInfo.RoomUrl,
                            startInfo.NickName,
                            startInfo.PlatformName,
                            pendingPaths);
                        IDisposable handoffProtection = MediaOperationRegistry.Register(
                            MediaOperationKind.Conversion,
                            () => pendingPaths);
                        Task dispatchTask = ExtensionHostRuntime.InvokeOverrideChainAsync<ExtensionPostProcessingOverride>(
                            ExtensionContractNames.PostProcessing,
                            (implementation, next) => implementation(request, next),
                            () => RecordingRecoveryService.QueueProcessAsync(pendingPaths),
                            AppSessionLogger.WriteException);
                        _ = ReleasePostProcessingHandoffAsync(dispatchTask, handoffProtection);
                        postProcessingQueued = true;
                        PublishLifecycle("post_processing_queued", startInfo);
                    }
                    catch (Exception e)
                    {
                        AppSessionLogger.WriteException(e);
                    }
                }
                mediaOperationRegistration?.Dispose();
                mediaOperationRegistration = null;
                CancelCrossStreamVerification();
                lock (stateLock)
                {
                    if (ownsTokenSource)
                    {
                        TokenSource?.Dispose();
                    }
                    TokenSource = null;
                    ownsTokenSource = false;
                    activeStartInfo = null;
                    activeRecordingId = string.Empty;
                }
                if (!postProcessingQueued)
                {
                    RecordingCleanupService.QueueRun();
                }
            }
        }
    }

    public void Stop(bool deferPostProcessing = false)
    {
        RecorderStartInfo? startInfo;
        lock (stateLock)
        {
            startInfo = activeStartInfo;
        }
        ExtensionRecorderStopRequest request = new(
            startInfo?.RoomUrl ?? string.Empty,
            startInfo?.NickName ?? string.Empty,
            startInfo?.PlatformName ?? string.Empty,
            FileName ?? string.Empty,
            deferPostProcessing);
        _ = ExtensionHostRuntime.InvokeOverrideChain<ExtensionRecorderStopOverride, bool>(
            ExtensionContractNames.RecorderStop,
            (implementation, next) => implementation(request, next),
            () => StopCore(deferPostProcessing),
            AppSessionLogger.WriteException);
    }

    private bool StopCore(bool deferPostProcessing)
    {
        if (deferPostProcessing)
        {
            Interlocked.Exchange(ref this.deferPostProcessing, 1);
        }
        Interlocked.Exchange(ref stopRequested, 1);
        lock (stateLock)
        {
            TokenSource?.Cancel();
            if (RecordStatus == RecordStatus.Recording)
            {
                EndTime = DateTime.Now;
                RecordStatus = RecordStatus.NotRecording;
            }
        }
        return true;
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
            "-fflags", "+genpts+discardcorrupt+sortdts",
            "-err_detect", "ignore_err",
        ];

        arguments
            .AddIf(isUseProxy, "-http_proxy", httpProxy)
            .AddIf(!string.IsNullOrWhiteSpace(headers), "-headers", headers)
            .AddIf(true,
                "-reconnect_delay_max", "8",
                "-reconnect_delay_total_max", "90",
                "-reconnect_max_retries", "12",
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

    private async Task<int> ExecuteRecorderAsync(
        string outputFileName,
        bool isUseProxy,
        string httpProxy,
        string headers,
        string userAgent,
        VideoRecordingMetadata metadata,
        FfmpegSegmentOptions? segmentOptions,
        RecorderStartInfo startInfo,
        CancellationToken token)
    {
        lastAttemptHadMediaProgress = false;
        lastAttemptWasCanceled = false;
        lastAttemptWasStalled = false;
        lastAttemptWasRejectedByCrossStreamVerification = false;
        lastAttemptDurationSeconds = 0;
        Stopwatch processLifetime = Stopwatch.StartNew();
        FfmpegInputOptions inputOptions = new(userAgent, headers, isUseProxy, httpProxy, true);
        AppSessionLogger.Event("info", "recorder", "record_native_starting", "ffmpeg native recording is starting", new
        {
            startInfo.PlatformName,
            FileName,
            outputFileName,
        });
        bool wasCanceled = false;
        bool wasStalled = false;
        int exitCode = 1;
        string commandPath = string.Empty;
        StringBuilder errorOutput = new();
        Process? process = null;
        RecorderProgressTracker? progressTracker = null;

        try
        {
            commandPath = MediaWorker.WriteCommand(Url ?? string.Empty, outputFileName, metadata, inputOptions, segmentOptions);
            process = StartMediaWorkerProcess(commandPath);
            MediaWorkerProcessId = process.Id;
            MediaWorkerProcessName = process.ProcessName;
            AppSessionLogger.Event("info", "recorder", "record_media_worker_started", "media worker process started", new
            {
                startInfo.RoomUrl,
                startInfo.NickName,
                startInfo.PlatformName,
                workerProcessId = process.Id,
                workerProcessName = process.ProcessName,
                FileName,
                outputFileName,
            });
            _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(startInfo.RoomUrl));
            TryTraceProcess(process);
            progressTracker = new(DateTime.UtcNow);
            using CancellationTokenSource processCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task outputTask = ReadMediaWorkerOutputAsync(
                process.StandardOutput,
                progressTracker,
                startInfo,
                outputFileName,
                processCancellation.Token,
                token);
            Task errorTask = ReadMediaWorkerErrorAsync(process.StandardError, errorOutput, processCancellation.Token);
            Task exitTask = process.WaitForExitAsync(CancellationToken.None);
            Task<bool> stallTask = WaitForProgressStallAsync(progressTracker, processCancellation.Token);
            Task cancellationTask = WaitForCancellationAsync(processCancellation.Token);
            while (true)
            {
                Task<FfmpegCrossStreamAnalysisResult>? verificationTask = GetCrossStreamVerificationTask();
                Task completedTask = verificationTask == null
                    ? await Task.WhenAny(exitTask, stallTask, cancellationTask)
                    : await Task.WhenAny(exitTask, stallTask, cancellationTask, verificationTask);
                if (completedTask == verificationTask)
                {
                    FfmpegCrossStreamAnalysisResult verification = await verificationTask!;
                    CompleteCrossStreamVerification(verificationTask!);
                    LogCrossStreamVerificationResult(startInfo, outputFileName, verification);
                    if (verification.IsConclusive && verification.ShouldRestart && !token.IsCancellationRequested)
                    {
                        wasStalled = true;
                        lastAttemptWasRejectedByCrossStreamVerification = true;
                        AppSessionLogger.Event("warn", "recorder", "record_restart_scheduled", "cross-stream verification confirmed a persistent mismatch", new
                        {
                            startInfo.RoomUrl,
                            startInfo.NickName,
                            outputFileName,
                            verification.TimelineDifferenceSeconds,
                            verification.Confidence,
                            verification.Reason,
                        });
                        AppSessionLogger.Event("warn", "recorder", "record_restart_executed", "the recording worker was stopped for a verified clean restart", new
                        {
                            startInfo.RoomUrl,
                            startInfo.NickName,
                            outputFileName,
                        });
                        KillProcessTree(process);
                        break;
                    }
                    continue;
                }
                if (completedTask == stallTask && await stallTask)
                {
                    wasStalled = true;
                    RecorderStallReason stallReason = progressTracker.GetStallReason(
                        DateTime.UtcNow,
                        ProgressStartupTimeout,
                        ProgressStallTimeout,
                        VideoProgressStallTimeout);
                    AppSessionLogger.Event("warn", "recorder", "record_progress_stalled", "recording media progress stalled and worker will be restarted", new
                    {
                        startInfo.RoomUrl,
                        startInfo.NickName,
                        workerProcessId = process.Id,
                        FileName,
                        outputFileName,
                        stalledSeconds = progressTracker.GetStalledDuration(DateTime.UtcNow, stallReason).TotalSeconds,
                        stallReason = stallReason.ToString().ToLowerInvariant(),
                    });
                    _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(startInfo.RoomUrl));
                    KillProcessTree(process);
                    break;
                }
                if (completedTask == cancellationTask && token.IsCancellationRequested)
                {
                    wasCanceled = true;
                    RequestProcessExit(process);
                    if (!await WaitForExitAsync(process, ProcessStopGracePeriod))
                    {
                        KillProcessTree(process);
                    }
                    break;
                }
                break;
            }

            await exitTask;
            processCancellation.Cancel();
            await Task.WhenAll(outputTask, errorTask);
            wasStalled |= process.ExitCode == MediaWorker.TimelineRestartExitCode;
            exitCode = wasStalled ? 1 : process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            wasCanceled = true;
            exitCode = 255;
        }
        catch (Exception e)
        {
            AppendOutputTail(errorOutput, e.ToString());
            exitCode = 1;
        }
        finally
        {
            if (process != null)
            {
                KillProcessTree(process);
                process.Dispose();
            }
            FlushMediaWorkerSpeedSummary(startInfo, outputFileName);
            if (!string.IsNullOrWhiteSpace(commandPath))
            {
                DeleteFileIfExists(commandPath);
            }
            MediaWorkerProcessId = 0;
            MediaWorkerProcessName = string.Empty;
            MediaWorkerWriteBytesPerSecond = 0;
            MediaWorkerReadBytesPerSecond = 0;
            lastMediaProgressBytes = 0;
            lastMediaInputBytes = 0;
            lastMediaProgressAt = DateTime.MinValue;
            mediaSpeedSummaryWindow.Reset();
        }

        wasCanceled = wasCanceled || token.IsCancellationRequested;
        lastAttemptHadMediaProgress = progressTracker?.HasProgress == true;
        lastAttemptWasCanceled = wasCanceled;
        lastAttemptWasStalled = wasStalled;
        processLifetime.Stop();
        lastProcessErrorOutput = errorOutput.ToString();
        double durationSeconds = processLifetime.Elapsed.TotalSeconds;
        lastAttemptDurationSeconds = durationSeconds;
        AppSessionLogger.Event(GetProcessExitLogLevel(exitCode, wasCanceled, wasStalled), "recorder", "record_process_exited", "ffmpeg native recording exited", new
        {
            startInfo.RoomUrl,
            startInfo.NickName,
            ExitCode = exitCode,
            wasCanceled,
            wasStalled,
            FileName,
            durationSeconds,
            errorOutput = exitCode == 0 ? string.Empty : lastProcessErrorOutput,
        });
        if (ShouldLogRapidExit(wasCanceled, wasStalled, durationSeconds))
        {
            AppSessionLogger.Event("warn", "recorder", "record_rapid_exit", "ffmpeg recording process exited in less than one minute", new
            {
                startInfo.RoomUrl,
                startInfo.NickName,
                ExitCode = exitCode,
                FileName,
                durationSeconds,
                hasErrorOutput = !string.IsNullOrWhiteSpace(lastProcessErrorOutput),
            });
        }

        if (wasCanceled)
        {
            throw new OperationCanceledException(token);
        }

        return exitCode;
    }

    private static Process StartMediaWorkerProcess(string commandPath)
    {
        ProcessStartInfo processStartInfo = new()
        {
            FileName = GetApplicationExecutablePath(),
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        processStartInfo.ArgumentList.Add(MediaWorker.ModeArgument);
        processStartInfo.ArgumentList.Add(commandPath);
        Process process = new() { StartInfo = processStartInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("media worker process did not start");
        }
        return process;
    }

    private void StartCrossStreamVerification(RecorderStartInfo startInfo, CancellationToken token)
    {
        AppSessionLogger.Event("warn", "recorder", "record_short_stall_detected", "a short audio-video timeline stall started cross-stream verification", new
        {
            startInfo.RoomUrl,
            startInfo.NickName,
            selectedUrlAvailable = !string.IsNullOrWhiteSpace(Url),
            referenceUrlAvailable = !string.IsNullOrWhiteSpace(startInfo.ReferenceUrl),
        });
        if (string.IsNullOrWhiteSpace(Url) || string.IsNullOrWhiteSpace(startInfo.ReferenceUrl))
        {
            AppSessionLogger.Event("warn", "recorder", "record_reference_stream_unavailable", "lower-quality reference stream is unavailable and local timeline recovery will be used", new
            {
                startInfo.RoomUrl,
                startInfo.NickName,
            });
            return;
        }

        lock (crossStreamVerificationLock)
        {
            if (crossStreamVerificationTask != null)
            {
                return;
            }

            crossStreamVerificationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            string selectedUrl = Url;
            string referenceUrl = startInfo.ReferenceUrl;
            crossStreamVerificationTask = RunCrossStreamVerificationAsync(
                selectedUrl,
                referenceUrl,
                CreateCrossStreamInputOptions(startInfo),
                crossStreamVerificationCancellation.Token);
            AppSessionLogger.Event("info", "recorder", "record_reference_stream_started", "lower-quality reference stream verification started", new
            {
                startInfo.RoomUrl,
                startInfo.NickName,
                maximumSeconds = 30,
            });
        }
    }

    private static FfmpegInputOptions CreateCrossStreamInputOptions(RecorderStartInfo startInfo)
    {
        string proxy = ProxyAddress.Normalize(Configurations.ProxyUrl.Get());
        bool useProxy = Configurations.IsUseProxy.Get() && !string.IsNullOrWhiteSpace(proxy);
        string userAgent = Configurations.UserAgent.Get();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            userAgent = "Mozilla/5.0 (Linux; Android 11; SAMSUNG SM-G973U) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/14.2 Chrome/87.0.4280.141 Mobile Safari/537.36";
        }
        return new(userAgent, RemoveCookieHeaders(startInfo.Headers), useProxy, proxy, true);
    }

    private static async Task<FfmpegCrossStreamAnalysisResult> RunCrossStreamVerificationAsync(
        string selectedUrl,
        string referenceUrl,
        FfmpegInputOptions inputOptions,
        CancellationToken token)
    {
        string commandPath = string.Empty;
        Process? process = null;
        try
        {
            commandPath = MediaWorker.WriteCrossStreamCommand(
                selectedUrl,
                referenceUrl,
                inputOptions,
                TimeSpan.FromSeconds(30));
            process = StartMediaWorkerProcess(commandPath);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            string output = await outputTask;
            string error = await errorTask;
            string? resultLine = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(line => line.StartsWith("cross|", StringComparison.Ordinal));
            if (resultLine == null)
            {
                return new(false, false, 0, 0, string.Empty, string.IsNullOrWhiteSpace(error) ? "cross-stream worker returned no result" : error.Trim());
            }

            return System.Text.Json.JsonSerializer.Deserialize<FfmpegCrossStreamAnalysisResult>(resultLine[6..])
                ?? new(false, false, 0, 0, string.Empty, "cross-stream worker returned an invalid result");
        }
        catch (OperationCanceledException)
        {
            return new(false, false, 0, 0, string.Empty, "cross-stream analysis was canceled");
        }
        catch (Exception e)
        {
            return new(false, false, 0, 0, string.Empty, e.Message);
        }
        finally
        {
            if (process != null)
            {
                KillProcessTree(process);
                process.Dispose();
            }
            if (!string.IsNullOrWhiteSpace(commandPath))
            {
                DeleteFileIfExists(commandPath);
            }
        }
    }

    private Task<FfmpegCrossStreamAnalysisResult>? GetCrossStreamVerificationTask()
    {
        lock (crossStreamVerificationLock)
        {
            return crossStreamVerificationTask;
        }
    }

    private void CompleteCrossStreamVerification(Task<FfmpegCrossStreamAnalysisResult> task)
    {
        lock (crossStreamVerificationLock)
        {
            if (!ReferenceEquals(crossStreamVerificationTask, task))
            {
                return;
            }
            crossStreamVerificationTask = null;
            crossStreamVerificationCancellation?.Dispose();
            crossStreamVerificationCancellation = null;
        }
    }

    private void CancelCrossStreamVerification()
    {
        lock (crossStreamVerificationLock)
        {
            crossStreamVerificationCancellation?.Cancel();
            crossStreamVerificationCancellation?.Dispose();
            crossStreamVerificationCancellation = null;
            crossStreamVerificationTask = null;
        }
    }

    private static void LogCrossStreamVerificationResult(
        RecorderStartInfo startInfo,
        string outputFileName,
        FfmpegCrossStreamAnalysisResult result)
    {
        AppSessionLogger.Event(result.IsConclusive ? "info" : "warn", "recorder", "record_cross_stream_offset_measured", "cross-stream timeline verification completed", new
        {
            startInfo.RoomUrl,
            startInfo.NickName,
            outputFileName,
            result.TimelineDifferenceSeconds,
            result.Confidence,
            result.Reason,
            result.Error,
        });
        string action = result.IsConclusive
            ? result.ShouldRestart ? "record_timeline_offset_persisted" : "record_restart_cancelled"
            : "record_cross_stream_inconclusive";
        string message = result.IsConclusive
            ? result.ShouldRestart ? "cross-stream mismatch persisted and requires an internal restart" : "cross-stream timelines recovered and the internal restart was cancelled"
            : "cross-stream verification was inconclusive and local timeline recovery remains active";
        AppSessionLogger.Event(result.IsConclusive && !result.ShouldRestart ? "info" : "warn", "recorder", action, message, new
        {
            startInfo.RoomUrl,
            startInfo.NickName,
            outputFileName,
            result.IsConclusive,
            result.ShouldRestart,
            result.TimelineDifferenceSeconds,
            result.Confidence,
            result.Reason,
            result.Error,
        });
        if (result.IsConclusive && !result.ShouldRestart)
        {
            AppSessionLogger.Event("info", "recorder", "record_quarantine_segment_kept", "the temporary recording part remained aligned and was kept", new
            {
                startInfo.RoomUrl,
                startInfo.NickName,
                outputFileName,
            });
        }
    }

    private static string GetApplicationExecutablePath()
    {
        return Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("application executable path is unavailable");
    }

    private async Task ReadMediaWorkerOutputAsync(
        StreamReader reader,
        RecorderProgressTracker progressTracker,
        RecorderStartInfo startInfo,
        string outputFileName,
        CancellationToken readToken,
        CancellationToken verificationToken)
    {
        try
        {
            while (!readToken.IsCancellationRequested && await reader.ReadLineAsync(readToken) is { } line)
            {
                if (TryParseMediaWorkerTimelineEvent(
                    line,
                    out FfmpegTimelineEventKind timelineEvent,
                    out long gapMicroseconds,
                    out long timelineVideoPackets,
                    out long timelineAudioPackets))
                {
                    bool videoStalled = timelineEvent == FfmpegTimelineEventKind.VideoStalled;
                    bool audioStalled = timelineEvent == FfmpegTimelineEventKind.AudioStalled;
                    string action = timelineEvent switch
                    {
                        FfmpegTimelineEventKind.VideoStalled => "record_video_timeline_stalled",
                        FfmpegTimelineEventKind.AudioStalled => "record_audio_timeline_stalled",
                        FfmpegTimelineEventKind.InitialAligned => "record_initial_timeline_aligned",
                        _ => "record_video_timeline_recovered",
                    };
                    string message = timelineEvent switch
                    {
                        FfmpegTimelineEventKind.VideoStalled => "audio continued while video packets stopped",
                        FfmpegTimelineEventKind.AudioStalled => "video continued while audio packets stopped",
                        FfmpegTimelineEventKind.InitialAligned => "video and audio startup timelines were aligned before recording output",
                        _ => "video timeline resumed on a keyframe and was aligned with audio",
                    };
                    AppSessionLogger.Event(
                        videoStalled || audioStalled ? "warn" : "info",
                        "recorder",
                        action,
                        message,
                        new
                        {
                            startInfo.RoomUrl,
                            startInfo.NickName,
                            FileName,
                            outputFileName,
                            gapSeconds = gapMicroseconds / 1_000_000d,
                            videoPackets = timelineVideoPackets,
                            audioPackets = timelineAudioPackets,
                        });
                    if (videoStalled || audioStalled)
                    {
                        StartCrossStreamVerification(startInfo, verificationToken);
                    }
                    continue;
                }
                if (line.StartsWith("progress", StringComparison.Ordinal))
                {
                    DateTime now = DateTime.UtcNow;
                    bool advanced = UpdateMediaWorkerWriteSpeed(line, now, startInfo, outputFileName, out long progressBytes);
                    if (TryParseMediaWorkerPacketProgress(line, out long videoPackets, out long audioPackets, out bool hasVideoStream)
                        ? progressTracker.Observe(progressBytes, videoPackets, audioPackets, hasVideoStream, now)
                        : advanced && progressTracker.Observe($"out_time={progressBytes}", now))
                    {
                        ConfirmMediaProgress(startInfo);
                    }
                    _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(startInfo.RoomUrl));
                    continue;
                }

                await OnStandardOutputReceived(line, readToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal bool UpdateMediaWorkerWriteSpeed(string line, DateTime now, RecorderStartInfo startInfo, string outputFileName, out long progressBytes)
    {
        string[] parts = line.Split('|');
        long outputBytes;
        if (parts.Length < 2
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out outputBytes)
            || outputBytes < 0)
        {
            progressBytes = 0;
            return false;
        }

        long inputBytes = lastMediaInputBytes;
        long parsedInputBytes = 0;
        bool hasInputCounter = parts.Length >= 3
            && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInputBytes)
            && parsedInputBytes >= 0;
        if (hasInputCounter)
        {
            inputBytes = parsedInputBytes;
        }
        bool advanced = outputBytes > lastMediaProgressBytes || inputBytes > lastMediaInputBytes;

        if (lastMediaProgressAt > DateTime.MinValue && now > lastMediaProgressAt && outputBytes >= lastMediaProgressBytes)
        {
            double seconds = (now - lastMediaProgressAt).TotalSeconds;
            if (seconds > 0)
            {
                MediaWorkerWriteBytesPerSecond = (outputBytes - lastMediaProgressBytes) / seconds;
                double sampleReadBytesPerSecond = double.NaN;
                if (hasInputCounter && inputBytes >= lastMediaInputBytes)
                {
                    MediaWorkerReadBytesPerSecond = (inputBytes - lastMediaInputBytes) / seconds;
                    sampleReadBytesPerSecond = MediaWorkerReadBytesPerSecond;
                }
                mediaSpeedSummaryWindow.Observe(
                    now,
                    seconds,
                    inputBytes >= lastMediaInputBytes ? inputBytes - lastMediaInputBytes : 0,
                    outputBytes - lastMediaProgressBytes,
                    sampleReadBytesPerSecond);
                if (mediaSpeedSummaryWindow.ShouldFlush(now))
                {
                    FlushMediaWorkerSpeedSummary(startInfo, outputFileName);
                }
            }
        }

        lastMediaProgressBytes = outputBytes;
        lastMediaInputBytes = inputBytes;
        lastMediaProgressAt = now;
        progressBytes = outputBytes > long.MaxValue - inputBytes
            ? long.MaxValue
            : outputBytes + inputBytes;
        return advanced;
    }

    internal static bool TryParseMediaWorkerPacketProgress(string line, out long videoPackets, out long audioPackets)
    {
        return TryParseMediaWorkerPacketProgress(line, out videoPackets, out audioPackets, out _);
    }

    internal static bool TryParseMediaWorkerPacketProgress(
        string line,
        out long videoPackets,
        out long audioPackets,
        out bool hasVideoStream)
    {
        videoPackets = 0;
        audioPackets = 0;
        hasVideoStream = false;
        string[] parts = line.Split('|');
        bool parsed = parts.Length >= 5
            && long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out videoPackets)
            && videoPackets >= 0
            && long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out audioPackets)
            && audioPackets >= 0;
        if (parsed && parts.Length >= 6)
        {
            hasVideoStream = parts[5] == "1";
        }
        return parsed;
    }

    internal static bool TryParseMediaWorkerTimelineEvent(
        string line,
        out FfmpegTimelineEventKind timelineEvent,
        out long gapMicroseconds,
        out long videoPackets,
        out long audioPackets)
    {
        timelineEvent = FfmpegTimelineEventKind.None;
        gapMicroseconds = 0;
        videoPackets = 0;
        audioPackets = 0;
        string[] parts = line.Split('|');
        if (parts.Length != 5
            || !string.Equals(parts[0], "timeline", StringComparison.Ordinal)
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out gapMicroseconds)
            || gapMicroseconds < 0
            || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out videoPackets)
            || videoPackets < 0
            || !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out audioPackets)
            || audioPackets < 0)
        {
            return false;
        }

        timelineEvent = parts[1] switch
        {
            "s" => FfmpegTimelineEventKind.VideoStalled,
            "a" => FfmpegTimelineEventKind.AudioStalled,
            "r" => FfmpegTimelineEventKind.VideoRecovered,
            "i" => FfmpegTimelineEventKind.InitialAligned,
            _ => FfmpegTimelineEventKind.None,
        };
        return timelineEvent != FfmpegTimelineEventKind.None;
    }

    private void FlushMediaWorkerSpeedSummary(RecorderStartInfo startInfo, string outputFileName)
    {
        MediaSpeedSummary? summary = mediaSpeedSummaryWindow.Drain();
        if (summary == null)
        {
            return;
        }

        AppSessionLogger.Event("info", "recorder", "record_speed_summary", "recording download speed summary", new
        {
            startInfo.RoomUrl,
            startInfo.NickName,
            startInfo.PlatformName,
            FileName,
            outputFileName,
            summary.Samples,
            durationSeconds = Math.Round(summary.DurationSeconds, 2),
            readAvgMbps = Math.Round(summary.ReadAverageMbps, 3),
            readMinMbps = Math.Round(summary.ReadMinMbps, 3),
            readMaxMbps = Math.Round(summary.ReadMaxMbps, 3),
            writeAvgMbps = Math.Round(summary.WriteAverageMbps, 3),
            inputMb = Math.Round(summary.InputBytes / 1024d / 1024d, 2),
            outputMb = Math.Round(summary.OutputBytes / 1024d / 1024d, 2),
        });
    }

    private async Task ReadMediaWorkerErrorAsync(StreamReader reader, StringBuilder errorOutput, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && await reader.ReadLineAsync(token) is { } line)
            {
                AppendOutputTail(errorOutput, line);
                await OnStandardErrorReceived(line, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
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
        PublishLifecycle("started", startInfo);
        _ = WeakReferenceMessenger.Default.Send(new RoomRecordingStateChangedMessage(startInfo.RoomUrl));
    }

    private void PublishLifecycle(string phase, RecorderStartInfo startInfo, int attempt = 0)
    {
        ExtensionRecordingLifecycleEvent payload = new(
            Guid.NewGuid().ToString("N"),
            activeRecordingId,
            phase,
            startInfo.RoomUrl,
            startInfo.NickName,
            startInfo.PlatformName,
            FileName ?? string.Empty,
            attempt,
            DateTimeOffset.UtcNow);
        _ = ExtensionHostRuntime.PublishAsync(ExtensionEventNames.RecordingLifecycle, payload);
    }

    private static async Task ReleasePostProcessingHandoffAsync(Task dispatchTask, IDisposable handoffProtection)
    {
        try
        {
            await dispatchTask;
        }
        finally
        {
            handoffProtection.Dispose();
        }
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
        Regex regex = new(
            regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(100));
        string searchPattern = pattern.Replace("%03d", "*", StringComparison.Ordinal);

        string[] segments = MediaFileCatalog.OrderSegmentPaths(
                Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
                    .Where(file => regex.IsMatch(Path.GetFileName(file))),
                pattern)
            .ToArray();
        return File.Exists(fileName) ? [fileName, .. segments] : segments;
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
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or Win32Exception or NotSupportedException)
        {
        }
    }

    internal static bool HasUsableOutput(string fileName)
    {
        return GetRecordedSourceFilesForPattern(fileName).Any(path =>
        {
            try
            {
                return new FileInfo(path).Length > 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                return false;
            }
        });
    }

    internal static void DeleteEmptyOutputFiles(string fileName)
    {
        foreach (string path in GetRecordedSourceFilesForPattern(fileName))
        {
            try
            {
                if (new FileInfo(path).Length == 0)
                {
                    File.Delete(path);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
            }
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
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
                if (progressTracker.GetStallReason(
                        DateTime.UtcNow,
                        ProgressStartupTimeout,
                        ProgressStallTimeout,
                        VideoProgressStallTimeout) != RecorderStallReason.None)
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

    internal static bool ShouldConsumeReconnectAttempt(bool? isLiveAfterRefresh, bool hadMediaProgress)
    {
        return isLiveAfterRefresh != true || !hadMediaProgress;
    }

    internal static bool ShouldSuppressRapidRetry(int exitCode, bool wasCanceled, bool wasStalled, double durationSeconds, bool? isLiveAfterRefresh)
    {
        return !wasCanceled
            && !wasStalled
            && exitCode == 0
            && durationSeconds > 0
            && durationSeconds < 20
            && isLiveAfterRefresh != false;
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

    internal static string? SelectInputFallback(
        string? platformName,
        string? currentUrl,
        string? hlsUrl,
        string? flvUrl,
        bool hadMediaProgress,
        bool alreadyTried)
    {
        if (alreadyTried
            || hadMediaProgress
            || !string.Equals(platformName, "Bilibili", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(currentUrl)
            || string.IsNullOrWhiteSpace(hlsUrl)
            || string.IsNullOrWhiteSpace(flvUrl)
            || !string.Equals(currentUrl, hlsUrl, StringComparison.Ordinal)
            || string.Equals(currentUrl, flvUrl, StringComparison.Ordinal))
        {
            return null;
        }

        return flvUrl;
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
        startInfo.ReferenceUrl = refreshed.ReferenceUrl;
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

    internal static string RemoveCookieHeaders(string? headers)
    {
        string[] lines = (headers ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return NormalizeHeaders(string.Join("\r\n", lines));
    }

    private static bool IsHlsUrl(string url, RecorderStartInfo startInfo)
    {
        return url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || url == startInfo.RecordUrl && startInfo.RecordUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || url == startInfo.HlsUrl;
    }

    internal static void DeleteFailedOutputFiles(string fileName, string? metadataPath)
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

    internal static string BuildSessionOutputFileName(string saveFolder, string fileName, string sourceExtension)
    {
        string extension = sourceExtension.TrimStart('.');
        return Path.Combine(saveFolder, $"{fileName}_%03d.{extension}");
    }

    internal static string BuildSessionPartOutputFileName(string outputPattern, int partIndex)
    {
        return outputPattern.Replace("%03d", partIndex.ToString("000", CultureInfo.InvariantCulture), StringComparison.Ordinal);
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

    internal static OutputReservation ReserveSessionOutput(string saveFolder, string requestedBaseFileName, string sourceExtension)
    {
        lock (OutputReservationLock)
        {
            int suffix = 1;
            while (true)
            {
                string baseFileName = suffix == 1 ? requestedBaseFileName : $"{requestedBaseFileName}_{suffix}";
                string outputPattern = BuildSessionOutputFileName(saveFolder, baseFileName, sourceExtension);
                if (!ReservedOutputPatterns.Contains(outputPattern) && !OutputExists(saveFolder, baseFileName, outputPattern, isToSegment: true))
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

        string extension = Path.GetExtension(outputPattern);
        return isToSegment
            ? File.Exists(Path.Combine(saveFolder, $"{baseFileName}{extension}"))
              || Directory.EnumerateFiles(saveFolder, $"{baseFileName}_*{extension}", SearchOption.TopDirectoryOnly).Any()
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

internal sealed class MediaSpeedSummaryWindow(TimeSpan interval)
{
    private DateTime startedAt = DateTime.MinValue;
    private DateTime lastObservedAt = DateTime.MinValue;
    private int samples;
    private double durationSeconds;
    private long inputBytes;
    private long outputBytes;
    private double readMinBytesPerSecond = double.MaxValue;
    private double readMaxBytesPerSecond;

    public void Observe(
        DateTime now,
        double sampleSeconds,
        long inputBytesDelta,
        long outputBytesDelta,
        double readBytesPerSecond)
    {
        if (sampleSeconds <= 0 || double.IsNaN(sampleSeconds) || double.IsInfinity(sampleSeconds))
        {
            return;
        }

        if (inputBytesDelta <= 0 && outputBytesDelta <= 0)
        {
            return;
        }

        if (startedAt == DateTime.MinValue)
        {
            startedAt = now;
        }

        lastObservedAt = now;
        samples++;
        durationSeconds += sampleSeconds;
        inputBytes += Math.Max(0, inputBytesDelta);
        outputBytes += Math.Max(0, outputBytesDelta);

        if (readBytesPerSecond >= 0 && !double.IsNaN(readBytesPerSecond) && !double.IsInfinity(readBytesPerSecond))
        {
            readMinBytesPerSecond = Math.Min(readMinBytesPerSecond, readBytesPerSecond);
            readMaxBytesPerSecond = Math.Max(readMaxBytesPerSecond, readBytesPerSecond);
        }
    }

    public bool ShouldFlush(DateTime now)
    {
        return samples > 0 && startedAt != DateTime.MinValue && now - startedAt >= interval;
    }

    public MediaSpeedSummary? Drain()
    {
        if (samples == 0 || durationSeconds <= 0)
        {
            Reset();
            return null;
        }

        MediaSpeedSummary summary = new(
            samples,
            durationSeconds,
            inputBytes,
            outputBytes,
            ToMbps(inputBytes / durationSeconds),
            readMinBytesPerSecond == double.MaxValue ? 0 : ToMbps(readMinBytesPerSecond),
            ToMbps(readMaxBytesPerSecond),
            ToMbps(outputBytes / durationSeconds),
            startedAt,
            lastObservedAt);
        Reset();
        return summary;
    }

    public void Reset()
    {
        startedAt = DateTime.MinValue;
        lastObservedAt = DateTime.MinValue;
        samples = 0;
        durationSeconds = 0;
        inputBytes = 0;
        outputBytes = 0;
        readMinBytesPerSecond = double.MaxValue;
        readMaxBytesPerSecond = 0;
    }

    private static double ToMbps(double bytesPerSecond)
    {
        return bytesPerSecond * 8d / 1_000_000d;
    }
}

internal sealed record MediaSpeedSummary(
    int Samples,
    double DurationSeconds,
    long InputBytes,
    long OutputBytes,
    double ReadAverageMbps,
    double ReadMinMbps,
    double ReadMaxMbps,
    double WriteAverageMbps,
    DateTime StartedAt,
    DateTime LastObservedAt);

internal sealed class RecorderProgressTracker(DateTime startedAt)
{
    private readonly object syncRoot = new();
    private DateTime lastProgressAt = startedAt;
    private string lastMediaTime = string.Empty;
    private bool hasProgress;
    private DateTime lastVideoProgressAt = startedAt;
    private DateTime lastAudioProgressAt = startedAt;
    private long lastVideoPackets;
    private long lastAudioPackets;
    private bool expectsVideo;

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

    public bool Observe(long progressMarker, long videoPackets, long audioPackets, DateTime observedAt)
    {
        return Observe(progressMarker, videoPackets, audioPackets, videoPackets > 0, observedAt);
    }

    public bool Observe(long progressMarker, long videoPackets, long audioPackets, bool hasVideoStream, DateTime observedAt)
    {
        lock (syncRoot)
        {
            expectsVideo |= hasVideoStream;
            bool firstProgress = !hasProgress;
            string mediaTime = progressMarker.ToString(CultureInfo.InvariantCulture);
            bool videoAdvanced = videoPackets > lastVideoPackets;
            bool audioAdvanced = audioPackets > lastAudioPackets;
            if (!string.Equals(lastMediaTime, mediaTime, StringComparison.Ordinal) || videoAdvanced || audioAdvanced)
            {
                lastMediaTime = mediaTime;
                lastProgressAt = observedAt;
                hasProgress = true;
            }

            if (videoAdvanced)
            {
                lastVideoPackets = videoPackets;
                lastVideoProgressAt = observedAt;
            }
            if (audioAdvanced)
            {
                lastAudioPackets = audioPackets;
                lastAudioProgressAt = observedAt;
            }
            return firstProgress && hasProgress;
        }
    }

    public bool IsStalled(DateTime now, TimeSpan startupTimeout, TimeSpan stallTimeout)
    {
        return GetStallReason(now, startupTimeout, stallTimeout, stallTimeout) != RecorderStallReason.None;
    }

    public RecorderStallReason GetStallReason(
        DateTime now,
        TimeSpan startupTimeout,
        TimeSpan stallTimeout,
        TimeSpan videoStallTimeout)
    {
        lock (syncRoot)
        {
            if (!hasProgress)
            {
                return now - lastProgressAt >= startupTimeout
                    ? RecorderStallReason.AllMedia
                    : RecorderStallReason.None;
            }
            if (expectsVideo
                && lastAudioProgressAt > lastVideoProgressAt
                && now - lastVideoProgressAt >= videoStallTimeout)
            {
                return RecorderStallReason.Video;
            }
            return now - lastProgressAt >= stallTimeout
                ? RecorderStallReason.AllMedia
                : RecorderStallReason.None;
        }
    }

    public TimeSpan GetStalledDuration(DateTime now, RecorderStallReason reason)
    {
        lock (syncRoot)
        {
            DateTime stalledSince = reason == RecorderStallReason.Video
                ? lastVideoProgressAt
                : lastProgressAt;
            return now > stalledSince ? now - stalledSince : TimeSpan.Zero;
        }
    }

    public bool HasProgress
    {
        get
        {
            lock (syncRoot)
            {
                return hasProgress;
            }
        }
    }
}

internal enum RecorderStallReason
{
    None,
    AllMedia,
    Video,
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

    public string ReferenceUrl { get; set; } = string.Empty;

    public string Headers { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Bitrate { get; set; } = string.Empty;

    public string CoverPath { get; set; } = string.Empty;

    public RoomRecordingOptions Options { get; set; } = RoomRecordingSettings.GetGlobal();

    internal Func<RoomRecordingOptions>? ResolveCurrentOptions { get; set; }

    internal Func<CancellationToken, Task<RecorderStreamRefreshResult?>>? RefreshStreamAsync { get; set; }

    internal Action? OfflineConfirmed { get; set; }

    internal Action? ReconnectExhausted { get; set; }

    internal Action? RapidExitDetected { get; set; }
}

internal sealed record RecorderStreamRefreshResult
{
    public bool? IsLiveStreaming { get; init; }

    public string RecordUrl { get; init; } = string.Empty;

    public string HlsUrl { get; init; } = string.Empty;

    public string FlvUrl { get; init; } = string.Empty;

    public string ReferenceUrl { get; init; } = string.Empty;

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
