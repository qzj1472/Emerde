using Emerde.Core;

namespace Emerde.Tests;

public sealed class RecordingCleanupServiceTests
{
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
