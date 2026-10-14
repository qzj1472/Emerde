using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Emerde.Core;

internal static class RecordingCoverStore
{
    internal const int CurrentCompositionVersion = 4;
    private const string CoverStreamSuffix = ":emerde.cover";
    private const string CoverSidecarSuffix = ".emerde-cover.png";
    private const int MaximumAvatarBytes = 5 * 1024 * 1024;
    private const int CoverWidth = 480;
    private const int CoverHeight = 320;
    private static readonly SemaphoreSlim GenerationGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, UnavailableCoverState> UnavailableCoverStates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan UnavailableCoverRetryInterval = TimeSpan.FromMinutes(5);
    private const int MaximumUnavailableCoverStates = 512;

    internal static byte[] CaptureAvatarSnapshot(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return [];
        }

        try
        {
            FileInfo file = new(source);
            if (file.Length <= 0 || file.Length > MaximumAvatarBytes)
            {
                return [];
            }
            byte[] bytes = File.ReadAllBytes(source);
            return AvatarCache.IsDecodableImage(bytes) ? NormalizeAvatar(bytes) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    internal static bool TryEnsureFinalizedCover(
        string mediaPath,
        VideoRecordingMetadata metadata,
        double durationSeconds,
        CancellationToken token)
    {
        if (!File.Exists(mediaPath))
        {
            return false;
        }
        if (HasFinalizedCover(mediaPath))
        {
            return true;
        }
        if (!ShouldGenerateNewCover(metadata))
        {
            return false;
        }
        if (HasCurrentFinalizedCover(mediaPath, metadata))
        {
            return true;
        }
        if (ShouldSkipUnavailableCover(mediaPath))
        {
            return false;
        }

        GenerationGate.Wait(token);
        try
        {
            if (HasCurrentFinalizedCover(mediaPath, metadata))
            {
                return true;
            }
            if (ShouldSkipUnavailableCover(mediaPath))
            {
                return false;
            }

            double position = GetFramePosition(metadata.RecordingSessionId, mediaPath, durationSeconds);
            if (!FfmpegMediaEngine.TryExtractCoverFrame(mediaPath, position, token, out BitmapSource frame, out string error))
            {
                if (IsUnavailableVideoStreamError(error))
                {
                    RememberUnavailableCover(mediaPath);
                }
                if (!string.IsNullOrWhiteSpace(error))
                {
                    AppSessionLogger.Event("warn", "media", "recording_cover_frame_failed", error, new { mediaPath, position });
                }
                return false;
            }

            if (metadata.RecordingAvatar.Length == 0)
            {
                metadata.RecordingAvatar = CaptureAvatarSnapshot(metadata.CoverPath);
            }
            BitmapSource? avatar = DecodeImage(metadata.RecordingAvatar);
            byte[] cover = ComposeCover(frame, avatar);
            if (!TryWriteCover(mediaPath, cover))
            {
                return false;
            }
            UnavailableCoverStates.TryRemove(Path.GetFullPath(mediaPath), out _);
            metadata.CoverCompositionVersion = CurrentCompositionVersion;
            metadata.CoverPath = string.Empty;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            AppSessionLogger.Event("warn", "media", "recording_cover_failed", exception.Message, new { mediaPath });
            return false;
        }
        finally
        {
            GenerationGate.Release();
        }
    }

    internal static bool TryCopyOrCreateFinalizedCover(
        IEnumerable<string> sourcePaths,
        string targetPath,
        VideoRecordingMetadata metadata,
        double durationSeconds,
        CancellationToken token)
    {
        foreach (string sourcePath in sourcePaths)
        {
            if (TryReadCover(sourcePath, out byte[] cover) && TryWriteCover(targetPath, cover))
            {
                metadata.CoverCompositionVersion = CurrentCompositionVersion;
                metadata.CoverPath = string.Empty;
                return true;
            }
        }
        if (HasFinalizedCover(targetPath))
        {
            return true;
        }
        if (!ShouldGenerateNewCover(metadata))
        {
            return false;
        }
        try
        {
            return TryEnsureFinalizedCover(targetPath, metadata, durationSeconds, token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            AppSessionLogger.Event("warn", "media", "recording_cover_failed", exception.Message, new { targetPath });
            return false;
        }
    }

    internal static string MaterializeDisplayImage(
        string mediaPath,
        VideoRecordingMetadata metadata,
        string cachePath,
        bool allowAvatarFallback = true)
    {
        if (TryGetCoverWriteTimeUtc(mediaPath, out DateTime coverWriteTimeUtc)
            && IsDisplayCacheCurrent(cachePath, coverWriteTimeUtc))
        {
            return cachePath;
        }
        if (TryReadCover(mediaPath, out byte[] bytes, out DateTime sourceWriteTimeUtc))
        {
            return WriteDisplayCache(cachePath, bytes, sourceWriteTimeUtc);
        }
        if (allowAvatarFallback && metadata.RecordingAvatar.Length > 0)
        {
            sourceWriteTimeUtc = GetMetadataWriteTimeUtc(mediaPath);
            if (IsDisplayCacheCurrent(cachePath, sourceWriteTimeUtc))
            {
                return cachePath;
            }
            return WriteDisplayCache(cachePath, metadata.RecordingAvatar, sourceWriteTimeUtc);
        }
        return string.Empty;
    }

    internal static string MaterializeRecordingAvatar(string mediaPath, VideoRecordingMetadata metadata, string cachePath)
    {
        if (metadata.RecordingAvatar.Length == 0)
        {
            return string.Empty;
        }

        DateTime sourceWriteTimeUtc = VideoRecordingMetadataStore.GetMetadataCandidates(new FileInfo(mediaPath))
            .Where(File.Exists)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(File.GetCreationTimeUtc(mediaPath))
            .Max();
        return WriteDisplayCache(cachePath, metadata.RecordingAvatar, sourceWriteTimeUtc, force: true);
    }

    internal static string GetDisplayCachePath(string mediaPath, string cacheDirectory)
    {
        string fullPath = Path.GetFullPath(mediaPath);
        string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(fullPath);
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.Combine(directory, stem).ToUpperInvariant()));
        return Path.Combine(cacheDirectory, $"{Convert.ToHexString(hash)[..24].ToLowerInvariant()}.jpg");
    }

    internal static bool HasFinalizedCover(string mediaPath)
    {
        return TryGetCoverWriteTimeUtc(mediaPath, out _);
    }

    internal static bool HasCurrentFinalizedCover(string mediaPath, VideoRecordingMetadata metadata)
    {
        return metadata.CoverCompositionVersion >= CurrentCompositionVersion && HasFinalizedCover(mediaPath);
    }

    internal static bool ShouldGenerateNewCover(VideoRecordingMetadata metadata)
    {
        return !string.IsNullOrWhiteSpace(metadata.RecordingSessionId)
            && metadata.RecordedAt > DateTime.MinValue;
    }

    internal static bool IsUnavailableVideoStreamError(string? error)
    {
        return string.Equals(error, "video stream was not found", StringComparison.OrdinalIgnoreCase);
    }

    internal static void DeleteAssociatedAssets(string mediaPath)
    {
        TryDelete(GetCoverSidecarPath(mediaPath));
        TryDelete(GetDisplayCachePath(mediaPath, AppPaths.ThumbnailCacheDirectory));
    }

    internal static bool TryCopyAssociatedCover(string sourcePath, string targetPath)
    {
        TryDelete(GetDisplayCachePath(targetPath, AppPaths.ThumbnailCacheDirectory));
        return !TryReadCover(sourcePath, out byte[] bytes) || TryWriteCover(targetPath, bytes);
    }

    internal static bool TryMoveAssociatedCover(string sourcePath, string targetPath)
    {
        bool sourceHasCover = TryReadCover(sourcePath, out byte[] bytes);
        bool targetHasCover = TryReadCover(targetPath, out _);
        if (sourceHasCover && !targetHasCover && !TryWriteCover(targetPath, bytes))
        {
            return false;
        }
        DeleteAssociatedAssets(sourcePath);
        TryDelete(GetDisplayCachePath(targetPath, AppPaths.ThumbnailCacheDirectory));
        return true;
    }

    internal static string GetCoverSidecarPath(string mediaPath)
    {
        return mediaPath + CoverSidecarSuffix;
    }

    private static byte[] NormalizeAvatar(byte[] bytes)
    {
        BitmapSource? source = DecodeImage(bytes);
        if (source == null)
        {
            return [];
        }
        double scale = Math.Min(1d, 512d / Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource normalized = scale < 1d
            ? new TransformedBitmap(source, new ScaleTransform(scale, scale))
            : source;
        normalized.Freeze();
        return EncodePng(normalized);
    }

    private static BitmapSource? DecodeImage(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }
        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    internal static byte[] ComposeCover(BitmapSource frame, BitmapSource? avatar)
    {
        byte[] framePixels = CopyBgraPixels(frame, out int frameWidth, out int frameHeight);
        byte[] output = new byte[CoverWidth * CoverHeight * 4];
        for (int y = 0; y < CoverHeight; y++)
        {
            for (int x = 0; x < CoverWidth; x++)
            {
                int outputIndex = (y * CoverWidth + x) * 4;
                CopyUniformToFillPixel(framePixels, frameWidth, frameHeight, x, y, CoverWidth, CoverHeight, output, outputIndex);
            }
        }

        if (avatar != null)
        {
            byte[] avatarPixels = CopyBgraPixels(avatar, out int avatarWidth, out int avatarHeight);
            int avatarLeft = CoverWidth / 3;
            int avatarWidthOnCover = CoverWidth - avatarLeft;
            for (int y = 0; y < CoverHeight; y++)
            {
                for (int x = avatarLeft; x < CoverWidth; x++)
                {
                    double relativeX = (x - avatarLeft) / (double)(avatarWidthOnCover - 1);
                    double mask = relativeX <= 0.475d
                        ? 0d
                        : relativeX >= 0.88d
                            ? 1d
                            : (relativeX - 0.475d) / 0.405d;
                    if (mask <= 0d)
                    {
                        continue;
                    }

                    int outputIndex = (y * CoverWidth + x) * 4;
                    int avatarIndex = GetUniformToFillPixelIndex(avatarWidth, avatarHeight, x - avatarLeft, y, avatarWidthOnCover, CoverHeight);
                    double alpha = avatarPixels[avatarIndex + 3] / 255d * mask;
                    if (alpha <= 0d)
                    {
                        continue;
                    }

                    output[outputIndex] = BlendChannel(output[outputIndex], avatarPixels[avatarIndex], alpha);
                    output[outputIndex + 1] = BlendChannel(output[outputIndex + 1], avatarPixels[avatarIndex + 1], alpha);
                    output[outputIndex + 2] = BlendChannel(output[outputIndex + 2], avatarPixels[avatarIndex + 2], alpha);
                    output[outputIndex + 3] = 255;
                }
            }
        }

        BitmapSource result = BitmapSource.Create(
            CoverWidth,
            CoverHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            output,
            CoverWidth * 4);
        result.Freeze();
        return EncodePng(result);
    }

    private static byte[] CopyBgraPixels(BitmapSource source, out int width, out int height)
    {
        BitmapSource converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        width = converted.PixelWidth;
        height = converted.PixelHeight;
        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static void CopyUniformToFillPixel(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int targetX,
        int targetY,
        int targetWidth,
        int targetHeight,
        byte[] target,
        int targetIndex)
    {
        int sourceIndex = GetUniformToFillPixelIndex(sourceWidth, sourceHeight, targetX, targetY, targetWidth, targetHeight);
        target[targetIndex] = source[sourceIndex];
        target[targetIndex + 1] = source[sourceIndex + 1];
        target[targetIndex + 2] = source[sourceIndex + 2];
        target[targetIndex + 3] = 255;
    }

    private static int GetUniformToFillPixelIndex(
        int sourceWidth,
        int sourceHeight,
        int targetX,
        int targetY,
        int targetWidth,
        int targetHeight)
    {
        double scale = Math.Max(targetWidth / (double)sourceWidth, targetHeight / (double)sourceHeight);
        double visibleWidth = targetWidth / scale;
        double visibleHeight = targetHeight / scale;
        int sourceX = Math.Clamp((int)Math.Floor((targetX + 0.5d) / scale + (sourceWidth - visibleWidth) / 2d), 0, sourceWidth - 1);
        int sourceY = Math.Clamp((int)Math.Floor((targetY + 0.5d) / scale + (sourceHeight - visibleHeight) / 2d), 0, sourceHeight - 1);
        return (sourceY * sourceWidth + sourceX) * 4;
    }

    private static byte BlendChannel(byte background, byte foreground, double alpha)
    {
        return (byte)Math.Clamp(Math.Round(foreground * alpha + background * (1d - alpha)), 0d, 255d);
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static double GetFramePosition(string recordingSessionId, string mediaPath, double durationSeconds)
    {
        if (durationSeconds <= 2)
        {
            return 0;
        }
        string seed = string.IsNullOrWhiteSpace(recordingSessionId) ? Path.GetFullPath(mediaPath) : recordingSessionId;
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        ulong value = BitConverter.ToUInt64(hash, 0);
        double fraction = 0.1d + value / (double)ulong.MaxValue * 0.8d;
        return Math.Clamp(durationSeconds * fraction, 1d, Math.Max(1d, durationSeconds - 1d));
    }

    private static bool TryWriteCover(string mediaPath, byte[] bytes)
    {
        bool written;
        try
        {
            using FileStream stream = new(mediaPath + CoverStreamSuffix, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            TryDelete(GetCoverSidecarPath(mediaPath));
            written = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            written = TryWriteSidecar(mediaPath, bytes);
        }
        if (written)
        {
            TryDelete(GetDisplayCachePath(mediaPath, AppPaths.ThumbnailCacheDirectory));
        }
        return written;
    }

    private static bool TryWriteSidecar(string mediaPath, byte[] bytes)
    {
        string path = GetCoverSidecarPath(mediaPath);
        string temporaryPath = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static bool TryReadCover(string mediaPath, out byte[] bytes)
    {
        return TryReadCover(mediaPath, out bytes, out _);
    }

    private static bool TryGetCoverWriteTimeUtc(string mediaPath, out DateTime lastWriteTimeUtc)
    {
        foreach (string path in GetCoverCandidatePaths(mediaPath))
        {
            try
            {
                FileInfo file = new(path);
                if (!file.Exists || file.Length <= 0 || file.Length > 16 * 1024 * 1024)
                {
                    continue;
                }
                lastWriteTimeUtc = file.LastWriteTimeUtc;
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }
        lastWriteTimeUtc = DateTime.MinValue;
        return false;
    }

    private static bool IsDisplayCacheCurrent(string cachePath, DateTime sourceWriteTimeUtc)
    {
        try
        {
            FileInfo cache = new(cachePath);
            return cache.Exists && cache.Length > 0 && cache.LastWriteTimeUtc >= sourceWriteTimeUtc;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryReadCover(string mediaPath, out byte[] bytes, out DateTime lastWriteTimeUtc)
    {
        foreach (string path in GetCoverCandidatePaths(mediaPath))
        {
            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length <= 0 || stream.Length > 16 * 1024 * 1024)
                {
                    continue;
                }
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }
        bytes = [];
        lastWriteTimeUtc = DateTime.MinValue;
        return false;
    }

    private static IEnumerable<string> GetCoverCandidatePaths(string mediaPath)
    {
        yield return mediaPath + CoverStreamSuffix;
        yield return GetCoverSidecarPath(mediaPath);
    }

    private static string WriteDisplayCache(string cachePath, byte[] bytes, DateTime sourceWriteTimeUtc, bool force = false)
    {
        try
        {
            if (!force
                && File.Exists(cachePath)
                && new FileInfo(cachePath).Length > 0
                && File.GetLastWriteTimeUtc(cachePath) >= sourceWriteTimeUtc)
            {
                return cachePath;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            string temporaryPath = cachePath + $".tmp-{Guid.NewGuid():N}";
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                File.Move(temporaryPath, cachePath, true);
                File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow);
                return cachePath;
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static DateTime GetMetadataWriteTimeUtc(string mediaPath)
    {
        DateTime latest = File.GetLastWriteTimeUtc(mediaPath);
        foreach (string metadataPath in VideoRecordingMetadataStore.GetMetadataCandidates(new FileInfo(mediaPath)))
        {
            if (File.Exists(metadataPath))
            {
                latest = latest > File.GetLastWriteTimeUtc(metadataPath) ? latest : File.GetLastWriteTimeUtc(metadataPath);
            }
        }
        return latest;
    }

    private static bool ShouldSkipUnavailableCover(string mediaPath)
    {
        string key = Path.GetFullPath(mediaPath);
        if (!UnavailableCoverStates.TryGetValue(key, out UnavailableCoverState state))
        {
            return false;
        }

        if (state.ExpiresAtUtc <= DateTime.UtcNow
            || !TryGetMediaStamp(mediaPath, out long length, out DateTime lastWriteTimeUtc)
            || state.Length != length
            || state.LastWriteTimeUtc != lastWriteTimeUtc)
        {
            UnavailableCoverStates.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    private static void RememberUnavailableCover(string mediaPath)
    {
        if (TryGetMediaStamp(mediaPath, out long length, out DateTime lastWriteTimeUtc))
        {
            UnavailableCoverStates[Path.GetFullPath(mediaPath)] = new UnavailableCoverState(
                length,
                lastWriteTimeUtc,
                DateTime.UtcNow + UnavailableCoverRetryInterval);
            TrimUnavailableCoverStates();
        }
    }

    private static void TrimUnavailableCoverStates()
    {
        if (UnavailableCoverStates.Count <= MaximumUnavailableCoverStates)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        foreach ((string key, UnavailableCoverState state) in UnavailableCoverStates)
        {
            if (state.ExpiresAtUtc <= now)
            {
                UnavailableCoverStates.TryRemove(key, out _);
            }
        }

        int excess = UnavailableCoverStates.Count - MaximumUnavailableCoverStates;
        if (excess <= 0)
        {
            return;
        }

        foreach (string key in UnavailableCoverStates.Keys.Take(excess).ToArray())
        {
            UnavailableCoverStates.TryRemove(key, out _);
        }
    }

    private static bool TryGetMediaStamp(string mediaPath, out long length, out DateTime lastWriteTimeUtc)
    {
        try
        {
            FileInfo file = new(mediaPath);
            file.Refresh();
            length = file.Length;
            lastWriteTimeUtc = file.LastWriteTimeUtc;
            return file.Exists;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            length = 0;
            lastWriteTimeUtc = DateTime.MinValue;
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private readonly record struct UnavailableCoverState(long Length, DateTime LastWriteTimeUtc, DateTime ExpiresAtUtc);
}
