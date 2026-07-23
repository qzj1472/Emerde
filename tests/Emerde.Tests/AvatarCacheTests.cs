using Emerde.Core;

namespace Emerde.Tests;

public sealed class AvatarCacheTests
{
    [Fact]
    public void HashRoomUrl_NormalizesSchemeCaseQueryAndTrailingSlash()
    {
        string first = AvatarCache.HashRoomUrl("https://LIVE.DOUYIN.COM/123/?source=test");
        string second = AvatarCache.HashRoomUrl("live.douyin.com/123");

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetCachedAvatarSource_ReturnsOnlyNonEmptyCacheFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-avatar-{Guid.NewGuid():N}");
        try
        {
            string path = AvatarCache.GetCachedAvatarPath("https://live.douyin.com/123", directory);
            Assert.Equal(string.Empty, AvatarCache.GetCachedAvatarSource("https://live.douyin.com/123", directory));

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

            Assert.Equal(path, AvatarCache.GetCachedAvatarSource("https://live.douyin.com/123", directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void IsDecodableImage_RejectsAHeaderWithoutImageData()
    {
        Assert.False(AvatarCache.IsDecodableImage([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
    }
}
