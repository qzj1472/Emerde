using Emerde.Core;

namespace Emerde.Tests;

public sealed class RecordingCleanupServiceTests
{
    [Fact]
    public void StagedVideoMetadata_CommitsAndCanRollBackTheFinalSidecar()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string mediaPath = Path.Combine(directory, "record.mp4");
        string metadataPath = Path.Combine(directory, "record.mplr.json");
        try
        {
            using StagedVideoMetadata? staged = VideoRecordingMetadataStore.StageSidecarForMedia(
                mediaPath,
                new VideoRecordingMetadata { NickName = "Host" },
                "test-metadata");

            Assert.NotNull(staged);
            Assert.False(File.Exists(metadataPath));
            Assert.Equal(metadataPath, staged.Commit());
            Assert.True(File.Exists(metadataPath));
            staged.DeleteCommitted();
            Assert.False(File.Exists(metadataPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DeleteOrphanedSidecars_PreservesMetadataWithMediaAndDeletesOrphans()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "kept.mp4"), [1]);
            File.WriteAllText(Path.Combine(directory, "kept.mplr.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "orphan.mplr.json"), "{}");

            Assert.Equal(1, VideoRecordingMetadataStore.DeleteOrphanedSidecars(directory));
            Assert.True(File.Exists(Path.Combine(directory, "kept.mplr.json")));
            Assert.False(File.Exists(Path.Combine(directory, "orphan.mplr.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, DataRetentionUnitHelper.Days, 1)]
    [InlineData(10000, DataRetentionUnitHelper.Days, DataRetentionUnitHelper.MaximumValue)]
    public void RetentionDuration_ClampsImportedValues(int value, int unit, int expectedDays)
    {
        Assert.Equal(TimeSpan.FromDays(expectedDays), DataRetentionUnitHelper.ToTimeSpan(value, unit));
    }

    [Fact]
    public async Task RunAsync_WhenDataRetentionDisabled_DoesNotDeleteExpiredMedia()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "old.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        File.SetLastWriteTime(mediaPath, DateTime.Now.AddDays(-10));

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(false);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync();

            Assert.True(File.Exists(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenEnabled_DoesNotDeleteUnownedMedia()
    {
        await VerifyCleanupAsync(hasMetadata: false, expectedExists: true);
    }

    [Fact]
    public async Task RunAsync_WhenEnabled_DeletesOwnedExpiredMedia()
    {
        await VerifyCleanupAsync(hasMetadata: true, expectedExists: false);
    }

    [Fact]
    public async Task RunAsync_WhenEnabled_DeletesExpiredMediaWithAttachedMetadata()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "old.ts");
        await File.WriteAllTextAsync(mediaPath, "media");
        Assert.True(VideoRecordingMetadataStore.WriteCompletedMetadata(mediaPath, new VideoRecordingMetadata
        {
            FileName = "old.ts",
            NickName = "Host",
            RoomUrl = "https://example.test/room",
            RecordedAt = DateTime.Now.AddDays(-10),
        }));
        File.SetLastWriteTime(mediaPath, DateTime.Now.AddDays(-10));

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync();

            Assert.False(File.Exists(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenEnabled_PreservesProtectedExpiredMedia()
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "old.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        File.SetLastWriteTime(mediaPath, DateTime.Now.AddDays(-10));
        _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "old", new VideoRecordingMetadata
        {
            FileName = "old.mp4",
            NickName = "Host",
        });

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);
            using IDisposable operation = MediaOperationRegistry.Register(MediaOperationKind.Conversion, () => [mediaPath]);

            await RecordingCleanupService.RunAsync();

            Assert.True(File.Exists(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public void Load_QuarantinesCorruptSidecar()
    {
        string directory = Path.Combine(Path.GetTempPath(), "emerde-metadata-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string videoPath = Path.Combine(directory, "record.mp4");
        string metadataPath = Path.Combine(directory, "record.mplr.json");
        File.WriteAllText(videoPath, "media");
        File.WriteAllText(metadataPath, "{");

        try
        {
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(videoPath));

            Assert.False(VideoRecordingMetadataStore.HasAnyMetadata(metadata));
            Assert.False(File.Exists(metadataPath));
            Assert.True(File.Exists(metadataPath + ".invalid"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TryDeleteSidecar_RetainsMetadataForConvertedTarget()
    {
        string directory = Path.Combine(Path.GetTempPath(), "emerde-metadata-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "record.ts");
        string targetPath = Path.Combine(directory, "record.mp4");
        File.WriteAllText(targetPath, "target");
        string? metadataPath = VideoRecordingMetadataStore.WriteSidecar(directory, "record", new VideoRecordingMetadata
        {
            FileName = "record.mp4",
            RoomUrl = "https://example.test/room",
        });

        try
        {
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(sourcePath);

            Assert.NotNull(metadataPath);
            Assert.True(File.Exists(metadataPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task VerifyCleanupAsync(bool hasMetadata, bool expectedExists)
    {
        string oldSaveFolder = Configurations.SaveFolder.Get();
        bool oldEnabled = Configurations.IsDataRetentionEnabled.Get();
        int oldValue = Configurations.DataRetentionValue.Get();
        int oldUnit = Configurations.DataRetentionUnit.Get();
        string tempFolder = Path.Combine(Path.GetTempPath(), "emerde-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string mediaPath = Path.Combine(tempFolder, "old.mp4");
        await File.WriteAllTextAsync(mediaPath, "media");
        File.SetLastWriteTime(mediaPath, DateTime.Now.AddDays(-10));
        if (hasMetadata)
        {
            _ = VideoRecordingMetadataStore.WriteSidecar(tempFolder, "old", new VideoRecordingMetadata
            {
                FileName = "old.mp4",
                NickName = "Host",
                RoomUrl = "https://example.test/room",
                RecordedAt = DateTime.Now.AddDays(-10),
            });
        }

        try
        {
            Configurations.SaveFolder.Set(tempFolder);
            Configurations.IsDataRetentionEnabled.Set(true);
            Configurations.DataRetentionValue.Set(1);
            Configurations.DataRetentionUnit.Set(DataRetentionUnitHelper.Days);

            await RecordingCleanupService.RunAsync();

            Assert.Equal(expectedExists, File.Exists(mediaPath));
        }
        finally
        {
            Configurations.SaveFolder.Set(oldSaveFolder);
            Configurations.IsDataRetentionEnabled.Set(oldEnabled);
            Configurations.DataRetentionValue.Set(oldValue);
            Configurations.DataRetentionUnit.Set(oldUnit);
            Directory.Delete(tempFolder, recursive: true);
        }
    }
}
