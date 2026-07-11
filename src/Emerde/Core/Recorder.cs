using CommunityToolkit.Mvvm.Messaging;
using Flucli;
using Flucli.Utils.Extensions;
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

    public RecordStatus RecordStatus { get; internal set; } = RecordStatus.Initialized;

    public CancellationTokenSource? TokenSource { get; private set; } = null;

    private readonly object stateLock = new();

    private readonly object processLock = new();

    private Process? currentProcess;

    private Task? recordingTask;

    private bool stopRequested;

    private readonly List<string> recordedFilePatterns = [];

    public bool IsBusy => recordingTask is { IsCompleted: false };

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

            stopRequested = false;
            RecordStatus = RecordStatus.Recording;
            TokenSource = tokenSource ?? new CancellationTokenSource();
            recordingTask = Task.Factory.StartNew(
                () => RunAsync(startInfo, TokenSource.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            ).Unwrap();
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

            string saveFolder = SaveFolderHelper.GetSaveFolder(recordingOptions.SaveFolder);
            saveFolder = BuildSaveFolder(saveFolder, startInfo.NickName, DateTime.Now, recordingOptions.SaveFolderPathLevel);
            Directory.CreateDirectory(saveFolder);

            string userAgent = Configurations.UserAgent.Get();
            string httpProxy = ProxyAddress.Normalize(Configurations.ProxyUrl.Get());
            bool isUseProxy = Configurations.IsUseProxy.Get() && !string.IsNullOrWhiteSpace(httpProxy);
            int segmentTime = Math.Max(1, recordingOptions.SegmentTime);
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

            if (string.IsNullOrWhiteSpace(userAgent))
            {
                userAgent = "Mozilla/5.0 (Linux; Android 11; SAMSUNG SM-G973U) AppleWebKit/537.36 ("
                          + "KHTML, like Gecko) SamsungBrowser/14.2 Chrome/87.0.4280.141 Mobile "
                          + "Safari/537.36";
            }

            EndTime = DateTime.MinValue;
            StartTime = DateTime.Now;
            recordedFilePatterns.Clear();

            int attempt = 0;
            while (!token.IsCancellationRequested && !stopRequested)
            {
                DateTime now = DateTime.Now;
                string baseFileName = BuildBaseFileName(startInfo, now).SanitizeFileName();
                FileName = BuildOutputFileName(saveFolder, baseFileName, isToSegment, isHls);
                VideoRecordingMetadata metadata = BuildMetadata(baseFileName, isHls ? "ts" : "flv", startInfo, now);
                MetadataPath = VideoRecordingMetadataStore.WriteSidecar(saveFolder, baseFileName, metadata);
                recordedFilePatterns.Add(FileName);

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
                    metadata);

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
                    isUseProxy,
                    attempt,
                });

                int exitCode = await ExecuteRecorderAsync(recorderPath, arguments, isUseProxy, httpProxy, startInfo, token);
                DeleteMetadataIfNoOutput(FileName, MetadataPath);
                if (token.IsCancellationRequested || stopRequested || exitCode == 0)
                {
                    break;
                }

                attempt++;
                if (!CanRetryRecording(attempt))
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

                TimeSpan delay = TimeSpan.FromSeconds(Math.Min(8, attempt switch
                {
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
            EndTime = DateTime.Now;
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
                stopRequested,
                startedAt = StartTime,
                endedAt = EndTime,
                durationSeconds = StartTime == DateTime.MinValue ? 0 : Math.Max(0, (EndTime - StartTime).TotalSeconds),
            });
            await ConvertRecordedFilesAsync(recordingOptions);
            RecordingCleanupService.QueueRun();
        }
    }

    public void Stop()
    {
        stopRequested = true;
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

    private List<string> BuildArguments(
        string outputFileName,
        bool isUseProxy,
        string httpProxy,
        string headers,
        string userAgent,
        bool isToSegment,
        bool isToSegmentBySize,
        int segmentTime,
        int segmentTimeUnit,
        VideoRecordingMetadata metadata)
    {
        List<string> arguments =
        [
            "-y",
            "-v", "verbose",
            "-rw_timeout", "30000000",
            "-loglevel", "error",
            "-hide_banner",
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
                "-i", Url ?? string.Empty,
                "-bufsize", "8000k",
                "-sn",
                "-dn",
                "-reconnect_delay_max", "60",
                "-reconnect", "1",
                "-reconnect_streamed", "1",
                "-reconnect_at_eof", "1",
                "-reconnect_on_network_error", "1",
                "-reconnect_on_http_error", "4xx,5xx",
                "-max_muxing_queue_size", "1024",
                "-correct_ts_overflow", "1",
                "-avoid_negative_ts", "1",
                "-map", "0",
                "-c:v", "copy",
                "-c:a", "copy"
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
        process.Start();
        TryTraceProcess(process);
        RuntimeResourceLogger.Register(process, "ffmpeg", "record", startInfo.RoomUrl, startInfo.NickName, new
        {
            startInfo.PlatformName,
            FileName,
            inputUrl = Url ?? string.Empty,
        });

        lock (processLock)
        {
            currentProcess = process;
        }

        Task errorTask = ReadPipeAsync(process.StandardError, OnStandardErrorReceived, CancellationToken.None);
        Task outputTask = ReadPipeAsync(process.StandardOutput, OnStandardOutputReceived, CancellationToken.None);
        bool wasCanceled = false;

        try
        {
            Task exitTask = process.WaitForExitAsync(CancellationToken.None);
            Task cancellationTask = WaitForCancellationAsync(token);
            Task completedTask = await Task.WhenAny(exitTask, cancellationTask);

            if (completedTask == cancellationTask && token.IsCancellationRequested)
            {
                wasCanceled = true;
                await StopProcessGracefullyAsync(process);
            }
            else
            {
                await exitTask;
            }
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
        AppSessionLogger.Event(process.ExitCode == 0 ? "info" : "warn", "recorder", "record_process_exited", "ffmpeg recording process exited", new
        {
            startInfo.RoomUrl,
            startInfo.NickName,
            process.Id,
            process.ExitCode,
            wasCanceled,
            FileName,
        });

        if (wasCanceled)
        {
            throw new OperationCanceledException(token);
        }

        return process.ExitCode;
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

    private async Task ConvertRecordedFilesAsync(RoomRecordingOptions recordingOptions)
    {
        try
        {
            string formatArrow = recordingOptions.RecordFormat;

            if (string.IsNullOrWhiteSpace(formatArrow) || !formatArrow.Contains("->", StringComparison.Ordinal))
            {
                return;
            }

            string targetFormat = "." + formatArrow.Split('>')[1].Trim().ToLowerInvariant();
            foreach (string fileName in GetRecordedSourceFiles())
            {
                if (await new Converter().ExecuteAsync(fileName, targetFormat) && recordingOptions.IsRemoveTs)
                {
                    File.Delete(fileName);
                    VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(fileName);
                    if (string.Equals(FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        FileName = Path.ChangeExtension(fileName, targetFormat);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            AppSessionLogger.WriteException(e);
        }
    }

    private string[] GetRecordedSourceFiles()
    {
        string[] patterns = recordedFilePatterns.Count > 0
            ? recordedFilePatterns.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : string.IsNullOrWhiteSpace(FileName) ? [] : [FileName];

        return patterns.SelectMany(GetRecordedSourceFilesForPattern)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .Where(file =>
            {
                try
                {
                    FileInfo info = new(file);
                    return info.Exists && info.Length > 0;
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();
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

        if (await WaitForExitAsync(process, TimeSpan.FromSeconds(15)))
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
        if (!string.IsNullOrWhiteSpace(startInfo.RecordUrl))
        {
            return startInfo.RecordUrl;
        }

        if (!string.IsNullOrWhiteSpace(startInfo.HlsUrl))
        {
            return startInfo.HlsUrl;
        }

        return startInfo.FlvUrl;
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

    private Task OnStandardErrorReceived(string data, CancellationToken token)
    {
        Debug.WriteLine(data);
        AppSessionLogger.Event("debug", "recorder", "ffmpeg_stderr", data, new
        {
            FileName,
        });
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
        AppSessionLogger.Event("debug", "recorder", "ffmpeg_stdout", data, new
        {
            FileName,
        });
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
    public static string SanitizeFileName(this string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be null or whitespace.", nameof(fileName));
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
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
