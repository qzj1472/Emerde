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

    [Fact]
    public void ParseUrl_WithKuaishouLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://live.kuaishou.com/u/example?fid=test");

        Assert.Equal("https://live.kuaishou.com/u/example", result);
    }

    [Theory]
    [InlineData("https://www.bigo.tv/cn/123456?from=test", "https://www.bigo.tv/123456")]
    [InlineData("https://www.bigo.tv/live?h=987654&source=test", "https://www.bigo.tv/987654")]
    public void ParseUrl_WithBigoLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://www.xiaohongshu.com/user/profile/abc123?xsec_token=test", "https://www.xiaohongshu.com/user/profile/abc123")]
    [InlineData("https://www.xiaohongshu.com/live?host_id=host123&source=test", "https://www.xiaohongshu.com/user/profile/host123")]
    public void ParseUrl_WithXiaohongshuLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
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
    [InlineData("https://live.kuaishou.com/u/example", "Kuaishou")]
    [InlineData("https://www.bigo.tv/cn/123456", "Bigo")]
    [InlineData("https://www.xiaohongshu.com/user/profile/abc123", "Xiaohongshu")]
    [InlineData("https://example.test/live.m3u8", "Direct")]
    [InlineData("https://example.test/page", "")]
    public void GetPlatformName_DetectsSupportedPlatform(string input, string expected)
    {
        Assert.Equal(expected, Spider.GetPlatformName(input));
    }

    [Fact]
    public void SupportedPlatformNames_IncludesRegisteredSpiders()
    {
        Assert.Contains("Douyin", Spider.SupportedPlatformNames);
        Assert.Contains("TikTok", Spider.SupportedPlatformNames);
        Assert.Contains("Bilibili", Spider.SupportedPlatformNames);
        Assert.Contains("Kuaishou", Spider.SupportedPlatformNames);
        Assert.Contains("Bigo", Spider.SupportedPlatformNames);
        Assert.Contains("Xiaohongshu", Spider.SupportedPlatformNames);
        Assert.Contains("Direct", Spider.SupportedPlatformNames);
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

    [Fact]
    public void KuaishouExtractInitialState_UsesHighestBitrateFlv()
    {
        KuaishouSpiderResult result = new()
        {
            RoomUrl = "https://live.kuaishou.com/u/example",
            PlatformName = "Kuaishou",
        };

        KuaishouSpider.ExtractInitialState(
            """
            {
              "route": {
                "payload": {
                  "author": { "name": "anchor" },
                  "liveStream": {
                    "playUrls": {
                      "h264": {
                        "adaptationSet": {
                          "representation": [
                            { "url": "https://example.test/low.flv", "bitrate": 600 },
                            { "url": "https://example.test/high.flv", "bitrate": 2000 }
                          ]
                        }
                      }
                    }
                  }
                }
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/high.flv", result.FlvUrl);
    }

    [Fact]
    public void BigoExtractStudioInfo_MapsLiveHlsData()
    {
        BigoSpiderResult result = new()
        {
            RoomUrl = "https://www.bigo.tv/123456",
            PlatformName = "Bigo",
        };

        BigoSpider.ExtractStudioInfo(
            """
            {
              "code": 0,
              "data": {
                "nick_name": "anchor",
                "avatar": "https://example.test/avatar.png",
                "alive": 1,
                "hls_src": "https://example.test/live.m3u8"
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
    }

    [Fact]
    public void XiaohongshuExtractInitialState_MapsLiveStreamData()
    {
        XiaohongshuSpiderResult result = new()
        {
            RoomUrl = "https://www.xiaohongshu.com/user/profile/abc123",
            PlatformName = "Xiaohongshu",
        };

        XiaohongshuSpider.ExtractInitialState(
            """
            {
              "liveStream": {
                "liveStatus": "success",
                "roomData": {
                  "roomInfo": {
                    "roomTitle": "Live title",
                    "deeplink": "xhsdiscover://live?host_nickname=anchor&flvUrl=http%3A%2F%2Flive-source-play.xhscdn.com%2Flive%2Froom123.flv"
                  }
                }
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("http://live-source-play.xhscdn.com/live/room123.flv", result.FlvUrl);
        Assert.Equal("http://live-source-play.xhscdn.com/live/room123.m3u8", result.HlsUrl);
    }
}
