using System.Text.Json;

namespace Emerde.Core;

internal static class MediaWorker
{
    public const string ModeArgument = "--emerde-media-worker";
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
            FfmpegInputOptions inputOptions = new(command.UserAgent, command.Headers, command.IsUseProxy, command.HttpProxy, true);
            FfmpegMediaRunResult result = FfmpegMediaEngine.RecordStream(
                command.InputUrl,
                command.OutputFileName,
                command.Metadata ?? new VideoRecordingMetadata(),
                inputOptions,
                CancellationToken.None,
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
                    lastProgressBytes = GetFileLength(command.OutputFileName, lastProgressBytes);
                    Console.Out.WriteLine($"progress|{lastProgressBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{inputBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                    Console.Out.Flush();
                });

            if (!string.IsNullOrWhiteSpace(result.ErrorOutput))
            {
                Console.Error.WriteLine(result.ErrorOutput);
            }

            return result.ExitCode;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    public static string WriteCommand(
        string inputUrl,
        string outputFileName,
        VideoRecordingMetadata metadata,
        FfmpegInputOptions inputOptions)
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
        };
        File.WriteAllText(path, JsonSerializer.Serialize(command, JsonOptions));
        return path;
    }

    private static long GetFileLength(string path, long fallback)
    {
        try
        {
            FileInfo fileInfo = new(path);
            return fileInfo.Exists ? fileInfo.Length : fallback;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return fallback;
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

    public string OutputFileName { get; set; } = string.Empty;

    public VideoRecordingMetadata? Metadata { get; set; }

    public string UserAgent { get; set; } = string.Empty;

    public string Headers { get; set; } = string.Empty;

    public bool IsUseProxy { get; set; }

    public string HttpProxy { get; set; } = string.Empty;
}
