using Emerde.Core;
using Emerde.Views;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Emerde.Tests;

public sealed class RecordingCoverStoreTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+XhoWAAAAAElFTkSuQmCC");

    [Fact]
    public void Runtime_IncludesCoverFramePixelScaler()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg", "swscale-8.dll")));
    }

    [Fact]
    public void CaptureAvatarSnapshot_RemainsUsableAfterSharedAvatarIsDeleted()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-cover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string avatarPath = Path.Combine(root, "room.avatar");
        string mediaPath = Path.Combine(root, "record.mp4");
        string cachePath = Path.Combine(root, "cache", "cover.jpg");
        try
        {
            File.WriteAllBytes(avatarPath, Png);
            File.WriteAllText(mediaPath, "media");

            byte[] snapshot = RecordingCoverStore.CaptureAvatarSnapshot(avatarPath);
            File.Delete(avatarPath);
            string displayPath = RecordingCoverStore.MaterializeDisplayImage(
                mediaPath,
                new VideoRecordingMetadata { RecordingAvatar = snapshot },
                cachePath);

            Assert.NotEmpty(snapshot);
            Assert.Equal(cachePath, displayPath);
            Assert.True(File.Exists(displayPath));
            Assert.NotNull(ThumbnailImageConverter.LoadImage(displayPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ComposeCover_KeepsVideoOpaqueAndLimitsAvatarToRightThirtyFivePercent()
    {
        BitmapSource frame = CreatePixel(0, 0, 255);
        BitmapSource avatar = CreatePixel(255, 0, 0);
        byte[] cover = RecordingCoverStore.ComposeCover(frame, avatar);
        BitmapSource image = Decode(cover);

        Assert.Equal([0, 0, 255, 255], GetPixel(image, 0, 160));
        Assert.Equal([0, 0, 255, 255], GetPixel(image, 311, 160));
        byte[] fadePixel = GetPixel(image, 360, 160);
        Assert.True(fadePixel[0] > 0);
        Assert.True(fadePixel[2] > 0);
        byte[] lateFadePixel = GetPixel(image, 418, 160);
        Assert.True(lateFadePixel[0] > 0);
        Assert.True(lateFadePixel[2] > 0);
        Assert.Equal([255, 0, 0, 255], GetPixel(image, 442, 160));
        Assert.Equal([255, 0, 0, 255], GetPixel(image, 479, 160));
    }

    [Fact]
    public void DeleteAssociatedAssets_RemovesSidecarAndDisplayCache()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-cover-cleanup-{Guid.NewGuid():N}");
        string cacheRoot = AppPaths.ThumbnailCacheDirectory;
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(cacheRoot);
        string mediaPath = Path.Combine(root, "record.mp4");
        string sidecarPath = RecordingCoverStore.GetCoverSidecarPath(mediaPath);
        string cachePath = RecordingCoverStore.GetDisplayCachePath(mediaPath, cacheRoot);
        try
        {
            File.WriteAllText(mediaPath, "media");
            File.WriteAllBytes(sidecarPath, Png);
            File.WriteAllBytes(cachePath, Png);

            Assert.True(RecordingCoverStore.HasFinalizedCover(mediaPath));
            Assert.False(RecordingCoverStore.HasCurrentFinalizedCover(mediaPath, new VideoRecordingMetadata()));
            Assert.True(RecordingCoverStore.HasCurrentFinalizedCover(mediaPath, new VideoRecordingMetadata
            {
                CoverCompositionVersion = RecordingCoverStore.CurrentCompositionVersion,
            }));

            RecordingCoverStore.DeleteAssociatedAssets(mediaPath);

            Assert.False(File.Exists(sidecarPath));
            Assert.False(File.Exists(cachePath));
            Assert.False(RecordingCoverStore.HasFinalizedCover(mediaPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            File.Delete(cachePath);
        }
    }

    [Fact]
    public void TryCopyOrCreateFinalizedCover_WhenCanceledAfterOutputCommit_SkipsCoverWithoutThrowing()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-cover-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string mediaPath = Path.Combine(root, "committed.mp4");
        File.WriteAllText(mediaPath, "media");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            bool created = RecordingCoverStore.TryCopyOrCreateFinalizedCover(
                [],
                mediaPath,
                new VideoRecordingMetadata(),
                1,
                cancellation.Token);

            Assert.False(created);
            Assert.True(File.Exists(mediaPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryCopyOrCreateFinalizedCover_CopiesFallbackSidecarAfterMediaRename()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-cover-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string oldPath = Path.Combine(root, "recording.mp4");
        string finalPath = Path.Combine(root, "final.mp4");
        File.WriteAllBytes(RecordingCoverStore.GetCoverSidecarPath(oldPath), Png);
        File.WriteAllText(finalPath, "media");
        try
        {
            VideoRecordingMetadata metadata = new()
            {
                CoverCompositionVersion = RecordingCoverStore.CurrentCompositionVersion,
            };

            bool copied = RecordingCoverStore.TryCopyOrCreateFinalizedCover(
                [oldPath],
                finalPath,
                metadata,
                1,
                CancellationToken.None);

            Assert.True(copied);
            Assert.True(RecordingCoverStore.HasCurrentFinalizedCover(finalPath, metadata));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AssociatedAssets_CopyPreservesCoverAndRepairReport()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-associated-copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string sourcePath = Path.Combine(root, "source.mp4");
        string targetPath = Path.Combine(root, "target.mp4");
        try
        {
            File.WriteAllText(sourcePath, "media");
            File.WriteAllText(targetPath, "media");
            File.WriteAllBytes(RecordingCoverStore.GetCoverSidecarPath(sourcePath), Png);
            File.WriteAllText(sourcePath + VideoRepairService.RepairReportSuffix, "report");

            Assert.True(RecordingAssociatedAssets.Copy(sourcePath, targetPath));

            Assert.True(RecordingCoverStore.HasFinalizedCover(targetPath));
            Assert.Equal("report", File.ReadAllText(targetPath + VideoRepairService.RepairReportSuffix));
            Assert.True(RecordingCoverStore.HasFinalizedCover(sourcePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AssociatedAssets_MovePreservesCoverAndRemovesOldRepairReport()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-associated-move-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string sourcePath = Path.Combine(root, "source.mp4");
        string targetPath = Path.Combine(root, "target.mp4");
        try
        {
            File.WriteAllText(sourcePath, "media");
            File.WriteAllBytes(RecordingCoverStore.GetCoverSidecarPath(sourcePath), Png);
            File.WriteAllText(sourcePath + VideoRepairService.RepairReportSuffix, "report");
            File.Move(sourcePath, targetPath);

            Assert.True(RecordingAssociatedAssets.Move(sourcePath, targetPath));

            Assert.True(RecordingCoverStore.HasFinalizedCover(targetPath));
            Assert.False(File.Exists(RecordingCoverStore.GetCoverSidecarPath(sourcePath)));
            Assert.False(File.Exists(sourcePath + VideoRepairService.RepairReportSuffix));
            Assert.Equal("report", File.ReadAllText(targetPath + VideoRepairService.RepairReportSuffix));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BitmapSource CreatePixel(byte blue, byte green, byte red)
    {
        BitmapSource image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { blue, green, red, 255 }, 4);
        image.Freeze();
        return image;
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        BitmapSource image = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        image.Freeze();
        return image;
    }

    private static byte[] GetPixel(BitmapSource image, int x, int y)
    {
        byte[] pixel = new byte[4];
        image.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel;
    }
}
