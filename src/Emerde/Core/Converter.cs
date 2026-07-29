using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed record ConverterOptions(string TargetFormat, bool OptimizeAudio = false);

public sealed class Converter
{
    private static readonly object TargetReservationLock = new();
    private static readonly HashSet<string> ReservedTargetPaths = new(StringComparer.OrdinalIgnoreCase);
    public static int ActiveConversionCount => MediaOperationRegistry.Count(MediaOperationKind.Conversion);

    public static bool HasActiveConversions => ActiveConversionCount > 0;

    public async Task<bool> ExecuteAsync(string sourceFileName, string targetFormat, CancellationTokenSource? tokenSource = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        ArgumentNullException.ThrowIfNull(targetFormat);

        return await ExecuteAsync([sourceFileName], CreateDefaultOptions(targetFormat), tokenSource);
    }

    public async Task<bool> ExecuteAsync(IReadOnlyList<string> sourceFileNames, string targetFormat, CancellationTokenSource? tokenSource = null)
    {
        return await ExecuteAsync(sourceFileNames, CreateDefaultOptions(targetFormat), tokenSource);
    }

    public async Task<bool> ExecuteAsync(string sourceFileName, ConverterOptions options, CancellationTokenSource? tokenSource = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        return await ExecuteAsync([sourceFileName], options, tokenSource);
    }

    public async Task<bool> ExecuteAsync(IReadOnlyList<string> sourceFileNames, ConverterOptions options, CancellationTokenSource? tokenSource = null)
    {
        return await ExecuteCoreAsync(sourceFileNames, options, tokenSource, sessionSourcePattern: null, allowSameFormat: false, null);
    }

    internal async Task<bool> ExecuteWithCompletionAsync(
        string sourceFileName,
        string targetFormat,
        Action<string> onCompleted,
        CancellationTokenSource? tokenSource = null,
        Action<string>? onTargetReserved = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileName);
        ArgumentNullException.ThrowIfNull(onCompleted);
        return await ExecuteCoreAsync([sourceFileName], CreateDefaultOptions(targetFormat), tokenSource, sessionSourcePattern: null, allowSameFormat: false, onCompleted, onTargetReserved);
    }

    internal async Task<bool> ExecuteSessionPartsAsync(
        string sourcePattern,
        IReadOnlyList<string> sourceFileNames,
        string targetFormat,
        CancellationTokenSource? tokenSource = null,
        Action<string>? onCompleted = null,
        Action<string>? onTargetReserved = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePattern);
        return await ExecuteCoreAsync(sourceFileNames, CreateDefaultOptions(targetFormat), tokenSource, sourcePattern, allowSameFormat: true, onCompleted, onTargetReserved);
    }

    private async Task<bool> ExecuteCoreAsync(
        IReadOnlyList<string> sourceFileNames,
        ConverterOptions converterOptions,
        CancellationTokenSource? tokenSource,
        string? sessionSourcePattern,
        bool allowSameFormat,
        Action<string>? onCompleted,
        Action<string>? onTargetReserved = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFileNames);
        ArgumentNullException.ThrowIfNull(converterOptions);

        string? normalizedTargetFormat = NormalizeTargetFormat(converterOptions.TargetFormat, allowSameFormat);
        if (normalizedTargetFormat == null)
        {
            return false;
        }
        string targetFormat = normalizedTargetFormat;
        bool optimizeAudio = targetFormat == ".mp4" && converterOptions.OptimizeAudio;

        if (!FfmpegMediaEngine.IsAvailable)
        {
            AppSessionLogger.Event("error", "converter", "converter_missing", "ffmpeg native libraries were not found", new { sourceFileNames, targetFormat });
            return false;
        }

        FileInfo[] sourceFileInfos = sourceFileNames
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new FileInfo(path))
            .ToArray();
        if (sourceFileInfos.Length == 0
            || sourceFileInfos.Any(file => !file.Exists)
            || sourceFileInfos.Any(file => !IsSupportedSourceFormat(file.Extension))
            || (!allowSameFormat && sourceFileInfos.Any(file => file.Extension.Equals(targetFormat, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        string requestedTargetFileName = string.IsNullOrWhiteSpace(sessionSourcePattern)
            ? BuildTargetPath(sourceFileInfos, targetFormat)
            : BuildSessionTargetPath(sessionSourcePattern, targetFormat);
        if (sourceFileInfos.Any(file => file.FullName.Equals(requestedTargetFileName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        string targetFileName = ReserveAvailableTargetPath(requestedTargetFileName);
        string temporaryTargetFileName = GetTemporaryTargetPath(targetFileName);
        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.WithFileName(
            VideoRecordingMetadataStore.Load(sourceFileInfos[0]),
            Path.GetFileName(targetFileName));
        using CancellationTokenSource operationCancellation = tokenSource == null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(tokenSource.Token);
        using IDisposable operation = MediaOperationRegistry.Register(
            MediaOperationKind.Conversion,
            () => sourceFileInfos.Select(file => file.FullName).Concat([temporaryTargetFileName, targetFileName]),
            operationCancellation.Cancel);
        CancellationToken token = operationCancellation.Token;
        bool targetCreated = false;
        bool completionAcknowledged = false;
        try
        {
            onTargetReserved?.Invoke(targetFileName);
            SourceProbeBatch probeBatch = await Task.Run(() => ProbeSources(sourceFileInfos, token), token);
            if (!probeBatch.Success)
            {
                AppSessionLogger.Event("error", "converter", "conversion_source_invalid", probeBatch.Error, new { sourceFileName = probeBatch.InvalidSourcePath });
                return false;
            }
            FfmpegMediaProbeResult[] sourceProbes = probeBatch.Probes;
            optimizeAudio = optimizeAudio && sourceProbes.All(probe => probe.HasAudio);
            string[] sourcePaths = sourceFileInfos.Select(file => file.FullName).ToArray();
            AppSessionLogger.Event("info", "converter", "conversion_starting", "recording conversion is starting", new
            {
                sourceFileNames = sourcePaths,
                targetFileName,
                activeConversions = ActiveConversionCount,
                optimizeAudio,
            });
            FfmpegMediaRunResult result = await Task.Run(
                () => optimizeAudio
                    ? FfmpegMediaEngine.RemuxFilesWithOptimizedAudio(sourcePaths, temporaryTargetFileName, metadata, token)
                    : FfmpegMediaEngine.RemuxFiles(sourcePaths, temporaryTargetFileName, metadata, token),
                token);
            if (result.WasCanceled)
            {
                throw new OperationCanceledException(token);
            }

            bool succeeded = result.ExitCode == 0
                && result.HadMediaProgress
                && await Task.Run(
                    () => IsUsableOutput(temporaryTargetFileName, sourceProbes, optimizeAudio, token),
                    token);
            if (succeeded)
            {
                File.Move(temporaryTargetFileName, targetFileName, false);
                targetCreated = true;
                if (!VideoRecordingMetadataStore.WriteCompletedMetadata(targetFileName, metadata))
                {
                    AppSessionLogger.Event("warn", "converter", "conversion_metadata_fallback_failed", "converted video metadata could not be stored", new { targetFileName });
                }
                onCompleted?.Invoke(targetFileName);
                completionAcknowledged = true;
            }
            AppSessionLogger.Event(succeeded ? "info" : "error", "converter", "conversion_finished", "recording conversion finished", new
            {
                sourceFileNames = sourceFileInfos.Select(file => file.FullName).ToArray(),
                targetFileName,
                result.ExitCode,
                succeeded,
                optimizeAudio,
                errorOutput = succeeded ? string.Empty : result.ErrorOutput,
            });
            return succeeded;
        }
        catch (OperationCanceledException)
        {
            AppSessionLogger.Event("warn", "converter", "conversion_cancelled", "recording conversion was cancelled", new { sourceFileNames, targetFileName });
            throw;
        }
        catch (Exception e)
        {
            if (targetCreated && !completionAcknowledged)
            {
                DeleteTemporaryOutput(targetFileName);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFileName);
            }
            AppSessionLogger.WriteException(e);
            AppSessionLogger.Event("error", "converter", "conversion_failed", e.Message, new { sourceFileNames, targetFileName });
            return false;
        }
        finally
        {
            DeleteTemporaryOutput(temporaryTargetFileName);
            ReleaseTargetPath(targetFileName);
        }
    }

    internal static string? NormalizeTargetFormat(string targetFormat, bool allowSourceContainerFormats)
    {
        string normalized = targetFormat.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }
        if (normalized[0] != '.')
        {
            normalized = "." + normalized;
        }
        normalized = normalized.ToLowerInvariant();
        return normalized is ".mp4" or ".mkv"
            || allowSourceContainerFormats && normalized is ".ts" or ".flv"
                ? normalized
                : null;
    }

    internal static ConverterOptions CreateDefaultOptions(string targetFormat)
    {
        string? normalized = NormalizeTargetFormat(targetFormat, allowSourceContainerFormats: true);
        return new ConverterOptions(targetFormat, normalized == ".mp4");
    }

    private static SourceProbeBatch ProbeSources(IReadOnlyList<FileInfo> sourceFileInfos, CancellationToken token)
    {
        FfmpegMediaProbeResult[] probes = new FfmpegMediaProbeResult[sourceFileInfos.Count];
        for (int index = 0; index < sourceFileInfos.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (!FfmpegMediaEngine.TryProbe(sourceFileInfos[index].FullName, out probes[index], out string error, token))
            {
                token.ThrowIfCancellationRequested();
                return new SourceProbeBatch(false, probes, sourceFileInfos[index].FullName, error);
            }
            token.ThrowIfCancellationRequested();
        }
        return new SourceProbeBatch(true, probes, string.Empty, string.Empty);
    }

    private static bool IsSupportedSourceFormat(string extension)
    {
        return extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flv", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildTargetPath(IReadOnlyList<FileInfo> sourceFileInfos, string targetFormat)
    {
        FileInfo firstSource = sourceFileInfos[0];
        string directory = firstSource.DirectoryName ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(firstSource.Name);
        if (sourceFileInfos.Count > 1)
        {
            stem = Regex.Replace(stem, @"_\d{3,}$", string.Empty, RegexOptions.CultureInvariant);
        }

        return Path.Combine(directory, stem + targetFormat);
    }

    internal static string BuildSessionTargetPath(string sourcePattern, string targetFormat)
    {
        string directory = Path.GetDirectoryName(sourcePattern) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(sourcePattern);
        stem = stem.Replace("_%03d", string.Empty, StringComparison.Ordinal)
            .Replace("%03d", string.Empty, StringComparison.Ordinal)
            .TrimEnd('_', '-', '.', ' ');
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = Path.GetFileNameWithoutExtension(sourcePattern);
        }

        return Path.Combine(directory, stem + targetFormat);
    }

    internal static string GetAvailableTargetPath(string requestedPath)
    {
        lock (TargetReservationLock)
        {
            return GetAvailableTargetPathCore(requestedPath);
        }
    }

    private static string GetAvailableTargetPathCore(string requestedPath)
    {
        string fullRequestedPath = Path.GetFullPath(requestedPath);
        if (!File.Exists(fullRequestedPath) && !ReservedTargetPaths.Contains(fullRequestedPath))
        {
            return fullRequestedPath;
        }
        string directory = Path.GetDirectoryName(requestedPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(requestedPath);
        string extension = Path.GetExtension(requestedPath);
        for (int index = 2; ; index++)
        {
            string candidate = Path.Combine(directory, $"{stem}_{index}{extension}");
            string fullCandidate = Path.GetFullPath(candidate);
            if (!File.Exists(fullCandidate) && !ReservedTargetPaths.Contains(fullCandidate))
            {
                return fullCandidate;
            }
        }
    }

    private static string ReserveAvailableTargetPath(string requestedPath)
    {
        lock (TargetReservationLock)
        {
            string targetPath = GetAvailableTargetPathCore(requestedPath);
            ReservedTargetPaths.Add(targetPath);
            return targetPath;
        }
    }

    private static void ReleaseTargetPath(string targetPath)
    {
        lock (TargetReservationLock)
        {
            ReservedTargetPaths.Remove(targetPath);
        }
    }

    private static string GetTemporaryTargetPath(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);
        return MediaFileCatalog.CreateTemporaryPath(Path.Combine(directory, stem + extension), "convert");
    }

    internal static bool IsUsableOutput(
        string path,
        IReadOnlyList<FfmpegMediaProbeResult> sourceProbes,
        bool optimizedAudioExpected,
        CancellationToken token = default)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.Length <= 0
                || !FfmpegMediaEngine.TryProbe(path, out FfmpegMediaProbeResult output, out _, token))
            {
                return false;
            }

            int sourceAudioStreamCount = sourceProbes.Max(probe => probe.AudioStreamCount);
            bool sourceHasAudio = sourceAudioStreamCount > 0;
            bool sourceHasVideo = sourceProbes.Any(probe => probe.HasVideo);
            if (sourceHasAudio && !output.HasAudio
                || sourceHasVideo && !output.HasVideo
                || optimizedAudioExpected && sourceHasAudio && output.AudioStreamCount < sourceAudioStreamCount + 1)
            {
                return false;
            }

            double expectedDuration = sourceProbes.Sum(probe => Math.Max(0, probe.DurationSeconds));
            return IsDurationWithinTolerance(expectedDuration, output.DurationSeconds);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsDurationWithinTolerance(double expectedDuration, double actualDuration)
    {
        if (expectedDuration <= 0)
        {
            return true;
        }
        return actualDuration > 0 && Math.Abs(actualDuration - expectedDuration) <= 2d;
    }

    private static void DeleteTemporaryOutput(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record SourceProbeBatch(
        bool Success,
        FfmpegMediaProbeResult[] Probes,
        string InvalidSourcePath,
        string Error);

}
