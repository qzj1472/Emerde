namespace Emerde.Core;

internal sealed record MediaFileWorkflowResult(
    bool Success,
    IReadOnlyList<string> OutputPaths,
    string Error = "");

internal static class MediaFileWorkflow
{

    public static async Task<MediaFileWorkflowResult> SplitAsync(
        string sourcePath,
        int segmentSeconds,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath) || segmentSeconds <= 0)
        {
            return new MediaFileWorkflowResult(false, [], "Source file or segment duration is invalid.");
        }

        FileInfo source = new(sourcePath);
        string directory = source.DirectoryName ?? Environment.CurrentDirectory;
        string outputBase = GetUniqueSegmentBase(directory, $"{Path.GetFileNameWithoutExtension(source.Name)}_part");
        string temporaryStem = $".emerde-split-{Guid.NewGuid():N}";
        string temporaryPattern = Path.Combine(directory, $"{temporaryStem}_%03d{source.Extension}");
        string finalPattern = Path.Combine(directory, $"{outputBase}_%03d{source.Extension}");
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using IDisposable? operation = await TryRegisterOperationAsync(
            MediaOperationKind.Split,
            [source.FullName],
            () => [source.FullName, temporaryPattern, finalPattern],
            operationCancellation.Cancel,
            operationCancellation.Token);
        if (operation == null)
        {
            return new MediaFileWorkflowResult(false, [], "Source file is already being processed.");
        }
        try
        {
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(source);
            FfmpegMediaRunResult result = await Task.Run(() => FfmpegMediaEngine.SplitFile(
                source.FullName,
                temporaryPattern,
                segmentSeconds,
                metadata,
                operationCancellation.Token), operationCancellation.Token);
            if (result.WasCanceled)
            {
                throw new OperationCanceledException(operationCancellation.Token);
            }

            string[] temporaryOutputs = GetSplitTemporaryOutputs(directory, temporaryStem, source.Extension, temporaryPattern);
            if (result.ExitCode != 0 || !result.HadMediaProgress || temporaryOutputs.Length == 0
                || temporaryOutputs.Any(path => new FileInfo(path).Length == 0))
            {
                return new MediaFileWorkflowResult(false, [], result.ErrorOutput);
            }

            List<string> outputs = [];
            string segmentGroupId = Guid.NewGuid().ToString("N");
            try
            {
                for (int index = 0; index < temporaryOutputs.Length; index++)
                {
                    string output = Path.Combine(directory, $"{outputBase}_{index:000}{source.Extension}");
                    File.Move(temporaryOutputs[index], output, false);
                    outputs.Add(output);
                    metadata.SchemaVersion = 4;
                    metadata.SegmentGroupId = segmentGroupId;
                    metadata.SegmentIndex = index;
                    metadata.SegmentCount = temporaryOutputs.Length;
                    metadata.SegmentKind = "manual";
                    _ = RecordingCoverStore.TryCopyOrCreateFinalizedCover(
                        [source.FullName],
                        output,
                        metadata,
                        metadata.DurationSeconds,
                        operationCancellation.Token);
                    if (VideoRecordingMetadataStore.HasAnyMetadata(metadata)
                        && !VideoRecordingMetadataStore.WriteCompletedMetadata(output, metadata))
                    {
                        throw new IOException("Failed to store split recording metadata.");
                    }
                }
                return new MediaFileWorkflowResult(true, outputs);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DeleteFiles(outputs);
                AppSessionLogger.WriteException(exception);
                return new MediaFileWorkflowResult(false, [], exception.Message);
            }
        }
        finally
        {
            DeleteFiles(GetSplitTemporaryOutputs(directory, temporaryStem, source.Extension, temporaryPattern));
        }
    }

    public static async Task<MediaFileWorkflowResult> MergeAsync(
        IReadOnlyList<string> sourcePaths,
        string targetDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        bool validateStreams = true)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return new MediaFileWorkflowResult(false, [], "Target directory is invalid.");
        }

        FileInfo[] sources = sourcePaths.Select(path => new FileInfo(path)).ToArray();
        if (sources.Length < 2 || sources.Any(file => !file.Exists)
            || sources.Select(file => file.Extension).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            return new MediaFileWorkflowResult(false, [], "At least two existing files with the same format are required.");
        }
        Directory.CreateDirectory(targetDirectory);
        FileInfo first = sources[0];
        string baseStem = GetMergeBaseStem(first.FullName);
        string targetPath = GetUniquePath(Path.Combine(targetDirectory, $"{baseStem}_merged{first.Extension}"));
        string temporaryPath = MediaFileCatalog.CreateTemporaryPath(targetPath, "merge");
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using IDisposable? operation = await TryRegisterOperationAsync(
            MediaOperationKind.Merge,
            sources.Select(file => file.FullName),
            () => sources.Select(file => file.FullName).Concat([temporaryPath, targetPath]),
            operationCancellation.Cancel,
            operationCancellation.Token);
        if (operation == null)
        {
            return new MediaFileWorkflowResult(false, [], "One or more source files are already being processed.");
        }
        if (validateStreams && !await Task.Run(() => HaveCompatibleStreams(sources), operationCancellation.Token))
        {
            return new MediaFileWorkflowResult(false, [], "Source media streams are not compatible.");
        }
        long totalBytes = sources.Sum(file => Math.Max(0, file.Length));
        long processedBytes = 0;
        bool targetCommitted = false;
        try
        {
            FfmpegMediaRunResult result = await Task.Run(() => FfmpegMediaEngine.RemuxFiles(
                sources.Select(file => file.FullName).ToArray(),
                temporaryPath,
                VideoRecordingMetadataStore.Load(first),
                operationCancellation.Token,
                bytes =>
                {
                    processedBytes = processedBytes > long.MaxValue - bytes ? long.MaxValue : processedBytes + bytes;
                    progress?.Report(totalBytes > 0 ? Math.Min(99, processedBytes * 100d / totalBytes) : 0);
                }), operationCancellation.Token);
            if (result.WasCanceled)
            {
                throw new OperationCanceledException(operationCancellation.Token);
            }
            if (result.ExitCode != 0 || !result.HadMediaProgress || !File.Exists(temporaryPath)
                || new FileInfo(temporaryPath).Length == 0)
            {
                return new MediaFileWorkflowResult(false, [], result.ErrorOutput);
            }

            File.Move(temporaryPath, targetPath, false);
            targetCommitted = true;
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(first);
            ClearMergedSegmentIdentity(metadata);
            _ = RecordingCoverStore.TryCopyOrCreateFinalizedCover(
                sources.Select(file => file.FullName),
                targetPath,
                metadata,
                metadata.DurationSeconds,
                operationCancellation.Token);
            if (VideoRecordingMetadataStore.HasAnyMetadata(metadata)
                && !VideoRecordingMetadataStore.WriteCompletedMetadata(targetPath, metadata))
            {
                throw new IOException("Failed to store merged recording metadata.");
            }
            progress?.Report(100);
            return new MediaFileWorkflowResult(true, [targetPath]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (targetCommitted)
            {
                DeleteFiles([targetPath]);
            }
            AppSessionLogger.WriteException(exception);
            return new MediaFileWorkflowResult(false, [], exception.Message);
        }
        finally
        {
            DeleteFiles([temporaryPath]);
        }
    }

    internal static void ClearMergedSegmentIdentity(VideoRecordingMetadata metadata)
    {
        metadata.SegmentGroupId = string.Empty;
        metadata.SegmentIndex = -1;
        metadata.SegmentCount = 0;
        metadata.SegmentKind = string.Empty;
        metadata.SegmentReason = string.Empty;
    }

    private static bool HaveCompatibleStreams(IEnumerable<FileInfo> sources)
    {
        string[] signatures = sources
            .Select(source => FfmpegMediaEngine.TryProbe(source.FullName, out FfmpegMediaProbeResult result, out _)
                ? result.StreamSignature
                : string.Empty)
            .ToArray();
        return signatures.All(signature => !string.IsNullOrWhiteSpace(signature))
            && signatures.Distinct(StringComparer.Ordinal).Count() == 1;
    }

    internal static Task<IDisposable?> TryRegisterOperationAsync(
        MediaOperationKind kind,
        IEnumerable<string> sourcePaths,
        Func<IEnumerable<string?>> protectedPaths,
        Action cancel,
        CancellationToken cancellationToken)
    {
        string[] paths = sourcePaths
            .Cast<string?>()
            .Concat(protectedPaths())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MediaOperationRegistry.TryRegister(kind, paths, cancel));
    }

    private static string[] GetSplitTemporaryOutputs(string directory, string temporaryStem, string extension, string temporaryPattern)
    {
        try
        {
            return MediaFileCatalog.OrderSegmentPaths(
                    Directory.EnumerateFiles(directory, $"{temporaryStem}_*{extension}", SearchOption.TopDirectoryOnly),
                    temporaryPattern)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(exception);
            return [];
        }
    }

    private static string GetUniqueSegmentBase(string directory, string stem)
    {
        for (int index = 0; index < 10000; index++)
        {
            string candidate = index == 0 ? stem : $"{stem}_{index:000}";
            if (!Directory.EnumerateFiles(directory, $"{candidate}_*.*", SearchOption.TopDirectoryOnly).Any())
            {
                return candidate;
            }
        }
        return $"{stem}_{Guid.NewGuid():N}";
    }

    private static string GetMergeBaseStem(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        int separator = stem.LastIndexOf('_');
        return separator > 0 && int.TryParse(stem[(separator + 1)..], out _)
            ? stem[..separator]
            : stem;
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int index = 1; index < 10000; index++)
        {
            string candidate = Path.Combine(directory, $"{stem}_{index:000}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(path);
                RecordingAssociatedAssets.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppSessionLogger.WriteException(exception);
            }
        }
    }
}
