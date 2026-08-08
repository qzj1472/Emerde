using Emerde.Core;

namespace Emerde.Tests;

public sealed class AvatarCacheTests
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

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
            File.WriteAllBytes(path, ValidPng);

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
    public void GetVersionedAvatarPath_ChangesWithDownloadedContent()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-avatar-{Guid.NewGuid():N}");
        const string roomUrl = "https://live.douyin.com/123";

        string first = AvatarCache.GetVersionedAvatarPath(roomUrl, "first", directory);
        string second = AvatarCache.GetVersionedAvatarPath(roomUrl, "second", directory);

        Assert.NotEqual(first, second);
        Assert.StartsWith(AvatarCache.GetCachedAvatarPath(roomUrl, directory)[..^".avatar".Length], first);
        Assert.EndsWith(".first.avatar", first);
        Assert.EndsWith(".second.avatar", second);
    }

    [Fact]
    public void GetCachedAvatarSource_ReturnsNewestUsableVersion()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-avatar-{Guid.NewGuid():N}");
        const string roomUrl = "https://live.douyin.com/123";
        try
        {
            Directory.CreateDirectory(directory);
            string legacyPath = AvatarCache.GetCachedAvatarPath(roomUrl, directory);
            string versionedPath = AvatarCache.GetVersionedAvatarPath(roomUrl, "new", directory);
            string invalidPath = AvatarCache.GetVersionedAvatarPath(roomUrl, "invalid", directory);
            File.WriteAllBytes(legacyPath, ValidPng);
            File.WriteAllBytes(versionedPath, ValidPng);
            File.WriteAllBytes(invalidPath, [0x00]);
            File.SetLastWriteTimeUtc(legacyPath, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(versionedPath, DateTime.UtcNow.AddMinutes(-1));
            File.SetLastWriteTimeUtc(invalidPath, DateTime.UtcNow);

            Assert.Equal(versionedPath, AvatarCache.GetCachedAvatarSource(roomUrl, directory));
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
