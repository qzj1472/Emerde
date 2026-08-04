using System.Text.Json;

namespace Emerde.Core;

internal static class MediaWorker
{
    public const string ModeArgument = "--emerde-media-worker";
    internal const int TimelineRestartExitCode = 74;
    private const string LegacyExecutablePattern = "*-EmerdeWorker-*.exe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 2 || !string.Equals(args[0], ModeArgument, StringComparison.Ordinal))
        {
            return false;
        }

        exitCode = Run(args[1]);
        return true;
    }

    public static void CleanupLegacyExecutables()
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory, LegacyExecutablePattern, SearchOption.TopDirectoryOnly))
            {
                DeleteLegacyExecutable(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    private static int Run(string commandPath)
    {
        try
        {
            string json = File.ReadAllText(commandPath);
            MediaWorkerCommand? command = JsonSerializer.Deserialize<MediaWorkerCommand>(json, JsonOptions);
            if (command == null)
            {
                Console.Error.WriteLine("media worker command is empty");
                return 1;
            }

            DateTime lastProgressAt = DateTime.MinValue;
            long lastProgressBytes = 0;
            long inputBytes = 0;
            long videoPackets = 0;
            long audioPackets = 0;
            bool hasVideoStream = false;
            bool hasAudioStream = false;
            using CancellationTokenSource stopSource = new();
            _ = StartControlInputReader(stopSource, Console.In);
            FfmpegInputOptions inputOptions = new(command.UserAgent, command.Headers, command.IsUseProxy, command.HttpProxy, true);
            if (!string.IsNullOrWhiteSpace(command.ReferenceInputUrl))
            {
                FfmpegCrossStreamAnalysisResult analysis = FfmpegMediaEngine.CompareLiveStreamsAsync(
                    command.InputUrl,
                    command.ReferenceInputUrl,
                    inputOptions,
                    TimeSpan.FromSeconds(Math.Clamp(command.AnalysisDurationSeconds, 1, 15)),
                    stopSource.Token).GetAwaiter().GetResult();
                Console.Out.WriteLine($"cross|{JsonSerializer.Serialize(analysis, JsonOptions)}");
                Console.Out.Flush();
                return 0;
            }
            FfmpegMediaRunResult result = FfmpegMediaEngine.RecordStream(
                command.InputUrl,
                command.OutputFileName,
                command.Metadata ?? new VideoRecordingMetadata(),
                inputOptions,
                command.SegmentOptions,
                stopSource.Token,
                bytesRead =>
                {
                    if (bytesRead > 0)
                    {
                        inputBytes += bytesRead;
                    }

                    DateTime now = DateTime.UtcNow;
                    if (now - lastProgressAt < TimeSpan.FromSeconds(1))
                    {
                        return;
                    }

                    lastProgressAt = now;
                    lastProgressBytes = GetOutputLength(command.OutputFileName, lastProgressBytes);
                    Console.Out.WriteLine($"progress|{lastProgressBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{inputBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{videoPackets.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{audioPackets.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{(hasVideoStream ? "1" : "0")}|{(hasAudioStream ? "1" : "0")}");
                    Console.Out.Flush();
                },
                packetProgress =>
                {
                    if (packetProgress.IsVideo)
                    {
                        videoPackets++;
                    }
                    if (packetProgress.IsAudio)
                    {
                        audioPackets++;
                    }
                    if (packetProgress.TimelineEvent != FfmpegTimelineEventKind.None)
                    {
                        string eventCode = packetProgress.TimelineEvent switch
                        {
                            FfmpegTimelineEventKind.VideoStalled => "s",
                            FfmpegTimelineEventKind.AudioStalled => "a",
                            FfmpegTimelineEventKind.InitialAligned => "i",
                            _ => "r",
                        };
                        Console.Out.WriteLine($"timeline|{eventCode}|{packetProgress.TimelineGapMicroseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{videoPackets.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{audioPackets.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                        Console.Out.Flush();
                    }
                },
                (hasVideo, hasAudio) =>
                {
                    hasVideoStream = hasVideo;
                    hasAudioStream = hasAudio;
                });

            if (!string.IsNullOrWhiteSpace(result.ErrorOutput))
            {
                Console.Error.WriteLine(result.ErrorOutput);
            }

            if (!result.HadMediaProgress)
            {
                DeleteIncompleteOutputs(command.OutputFileName);
                return GetProcessExitCode(result);
            }

            return GetProcessExitCode(result);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    internal static int GetProcessExitCode(FfmpegMediaRunResult result)
    {
        if (result.RequiresInputRestart)
        {
            return TimelineRestartExitCode;
        }

        return result.ExitCode == 0 && !result.HadMediaProgress ? 1 : result.ExitCode;
    }

    internal static Thread StartControlInputReader(CancellationTokenSource stopSource, TextReader reader)
    {
        Thread thread = new(() => ReadControlInput(stopSource, reader))
        {
            IsBackground = true,
            Name = "Emerde.MediaWorker.Control",
        };
        thread.Start();
        return thread;
    }

    private static void ReadControlInput(CancellationTokenSource stopSource, TextReader reader)
    {
        try
        {
            while (!stopSource.IsCancellationRequested && reader.ReadLine() is { } command)
            {
                if (string.Equals(command.Trim(), "q", StringComparison.OrdinalIgnoreCase))
                {
                    stopSource.Cancel();
                    return;
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    public static string WriteCommand(
        string inputUrl,
        string outputFileName,
        VideoRecordingMetadata metadata,
        FfmpegInputOptions inputOptions,
        FfmpegSegmentOptions? segmentOptions)
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-media-worker-{Guid.NewGuid():N}.json");
        MediaWorkerCommand command = new()
        {
            InputUrl = inputUrl,
            OutputFileName = outputFileName,
            Metadata = metadata,
            UserAgent = inputOptions.UserAgent,
            Headers = inputOptions.Headers,
            IsUseProxy = inputOptions.IsUseProxy,
            HttpProxy = inputOptions.HttpProxy,
            SegmentOptions = segmentOptions,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(command, JsonOptions));
        return path;
    }

    public static string WriteCrossStreamCommand(
        string inputUrl,
        string referenceInputUrl,
        FfmpegInputOptions inputOptions,
        TimeSpan maximumDuration)
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-media-worker-{Guid.NewGuid():N}.json");
        MediaWorkerCommand command = new()
        {
            InputUrl = inputUrl,
            ReferenceInputUrl = referenceInputUrl,
            UserAgent = inputOptions.UserAgent,
            Headers = inputOptions.Headers,
            IsUseProxy = inputOptions.IsUseProxy,
            HttpProxy = inputOptions.HttpProxy,
            AnalysisDurationSeconds = Math.Clamp((int)Math.Ceiling(maximumDuration.TotalSeconds), 1, 15),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(command, JsonOptions));
        return path;
    }

    internal static long GetOutputLength(string path, long fallback)
    {
        try
        {
            if (path.Contains("%03d", StringComparison.Ordinal))
            {
                return GetSegmentOutputLength(path, fallback);
            }

            FileInfo fileInfo = new(path);
            return fileInfo.Exists ? fileInfo.Length : fallback;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return fallback;
        }
    }

    private static long GetSegmentOutputLength(string path, long fallback)
    {
        string? directory = Path.GetDirectoryName(path);
        string pattern = Path.GetFileName(path);
        int markerIndex = pattern.IndexOf("%03d", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(directory) || markerIndex < 0 || !Directory.Exists(directory))
        {
            return fallback;
        }

        string prefix = pattern[..markerIndex];
        string suffix = pattern[(markerIndex + 4)..];
        string searchPattern = prefix + "*" + suffix;
        long totalLength = 0;
        bool found = false;
        foreach (string candidate in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(candidate);
            int numberLength = fileName.Length - prefix.Length - suffix.Length;
            if (numberLength < 3
                || !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                || !fileName.AsSpan(prefix.Length, numberLength).ToString().All(char.IsDigit))
            {
                continue;
            }

            totalLength += new FileInfo(candidate).Length;
            found = true;
        }

        return found ? totalLength : fallback;
    }

    private static void DeleteIncompleteOutputs(string path)
    {
        try
        {
            if (!path.Contains("%03d", StringComparison.Ordinal))
            {
                File.Delete(path);
                return;
            }

            string? directory = Path.GetDirectoryName(path);
            string pattern = Path.GetFileName(path);
            int markerIndex = pattern.IndexOf("%03d", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(directory) || markerIndex < 0 || !Directory.Exists(directory))
            {
                return;
            }

            string prefix = pattern[..markerIndex];
            string suffix = pattern[(markerIndex + 4)..];
            foreach (string candidate in Directory.EnumerateFiles(directory, prefix + "*" + suffix, SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(candidate);
                int numberLength = fileName.Length - prefix.Length - suffix.Length;
                if (numberLength >= 3
                    && fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    && fileName.AsSpan(prefix.Length, numberLength).ToString().All(char.IsDigit))
                {
                    File.Delete(candidate);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    private static void DeleteLegacyExecutable(string path)
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

internal sealed class MediaWorkerCommand
{
    public string InputUrl { get; set; } = string.Empty;

    public string ReferenceInputUrl { get; set; } = string.Empty;

    public int AnalysisDurationSeconds { get; set; } = 15;

    public string OutputFileName { get; set; } = string.Empty;

    public VideoRecordingMetadata? Metadata { get; set; }

    public string UserAgent { get; set; } = string.Empty;

    public string Headers { get; set; } = string.Empty;

    public bool IsUseProxy { get; set; }

    public string HttpProxy { get; set; } = string.Empty;

    public FfmpegSegmentOptions? SegmentOptions { get; set; }
}
