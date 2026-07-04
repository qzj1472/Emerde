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
    [InlineData("https://www.huya.com/52333?from=test", "https://www.huya.com/52333")]
    [InlineData("https://huya.com/example_room?from=test", "https://www.huya.com/example_room")]
    public void ParseUrl_WithHuyaLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseUrl_WithBaiduLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://live.baidu.com/m/media/pclive/pchome/live.html?room_id=9175031377&tab_category=test");

        Assert.Equal("https://live.baidu.com/m/media/pclive/pchome/live.html?room_id=9175031377", result);
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
    [InlineData("https://17.live/en/live/6302408?from=test", "https://17.live/en/live/6302408")]
    [InlineData("https://www.17.live/en-US/room/3349463?from=test", "https://17.live/en/live/3349463")]
    public void ParseUrl_WithSeventeenLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://chzzk.naver.com/live/458f6ec20b034f49e0fc6d03921646d2?from=test", "https://chzzk.naver.com/live/458f6ec20b034f49e0fc6d03921646d2")]
    [InlineData("https://m.chzzk.naver.com/live/458f6ec20b034f49e0fc6d03921646d2", "https://chzzk.naver.com/live/458f6ec20b034f49e0fc6d03921646d2")]
    public void ParseUrl_WithChzzkLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseUrl_WithMaoerFmLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://fm.missevan.com/live/868895007?from=test");

        Assert.Equal("https://fm.missevan.com/live/868895007", result);
    }

    [Fact]
    public void ParseUrl_WithPicartoLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://picarto.tv/cuteavalanche?from=test");

        Assert.Equal("https://www.picarto.tv/cuteavalanche", result);
    }

    [Fact]
    public void ParseUrl_WithLianjieLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://show.lailianjie.com/10000258?from=test");

        Assert.Equal("https://show.lailianjie.com/10000258", result);
    }

    [Fact]
    public void ParseUrl_WithLangLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://www.lang.live/en-US/room/3349463?from=test");

        Assert.Equal("https://www.lang.live/en-US/room/3349463", result);
    }

    [Fact]
    public void ParseUrl_WithSixRoomsLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://v.6.cn/634435?from=test");

        Assert.Equal("https://v.6.cn/634435", result);
    }

    [Fact]
    public void ParseUrl_WithVvXqiuLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://h5webcdn-pro.vvxqiu.com/activity/videoShare/videoShare.html?h5Server=https://h5p.vvxqiu.com&roomId=LP115924473&platformId=vvstar");

        Assert.Equal("https://h5webcdn-pro.vvxqiu.com/activity/videoShare/videoShare.html?roomId=LP115924473", result);
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
    [InlineData("https://fanxing.kugou.com/123456?from=test", "https://fanxing.kugou.com/123456")]
    [InlineData("https://fanxing2.kugou.com/index.html?roomId=987654&source=test", "https://fanxing.kugou.com/987654")]
    public void ParseUrl_WithKugouLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseUrl_WithYingkeLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://www.inke.cn/liveroom.html?id=live123&uid=user456&from=test");

        Assert.Equal("https://www.inke.cn/liveroom.html?uid=user456&id=live123", result);
    }

    [Theory]
    [InlineData("https://www.showroom-live.com/room/profile?room_id=123456&from=test", "https://www.showroom-live.com/room/profile?room_id=123456")]
    [InlineData("https://www.showroom-live.com/example_room?source=test", "https://www.showroom-live.com/example_room")]
    public void ParseUrl_WithShowRoomLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://live.acfun.cn/live/17912421?from=test", "https://live.acfun.cn/live/17912421")]
    [InlineData("https://m.acfun.cn/live/17912421", "https://live.acfun.cn/live/17912421")]
    public void ParseUrl_WithAcFunLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://www.yy.com/123456?from=test", "https://www.yy.com/123456")]
    [InlineData("https://www.yy.com/123456/7890?from=test", "https://www.yy.com/123456/7890")]
    public void ParseUrl_WithYyLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://cc.163.com/123456?from=test", "https://cc.163.com/123456")]
    [InlineData("https://cc.163.com/123456/7890?from=test", "https://cc.163.com/123456/7890")]
    public void ParseUrl_WithNeteaseCcLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://qiandurebo.com/123456?from=test", "https://qiandurebo.com/123456")]
    [InlineData("https://www.qiandurebo.com/web/123456?from=test", "https://qiandurebo.com/web/123456")]
    public void ParseUrl_WithQianduReboLiveUrl_NormalizesRoomUrl(string input, string expected)
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
    [InlineData("https://www.huya.com/52333", "Huya")]
    [InlineData("https://live.baidu.com/m/media/pclive/pchome/live.html?room_id=9175031377", "Baidu")]
    [InlineData("https://www.bigo.tv/cn/123456", "Bigo")]
    [InlineData("https://17.live/en/live/6302408", "17Live")]
    [InlineData("https://chzzk.naver.com/live/458f6ec20b034f49e0fc6d03921646d2", "CHZZK")]
    [InlineData("https://fm.missevan.com/live/868895007", "MaoerFM")]
    [InlineData("https://www.picarto.tv/cuteavalanche", "Picarto")]
    [InlineData("https://show.lailianjie.com/10000258", "Lianjie")]
    [InlineData("https://www.lang.live/en-US/room/3349463", "LangLive")]
    [InlineData("https://v.6.cn/634435", "6Rooms")]
    [InlineData("https://h5webcdn-pro.vvxqiu.com/activity/videoShare/videoShare.html?roomId=LP115924473", "VVXqiu")]
    [InlineData("https://www.xiaohongshu.com/user/profile/abc123", "Xiaohongshu")]
    [InlineData("https://fanxing.kugou.com/123456", "Kugou")]
    [InlineData("https://www.inke.cn/liveroom.html?uid=user456&id=live123", "Yingke")]
    [InlineData("https://www.showroom-live.com/example_room", "ShowRoom")]
    [InlineData("https://live.acfun.cn/live/17912421", "AcFun")]
    [InlineData("https://www.yy.com/123456", "YY")]
    [InlineData("https://cc.163.com/123456", "NeteaseCC")]
    [InlineData("https://qiandurebo.com/123456", "QianduRebo")]
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
        Assert.Contains("Huya", Spider.SupportedPlatformNames);
        Assert.Contains("Baidu", Spider.SupportedPlatformNames);
        Assert.Contains("Bigo", Spider.SupportedPlatformNames);
        Assert.Contains("17Live", Spider.SupportedPlatformNames);
        Assert.Contains("CHZZK", Spider.SupportedPlatformNames);
        Assert.Contains("MaoerFM", Spider.SupportedPlatformNames);
        Assert.Contains("Picarto", Spider.SupportedPlatformNames);
        Assert.Contains("Lianjie", Spider.SupportedPlatformNames);
        Assert.Contains("LangLive", Spider.SupportedPlatformNames);
        Assert.Contains("6Rooms", Spider.SupportedPlatformNames);
        Assert.Contains("VVXqiu", Spider.SupportedPlatformNames);
        Assert.Contains("Xiaohongshu", Spider.SupportedPlatformNames);
        Assert.Contains("Kugou", Spider.SupportedPlatformNames);
        Assert.Contains("Yingke", Spider.SupportedPlatformNames);
        Assert.Contains("ShowRoom", Spider.SupportedPlatformNames);
        Assert.Contains("AcFun", Spider.SupportedPlatformNames);
        Assert.Contains("YY", Spider.SupportedPlatformNames);
        Assert.Contains("NeteaseCC", Spider.SupportedPlatformNames);
        Assert.Contains("QianduRebo", Spider.SupportedPlatformNames);
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
    public void HuyaExtractors_MapProfileRoomAndStreamData()
    {
        HuyaSpiderResult result = new()
        {
            RoomUrl = "https://www.huya.com/52333",
            PlatformName = "Huya",
        };

        string? roomId = HuyaSpider.ExtractProfileRoomId("""window.HNF_GLOBAL_INIT = {"ProfileRoom":52333,"sPrivateHost":"host"};""");
        HuyaSpider.ExtractProfileRoom(
            """
            {
              "data": {
                "profileInfo": {
                  "nick": "anchor",
                  "avatar180": "https://example.test/avatar.png"
                },
                "realLiveStatus": "ON",
                "liveData": {
                  "introduction": "Live title"
                },
                "stream": {
                  "baseSteamInfoList": [
                    {
                      "sCdnType": "AL",
                      "sStreamName": "room-low",
                      "sFlvUrl": "http://example.test/live",
                      "sFlvAntiCode": "wsSecret=low&ctype=tars_mp&fs=bhct",
                      "sHlsUrl": "http://example.test/hls",
                      "sHlsAntiCode": "wsSecret=low"
                    },
                    {
                      "sCdnType": "TX",
                      "sStreamName": "room-high",
                      "sFlvUrl": "http://example.test/live",
                      "sFlvAntiCode": "wsSecret=high&ctype=tars_mp&fs=bhct",
                      "sHlsUrl": "http://example.test/hls",
                      "sHlsAntiCode": "wsSecret=high"
                    }
                  ]
                }
              }
            }
            """,
            result);

        Assert.Equal("52333", roomId);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live/room-high.flv?wsSecret=high&ctype=huya_webh5&fs=bgct", result.FlvUrl);
        Assert.Equal("https://example.test/hls/room-high.m3u8?wsSecret=high", result.HlsUrl);
    }

    [Fact]
    public void BaiduExtractRoomInfo_MapsHostStatusAndStreamData()
    {
        BaiduSpiderResult result = new()
        {
            RoomUrl = "https://live.baidu.com/m/media/pclive/pchome/live.html?room_id=9175031377",
            PlatformName = "Baidu",
        };

        BaiduSpider.ExtractRoomInfo(
            """
            {
              "data": {
                "9175031377": {
                  "host": {
                    "name": "anchor",
                    "avatar": "https://example.test/avatar.png"
                  },
                  "status": "0",
                  "video": {
                    "title": "Live title",
                    "url_clarity_list": [
                      {
                        "urls": {
                          "flv": "https://example.test/live/stream123.flv?token=abc"
                        }
                      }
                    ]
                  }
                }
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live/stream123.flv?token=abc", result.FlvUrl);
        Assert.Equal("https://hls.liveshow.bdstatic.com/live/stream123.m3u8", result.HlsUrl);
    }

    [Fact]
    public void SeventeenLiveExtractors_MapRoomAndStreamData()
    {
        SeventeenLiveSpiderResult result = new()
        {
            RoomUrl = "https://17.live/en/live/6302408",
            PlatformName = "17Live",
        };

        SeventeenLiveSpider.ExtractRoomInfo(
            """
            {
              "displayName": "anchor",
              "profilePic": "https://example.test/avatar.png"
            }
            """,
            result);
        SeventeenLiveSpider.ExtractAliveInfo(
            """
            {
              "status": 2,
              "pullURLsInfo": {
                "rtmpURLs": [
                  {
                    "urlHighQuality": "rtmp://example.test/live/high"
                  }
                ],
                "hlsURLs": [
                  {
                    "urlHighQuality": "https://example.test/live/high.m3u8"
                  }
                ]
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("rtmp://example.test/live/high", result.FlvUrl);
        Assert.Equal("https://example.test/live/high.m3u8", result.HlsUrl);
    }

    [Fact]
    public void ChzzkExtractors_MapLiveDetailAndPlaybackData()
    {
        ChzzkSpiderResult result = new()
        {
            RoomUrl = "https://chzzk.naver.com/live/458f6ec20b034f49e0fc6d03921646d2",
            PlatformName = "CHZZK",
        };

        ChzzkSpider.ExtractLiveDetail(
            """
            {
              "content": {
                "channel": {
                  "channelName": "anchor",
                  "channelImageUrl": "https://example.test/avatar.png"
                },
                "status": "OPEN",
                "livePlaybackJson": "{\"media\":[{\"path\":\"https://example.test/live/master.m3u8\"}]}"
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live/master.m3u8", result.HlsUrl);
    }

    [Fact]
    public void MaoerFmExtractRoomInfo_MapsLiveStreamData()
    {
        MaoerFmSpiderResult result = new()
        {
            RoomUrl = "https://fm.missevan.com/live/868895007",
            PlatformName = "MaoerFM",
        };

        MaoerFmSpider.ExtractRoomInfo(
            """
            {
              "info": {
                "creator": {
                  "username": "anchor",
                  "icon": "https://example.test/avatar.png"
                },
                "room": {
                  "status": {
                    "broadcasting": true
                  },
                  "channel": {
                    "hls_pull_url": "https://example.test/live.m3u8",
                    "flv_pull_url": "https://example.test/live.flv"
                  }
                }
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }

    [Fact]
    public void PicartoExtractChannelDetail_MapsLiveHlsData()
    {
        PicartoSpiderResult result = new()
        {
            RoomUrl = "https://www.picarto.tv/cuteavalanche",
            PlatformName = "Picarto",
        };

        PicartoSpider.ExtractChannelDetail(
            """
            {
              "channel": {
                "name": "cuteavalanche",
                "avatar": "https://example.test/avatar.png",
                "online": true,
                "title": "Live title"
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("cuteavalanche", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://1-edge1-us-newyork.picarto.tv/stream/hls/golive+cuteavalanche/index.m3u8", result.HlsUrl);
    }

    [Fact]
    public void LianjieExtractRoomInfo_MapsWebRtcStreamUrls()
    {
        LianjieSpiderResult result = new()
        {
            RoomUrl = "https://show.lailianjie.com/10000258",
            PlatformName = "Lianjie",
        };

        LianjieSpider.ExtractRoomInfo(
            """
            {
              "data": {
                "nickname": "anchor",
                "isonline": 1,
                "videoUrl": "webrtc://example.test/live/stream?token=abc"
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/live/stream.flv?token=abc", result.FlvUrl);
        Assert.Equal("https://example.test/live/stream.m3u8?token=abc", result.HlsUrl);
    }

    [Fact]
    public void LangLiveExtractLiveInfo_MapsLiveStreamData()
    {
        LangLiveSpiderResult result = new()
        {
            RoomUrl = "https://www.lang.live/en-US/room/3349463",
            PlatformName = "LangLive",
        };

        LangLiveSpider.ExtractLiveInfo(
            """
            {
              "data": {
                "live_info": {
                  "nickname": "anchor",
                  "avatar": "https://example.test/avatar.png",
                  "live_status": 1,
                  "liveurl": "https://example.test/live.flv",
                  "liveurl_hls": "https://example.test/live.m3u8"
                }
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
    }

    [Fact]
    public void SixRoomsExtractors_MapRoomIdAndFlvData()
    {
        SixRoomsSpiderResult result = new()
        {
            RoomUrl = "https://v.6.cn/634435",
            PlatformName = "6Rooms",
        };

        string? roomId = SixRoomsSpider.ExtractRoomId(
            """
            rid: '123456',
                roomid
            """);
        SixRoomsSpider.ExtractMobileRoom(
            """
            {
              "content": {
                "liveinfo": {
                  "flvtitle": "live_stream"
                },
                "roominfo": {
                  "alias": "anchor"
                }
              }
            }
            """,
            result);

        Assert.Equal("123456", roomId);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://wlive.6rooms.com/httpflv/live_stream.flv", result.FlvUrl);
    }

    [Fact]
    public void VvXqiuExtractBanner_MapsAnchorName()
    {
        VvXqiuSpiderResult result = new()
        {
            RoomUrl = "https://h5webcdn-pro.vvxqiu.com/activity/videoShare/videoShare.html?roomId=LP115924473",
            PlatformName = "VVXqiu",
        };

        VvXqiuSpider.ExtractBanner(
            """
            {
              "data": {
                "anchorName": "anchor",
                "anchorAvatar": "https://example.test/avatar.png"
              }
            }
            """,
            result);

        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
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

    [Fact]
    public void KugouExtractors_MapRoomInfoAndStreamUrl()
    {
        KugouSpiderResult result = new()
        {
            RoomUrl = "https://fanxing.kugou.com/123456",
            PlatformName = "Kugou",
        };

        KugouSpider.ExtractRoomInfo(
            """
            {
              "data": {
                "normalRoomInfo": { "nickName": "anchor" },
                "liveType": 1
              }
            }
            """,
            result);

        KugouSpider.ExtractStreamUrl(
            """
            {
              "data": {
                "lines": [
                  { "streamProfiles": [ { "httpsFlv": [ "https://example.test/low.flv" ] } ] },
                  { "streamProfiles": [ { "httpsFlv": [ "https://example.test/high.flv" ] } ] }
                ]
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/high.flv", result.FlvUrl);
    }

    [Fact]
    public void YingkeExtractShareData_MapsLiveStreamUrls()
    {
        YingkeSpiderResult result = new()
        {
            RoomUrl = "https://www.inke.cn/liveroom.html?uid=user456&id=live123",
            PlatformName = "Yingke",
        };

        YingkeSpider.ExtractShareData(
            """
            {
              "data": {
                "media_info": { "nick": "anchor" },
                "status": 1,
                "live_addr": [
                  {
                    "hls_stream_addr": "https://example.test/live.m3u8",
                    "stream_addr": "https://example.test/live.flv"
                  }
                ]
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }

    [Fact]
    public void ShowRoomExtractors_MapRoomAndHlsData()
    {
        ShowRoomSpiderResult result = new()
        {
            RoomUrl = "https://www.showroom-live.com/room/profile?room_id=123456",
            PlatformName = "ShowRoom",
        };

        string? roomId = ShowRoomSpider.ExtractRoomIdFromHtml("""<a href="/room/profile?room_id=123456">profile</a>""");
        ShowRoomSpider.ExtractLiveInfo(
            """
            {
              "room_name": "anchor",
              "live_status": 2
            }
            """,
            result);
        ShowRoomSpider.ExtractStreamingUrl(
            """
            {
              "streaming_url_list": [
                { "type": "hls", "url": "https://example.test/low.m3u8" },
                { "type": "hls_all", "url": "https://example.test/all.m3u8" }
              ]
            }
            """,
            result);

        Assert.Equal("123456", roomId);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/all.m3u8", result.HlsUrl);
    }

    [Fact]
    public void AcFunExtractors_MapUserInfoVisitorAndPlayData()
    {
        AcFunSpiderResult result = new()
        {
            RoomUrl = "https://live.acfun.cn/live/17912421",
            PlatformName = "AcFun",
        };

        AcFunSpider.ExtractUserInfo("""{"profile":{"name":"anchor","liveId":"live123"}}""", result);
        AcFunVisitorSign? sign = AcFunSpider.ExtractVisitorSign("""{"userId":"visitor","acfun.api.visitor_st":"token"}""", "web_test");
        AcFunSpider.ExtractStartPlay(
            """
            {
              "data": {
                "videoPlayRes": "{\"liveAdaptiveManifest\":[{\"adaptationSet\":{\"representation\":[{\"url\":\"https://example.test/low.flv\",\"bitrate\":600},{\"url\":\"https://example.test/high.flv\",\"bitrate\":2000}]}}]}"
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.NotNull(sign);
        Assert.Equal("visitor", sign.UserId);
        Assert.Equal("web_test", sign.Did);
        Assert.Equal("token", sign.VisitorSt);
        Assert.Equal("https://example.test/high.flv", result.FlvUrl);
    }

    [Fact]
    public void YyExtractors_MapPageAndStreamData()
    {
        YySpiderResult result = new()
        {
            RoomUrl = "https://www.yy.com/123456",
            PlatformName = "YY",
        };

        YySpider.ExtractPageData(
            """
            nick: "anchor",
                logo
            sid : "channel123",
                ssid
            """,
            result);
        YySpider.ExtractStreamInfo(
            """
            {
              "avp_info_res": {
                "stream_line_addr": {
                  "line1": {
                    "cdn_info": {
                      "url": "https://example.test/live.flv"
                    }
                  }
                }
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("channel123", result.ChannelId);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }

    [Fact]
    public void NeteaseCcExtractNextData_MapsLiveStreamData()
    {
        NeteaseCcSpiderResult result = new()
        {
            RoomUrl = "https://cc.163.com/123456",
            PlatformName = "NeteaseCC",
        };

        NeteaseCcSpider.ExtractNextData(
            """
            {
              "props": {
                "pageProps": {
                  "roomInfoInitData": {
                    "nickname": "fallback",
                    "live": {
                      "nickname": "anchor",
                      "status": 1,
                      "sharefile": "https://example.test/live.m3u8",
                      "quickplay": {
                        "resolution": {
                          "high": {
                            "cdn": {
                              "main": "https://example.test/high.flv"
                            }
                          },
                          "standard": {
                            "cdn": {
                              "main": "https://example.test/standard.flv"
                            }
                          }
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
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
        Assert.Equal("https://example.test/high.flv", result.FlvUrl);
    }

    [Fact]
    public void QianduReboExtractData_MapsNicknameAndFlvUrl()
    {
        QianduReboSpiderResult result = new()
        {
            RoomUrl = "https://qiandurebo.com/123456",
            PlatformName = "QianduRebo",
        };

        QianduReboSpider.ExtractData(
            """
            <script>
            var user = {
              "zb_nickname": "anchor",
              "play_url": "https:\/\/example.test\/live.flv",
            }
                user.play_url = user.play_url
            </script>
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }
}
