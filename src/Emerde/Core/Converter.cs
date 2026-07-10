using CommunityToolkit.Mvvm.Messaging;
using Flucli;
using Flucli.Utils.Extensions;
using System.Diagnostics;
using System.Text;
using Emerde.Extensions;
using Emerde.Models;

namespace Emerde.Core;

public sealed class Converter
{
    public async Task<bool> ExecuteAsync(string sourceFileName, string targetFormat, CancellationTokenSource? tokenSource = null)
    {
        _ = sourceFileName ?? throw new ArgumentNullException(nameof(sourceFileName));
        _ = targetFormat ?? throw new ArgumentNullException(nameof(targetFormat));

        string? recorderPath = SearchFileHelper.SearchExecutable("ffmpeg.exe");

        if (recorderPath == null)
        {
            // Error on Converter not found
            return false;
        }

        FileInfo sourceFileInfo = new(sourceFileName);

        if (!sourceFileInfo.Exists)
        {
            return false;
        }

        if (sourceFileInfo.Extension.Equals(targetFormat, StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        string targetFileName = Path.ChangeExtension(sourceFileName, targetFormat);
        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.WithFileName(
            VideoRecordingMetadataStore.Load(sourceFileInfo),
            Path.GetFileName(targetFileName));
        string parameters = string.Empty;

        if (sourceFileInfo.Extension.Equals(".ts", StringComparison.CurrentCultureIgnoreCase))
        {
            List<string> arguments =
            [
                "-y",
                "-fflags", "+genpts",
                "-i", sourceFileName,
                "-c", "copy",
            ];
            arguments.AddRange(VideoRecordingMetadataStore.BuildFfmpegMetadataArguments(metadata));
            if (VideoRecordingMetadataStore.UsesMovMetadataTags(targetFileName))
            {
                arguments.AddRange(["-movflags", "use_metadata_tags"]);
            }
            arguments.Add(targetFileName);
            parameters = arguments.ToArguments();
        }
        else if (sourceFileInfo.Extension.Equals(".flv", StringComparison.CurrentCultureIgnoreCase))
        {
            List<string> arguments =
            [
                "-y",
                "-i", sourceFileName,
                "-c", "copy",
            ];
            arguments.AddRange(VideoRecordingMetadataStore.BuildFfmpegMetadataArguments(metadata));
            if (VideoRecordingMetadataStore.UsesMovMetadataTags(targetFileName))
            {
                arguments.AddRange(["-movflags", "use_metadata_tags"]);
            }
            arguments.Add(targetFileName);
            parameters = arguments.ToArguments();
        }

        CliResult result = await recorderPath
            .WithArguments(parameters)
            .WithStandardErrorPipe(PipeTarget.ToDelegate(OnStandardErrorReceived, Encoding.UTF8))
            .WithStandardOutputPipe(PipeTarget.ToDelegate(OnStandardOutputReceived, Encoding.UTF8))
            .ExecuteAsync(cancellationToken: tokenSource?.Token ?? default);

        Debug.WriteLine($"[Converter] exit code is {result.ExitCode}.");

        return result.IsSuccess;
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
}
