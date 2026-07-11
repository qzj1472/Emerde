using CommunityToolkit.Mvvm.Messaging;
using MediaInfoLib;
using System.Diagnostics;
using System.Text;
using Emerde.Extensions;
using Emerde.Models;

namespace Emerde.Core;

public sealed class Converter
{
    private const string OptimizedAudioFilter = "[0:a:0]volume=30dB,acompressor=threshold=-10dB:ratio=3,alimiter=limit=0.316227766:level=false[aopt]";
    private const int ProcessOutputTailLimit = 8192;
    private static int activeCount;

    public static int ActiveConversionCount => Math.Max(0, Volatile.Read(ref activeCount));

    public static bool HasActiveConversions => ActiveConversionCount > 0;

    public async Task<bool> ExecuteAsync(string sourceFileName, string targetFormat, CancellationTokenSource? tokenSource = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        ArgumentNullException.ThrowIfNull(targetFormat);

        string? recorderPath = SearchFileHelper.SearchExecutable("ffmpeg.exe");
        if (string.IsNullOrWhiteSpace(recorderPath))
        {
            AppSessionLogger.Event("error", "converter", "converter_missing", "ffmpeg executable was not found", new { sourceFileName, targetFormat });
            return false;
        }

        FileInfo sourceFileInfo = new(sourceFileName);
        if (!sourceFileInfo.Exists || sourceFileInfo.Extension.Equals(targetFormat, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string targetFileName = Path.ChangeExtension(sourceFileName, targetFormat);
        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.WithFileName(
            VideoRecordingMetadataStore.Load(sourceFileInfo),
            Path.GetFileName(targetFileName));
        IReadOnlyList<string> arguments = BuildArguments(sourceFileName, targetFileName, metadata, ProbeAudioStream(sourceFileName));
        if (arguments.Count == 0)
        {
            return false;
        }

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = recorderPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            },
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        CancellationToken token = tokenSource?.Token ?? default;
        Interlocked.Increment(ref activeCount);
        AppSessionLogger.Event("info", "converter", "conversion_starting", "recording conversion is starting", new
        {
            sourceFileName,
            targetFileName,
            activeConversions = ActiveConversionCount,
        });

        try
        {
            process.Start();
            RuntimeResourceLogger.Register(process, "ffmpeg", "convert", extra: new { sourceFileName, targetFileName });
            StringBuilder errorTail = new();
            Task errorTask = ReadPipeAsync(process.StandardError, async (data, readToken) =>
            {
                AppendOutputTail(errorTail, data);
                await OnStandardErrorReceived(data, readToken);
            }, token);
            Task outputTask = ReadPipeAsync(process.StandardOutput, OnStandardOutputReceived, token);

            await process.WaitForExitAsync(token);
            await Task.WhenAll(errorTask, outputTask);

            bool succeeded = process.ExitCode == 0 && File.Exists(targetFileName);
            AppSessionLogger.Event(succeeded ? "info" : "error", "converter", "conversion_finished", "recording conversion finished", new
            {
                sourceFileName,
                targetFileName,
                process.ExitCode,
                succeeded,
                errorOutput = succeeded ? string.Empty : errorTail.ToString(),
            });
            return succeeded;
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            AppSessionLogger.Event("warn", "converter", "conversion_cancelled", "recording conversion was cancelled", new { sourceFileName, targetFileName });
            throw;
        }
        catch (Exception e)
        {
            KillProcessTree(process);
            AppSessionLogger.WriteException(e);
            AppSessionLogger.Event("error", "converter", "conversion_failed", e.Message, new { sourceFileName, targetFileName });
            return false;
        }
        finally
        {
            Interlocked.Decrement(ref activeCount);
        }
    }

    internal static IReadOnlyList<string> BuildArguments(string sourceFileName, string targetFileName, VideoRecordingMetadata metadata, bool hasAudio = true)
    {
        return BuildArguments(sourceFileName, targetFileName, metadata, hasAudio ? AudioStreamPresence.Present : AudioStreamPresence.Absent);
    }

    internal static IReadOnlyList<string> BuildArguments(string sourceFileName, string targetFileName, VideoRecordingMetadata metadata, AudioStreamPresence audioPresence)
    {
        string sourceExtension = Path.GetExtension(sourceFileName);
        if (!sourceExtension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            && !sourceExtension.Equals(".flv", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        List<string> arguments = ["-y"];
        if (sourceExtension.Equals(".ts", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["-fflags", "+genpts"]);
        }

        arguments.AddRange(["-i", sourceFileName]);
        if (audioPresence == AudioStreamPresence.Present)
        {
            arguments.AddRange([
                "-filter_complex", OptimizedAudioFilter,
                "-map", "0:v?",
                "-map", "0:a:0?",
                "-map", "[aopt]",
                "-map", "0:s?",
                "-map_metadata", "0",
                "-map_chapters", "0",
                "-c:v", "copy",
                "-c:a:0", "copy",
                "-c:a:1", "aac",
                "-c:s", "copy",
                "-metadata:s:a:0", "title=原音频",
                "-metadata:s:a:0", "handler_name=原音频",
                "-metadata:s:a:1", "title=优化音频",
                "-metadata:s:a:1", "handler_name=优化音频",
            ]);
        }
        else if (audioPresence == AudioStreamPresence.Absent)
        {
            arguments.AddRange([
                "-map", "0:v?",
                "-map", "0:s?",
                "-map_metadata", "0",
                "-map_chapters", "0",
                "-c:v", "copy",
                "-c:s", "copy",
            ]);
        }
        else
        {
            arguments.AddRange([
                "-map", "0:v?",
                "-map", "0:a?",
                "-map", "0:s?",
                "-map_metadata", "0",
                "-map_chapters", "0",
                "-c:v", "copy",
                "-c:a", "copy",
                "-c:s", "copy",
            ]);
        }
        arguments.AddRange(VideoRecordingMetadataStore.BuildFfmpegMetadataArguments(metadata));
        if (VideoRecordingMetadataStore.UsesMovMetadataTags(targetFileName))
        {
            arguments.AddRange(["-movflags", "use_metadata_tags"]);
        }
        arguments.Add(targetFileName);
        return arguments;
    }

    internal static AudioStreamPresence ProbeAudioStream(string sourceFileName)
    {
        try
        {
            using MediaInfo mediaInfo = new();
            nint openResult = mediaInfo.Open(sourceFileName);
            if (openResult == nint.Zero)
            {
                return AudioStreamPresence.Unknown;
            }
            return mediaInfo.Count_Get(StreamKind.Audio) > 0
                ? AudioStreamPresence.Present
                : AudioStreamPresence.Absent;
        }
        catch
        {
            return AudioStreamPresence.Unknown;
        }
    }

    private static void AppendOutputTail(StringBuilder output, string data)
    {
        output.AppendLine(data);
        if (output.Length > ProcessOutputTailLimit)
        {
            output.Remove(0, output.Length - ProcessOutputTailLimit);
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

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
        }
    }

    private static Task OnStandardErrorReceived(string data, CancellationToken token)
    {
        Debug.WriteLine(data);
        WeakReferenceMessenger.Default.Send(new RecorderMessage
        {
            DataType = StandardData.StandardError,
            Data = data,
        });
        return Task.CompletedTask;
    }

    private static Task OnStandardOutputReceived(string data, CancellationToken token)
    {
        Debug.WriteLine(data);
        WeakReferenceMessenger.Default.Send(new RecorderMessage
        {
            DataType = StandardData.StandardOutput,
            Data = data,
        });
        return Task.CompletedTask;
    }
}

internal enum AudioStreamPresence
{
    Unknown,
    Absent,
    Present,
}
