using Emerde.Extensions;

namespace Emerde.Tests;

public sealed class ConfigurationMigrationHelperTests
{
    [Fact]
    public void MigrateLegacyThumbnailCache_MovesFilesAndRemovesEmptyLegacyDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-migration-{Guid.NewGuid():N}");
        string source = Path.Combine(root, "video_thumbnails");
        string target = Path.Combine(root, "cache", "video_thumbnails");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "thumbnail.jpg"), "thumbnail");

        try
        {
            ConfigurationMigrationHelper.MigrateLegacyThumbnailCache(source, target);

            Assert.Equal("thumbnail", File.ReadAllText(Path.Combine(target, "thumbnail.jpg")));
            Assert.False(Directory.Exists(source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
