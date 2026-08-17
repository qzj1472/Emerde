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
        if (HasCurrentFinalizedCover(mediaPath, metadata))
        {
            return true;
        }

        GenerationGate.Wait(token);
        try
        {
            if (HasCurrentFinalizedCover(mediaPath, metadata))
            {
                return true;
            }

            double position = GetFramePosition(metadata.RecordingSessionId, mediaPath, durationSeconds);
            if (!FfmpegMediaEngine.TryExtractCoverFrame(mediaPath, position, token, out BitmapSource frame, out string error))
            {
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
            metadata.CoverCompositionVersion = CurrentCompositionVersion;
            metadata.CoverPath = string.Empty;
            return true;
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
        if (metadata.CoverCompositionVersion >= CurrentCompositionVersion)
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
        }
        try
        {
            return TryEnsureFinalizedCover(targetPath, metadata, durationSeconds, token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal static string MaterializeDisplayImage(string mediaPath, VideoRecordingMetadata metadata, string cachePath)
    {
        byte[] bytes;
        DateTime sourceWriteTimeUtc;
        if (TryReadCover(mediaPath, out bytes, out sourceWriteTimeUtc))
        {
            return WriteDisplayCache(cachePath, bytes, sourceWriteTimeUtc);
        }
        if (metadata.RecordingAvatar.Length > 0)
        {
            sourceWriteTimeUtc = GetMetadataWriteTimeUtc(mediaPath);
            return WriteDisplayCache(cachePath, metadata.RecordingAvatar, sourceWriteTimeUtc);
        }
        return string.Empty;
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
        return TryReadCover(mediaPath, out _);
    }

    internal static bool HasCurrentFinalizedCover(string mediaPath, VideoRecordingMetadata metadata)
    {
        return metadata.CoverCompositionVersion >= CurrentCompositionVersion && HasFinalizedCover(mediaPath);
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
        DrawingVisual visual = new();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            Rect canvas = new(0, 0, CoverWidth, CoverHeight);
            ImageBrush frameBrush = new(frame)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };
            drawing.DrawRectangle(frameBrush, null, canvas);
            if (avatar != null)
            {
                ImageBrush avatarBrush = new(avatar)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                };
                LinearGradientBrush avatarMask = CreateHorizontalMask(
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Colors.Transparent, 0.475d),
                    new GradientStop(Colors.White, 0.88d),
                    new GradientStop(Colors.White, 1));
                drawing.PushOpacityMask(avatarMask);
                drawing.DrawRectangle(avatarBrush, null, new Rect(CoverWidth / 3d, 0, CoverWidth * 2d / 3d, CoverHeight));
                drawing.Pop();
            }
        }

        RenderTargetBitmap target = new(CoverWidth, CoverHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return EncodePng(target);
    }

    private static LinearGradientBrush CreateHorizontalMask(params GradientStop[] stops)
    {
        LinearGradientBrush brush = new(new GradientStopCollection(stops), new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        brush.Freeze();
        return brush;
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

    private static bool TryReadCover(string mediaPath, out byte[] bytes, out DateTime lastWriteTimeUtc)
    {
        foreach (string path in new[] { mediaPath + CoverStreamSuffix, GetCoverSidecarPath(mediaPath) })
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

    private static string WriteDisplayCache(string cachePath, byte[] bytes, DateTime sourceWriteTimeUtc)
    {
        try
        {
            if (File.Exists(cachePath)
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
}
