using Emerde.Core;

namespace Emerde.Tests;

public sealed class SpiderTests
{
    [Theory]
    [InlineData("douyin")]
    [InlineData("tiktok")]
    [InlineData("not a url with douyin")]
    public void ParseUrl_WithInvalidInput_ReturnsNull(string input)
    {
        Assert.Null(Spider.ParseUrl(input));
    }

    [Fact]
    public void ParseUrl_WithDouyinLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://live.douyin.com/123456?from=test");

        Assert.Equal("https://live.douyin.com/123456", result);
    }

    [Fact]
    public void ParseUrl_WithTiktokLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://www.tiktok.com/@someone/live");

        Assert.Equal("https://www.tiktok.com/@someone/live", result);
    }

    [Fact]
    public void ParseUrl_WithBilibiliLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://live.bilibili.com/123456?from=test");

        Assert.Equal("https://live.bilibili.com/123456", result);
    }

    [Theory]
    [InlineData("https://example.test/live.m3u8?token=abc", "https://example.test/live.m3u8?token=abc")]
    [InlineData("https://example.test/live.flv", "https://example.test/live.flv")]
    public void ParseUrl_WithDirectStreamUrl_PreservesStreamUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://live.douyin.com/123456", "Douyin")]
    [InlineData("https://www.tiktok.com/@someone/live", "TikTok")]
    [InlineData("https://live.bilibili.com/123456", "Bilibili")]
    [InlineData("https://example.test/live.m3u8", "Direct")]
    [InlineData("https://example.test/page", "")]
    public void GetPlatformName_DetectsSupportedPlatform(string input, string expected)
    {
        Assert.Equal(expected, Spider.GetPlatformName(input));
    }

    [Fact]
    public void GetResult_WithDirectHlsUrl_ReturnsStreamingResult()
    {
        ISpiderResult? result = Spider.GetResult("https://example.test/live/index.m3u8?token=abc");

        Assert.NotNull(result);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("Direct", result.PlatformName);
        Assert.Equal("index.m3u8", result.Nickname);
        Assert.Equal("https://example.test/live/index.m3u8?token=abc", result.HlsUrl);
    }

    [Fact]
    public void BilibiliExtractors_MapRoomMasterAndPlayData()
    {
        BilibiliSpiderResult result = new()
        {
            RoomUrl = "https://live.bilibili.com/123",
            PlatformName = "Bilibili",
        };

        BilibiliSpider.ExtractRoomInfo("""{"code":0,"data":{"room_id":456,"uid":789,"live_status":1}}""", result);
        BilibiliSpider.ExtractMasterInfo("""{"code":0,"data":{"info":{"uname":"anchor","face":"https://example.test/avatar.png"}}}""", result);
        BilibiliSpider.ExtractPlayUrl("""{"code":0,"data":{"durl":[{"url":"https://example.test/live.flv"}]}}""", result);

        Assert.Equal("456", result.RoomId);
        Assert.Equal("789", result.Uid);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("https://live.bilibili.com/456", result.RoomUrl);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }
}
