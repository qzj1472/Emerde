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
}
