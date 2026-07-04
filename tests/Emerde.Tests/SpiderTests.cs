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

    [Fact]
    public void ParseUrl_WithBluedLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://app.blued.cn/live?id=Mp6G2R&from=test");

        Assert.Equal("https://app.blued.cn/live?id=Mp6G2R", result);
    }

    [Fact]
    public void ParseUrl_WithLiuxingLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://wap.7u66.com/100960?promoters=0");

        Assert.Equal("https://www.7u66.com/100960", result);
    }

    [Fact]
    public void ParseUrl_WithChangliaoLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://www.tlclw.com/106188?from=test");

        Assert.Equal("https://live.tlclw.com/106188", result);
    }

    [Fact]
    public void ParseUrl_WithYinboLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://wap.ybw1666.com/800002949?from=test");

        Assert.Equal("https://live.ybw1666.com/800002949", result);
    }

    [Theory]
    [InlineData("https://www.zhihu.com/people/ac3a467005c5d20381a82230101308e9?from=test", "https://www.zhihu.com/people/ac3a467005c5d20381a82230101308e9")]
    [InlineData("https://www.zhihu.com/theater/123456?from=test", "https://www.zhihu.com/theater/123456")]
    public void ParseUrl_WithZhihuLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseUrl_WithPpLiveLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://m.pp.weimipopo.com/live/preview.html?uid=91648673&anchorUid=91625862&app=plpl");

        Assert.Equal("https://m.pp.weimipopo.com/live/preview.html?anchorUid=91625862", result);
    }

    [Fact]
    public void ParseUrl_WithCatShowLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://h.catshow168.com/live/preview.html?uid=19066357&anchorUid=18895331");

        Assert.Equal("https://h.catshow168.com/live/preview.html?anchorUid=18895331", result);
    }

    [Fact]
    public void ParseUrl_WithLaixiuLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://www.imkktv.com/h5/share/video.html?uid=1845195&roomId=1710496");

        Assert.Equal("https://www.imkktv.com/h5/share/video.html?roomId=1710496", result);
    }

    [Theory]
    [InlineData("https://3.cn/28MLBy-E?from=test", "https://3.cn/28MLBy-E")]
    [InlineData("https://m.jd.com/live/123456?authorId=789", "https://m.jd.com/live/123456?authorId=789")]
    public void ParseUrl_WithJdLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseUrl_WithPandaLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://www.pandalive.co.kr/live/play/bara0109?pwd=1234&from=test");

        Assert.Equal("https://www.pandalive.co.kr/live/play/bara0109?pwd=1234", result);
    }

    [Fact]
    public void ParseUrl_WithWinkTvLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://www.winktv.co.kr/live/play/anjer1004?pwd=1234&from=test");

        Assert.Equal("https://www.winktv.co.kr/live/play/anjer1004?pwd=1234", result);
    }

    [Fact]
    public void ParseUrl_WithTwitchLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://twitch.tv/example?from=test");

        Assert.Equal("https://www.twitch.tv/example", result);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123&feature=share", "https://www.youtube.com/watch?v=abc123")]
    [InlineData("https://youtu.be/abc123?si=test", "https://youtu.be/abc123")]
    [InlineData("https://www.youtube.com/live/abc123?feature=share", "https://www.youtube.com/live/abc123")]
    public void ParseUrl_WithYouTubeLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://live.shopee.sg/share?from=live&session=802458&share_user_id=", "https://live.shopee.sg/share?from=live&session=802458&share_user_id=")]
    [InlineData("https://shp.ee/abc123?from=test", "https://shp.ee/abc123?from=test")]
    public void ParseUrl_WithShopeeLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseUrl_WithTwitCastingLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://twitcasting.tv/example/show?from=test");

        Assert.Equal("https://twitcasting.tv/example", result);
    }

    [Fact]
    public void ParseUrl_WithFaceitLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://www.faceit.com/zh/players/qpjzz/stream?from=test");

        Assert.Equal("https://www.faceit.com/players/qpjzz/stream", result);
    }

    [Theory]
    [InlineData("https://weibo.com/l/wblive/p/show/1022:2321325026370190442592?from=test", "https://weibo.com/l/wblive/p/show/1022:2321325026370190442592")]
    [InlineData("https://weibo.com/u/5885340893?from=test", "https://weibo.com/u/5885340893")]
    public void ParseUrl_WithWeiboLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://www.huajiao.com/l/345096174?from=test", "https://www.huajiao.com/l/345096174")]
    [InlineData("https://www.huajiao.com/user/123456?from=test", "https://www.huajiao.com/user/123456")]
    public void ParseUrl_WithHuajiaoLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://play.sooplive.co.kr/sw7love?from=test", "https://play.sooplive.co.kr/sw7love")]
    [InlineData("https://www.sooplive.com/sw7love?from=test", "https://www.sooplive.com/sw7love")]
    public void ParseUrl_WithSoopLiveUrl_NormalizesRoomUrl(string input, string expected)
    {
        string? result = Spider.ParseUrl(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseUrl_WithFlexTvLiveUrl_NormalizesRoomUrl()
    {
        string? result = Spider.ParseUrl("https://www.ttinglive.com/channels/593127/live?from=test");

        Assert.Equal("https://www.flextv.co.kr/channels/593127/live", result);
    }

    [Theory]
    [InlineData("https://www.popkontv.com/live/view?castId=wjfal007&partnerCode=P-00117", "https://www.popkontv.com/live/view?castId=wjfal007&partnerCode=P-00117")]
    [InlineData("https://www.popkontv.com/channel/notices?mcid=wjfal007&mcPartnerCode=P-00117", "https://www.popkontv.com/live/view?castId=wjfal007&partnerCode=P-00117")]
    public void ParseUrl_WithPopkonTvLiveUrl_NormalizesRoomUrl(string input, string expected)
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
    [InlineData("https://app.blued.cn/live?id=Mp6G2R", "Blued")]
    [InlineData("https://www.7u66.com/100960", "Liuxing")]
    [InlineData("https://live.tlclw.com/106188", "Changliao")]
    [InlineData("https://live.ybw1666.com/800002949", "Yinbo")]
    [InlineData("https://www.zhihu.com/people/ac3a467005c5d20381a82230101308e9", "Zhihu")]
    [InlineData("https://m.pp.weimipopo.com/live/preview.html?anchorUid=91625862", "PPLive")]
    [InlineData("https://h.catshow168.com/live/preview.html?anchorUid=18895331", "CatShow")]
    [InlineData("https://www.imkktv.com/h5/share/video.html?roomId=1710496", "Laixiu")]
    [InlineData("https://3.cn/28MLBy-E", "JD")]
    [InlineData("https://www.pandalive.co.kr/live/play/bara0109", "PandaTV")]
    [InlineData("https://www.winktv.co.kr/live/play/anjer1004", "WinkTV")]
    [InlineData("https://www.twitch.tv/example", "Twitch")]
    [InlineData("https://www.youtube.com/watch?v=abc123", "YouTube")]
    [InlineData("https://live.shopee.sg/share?from=live&session=802458", "Shopee")]
    [InlineData("https://twitcasting.tv/example", "TwitCasting")]
    [InlineData("https://www.faceit.com/zh/players/qpjzz/stream", "Faceit")]
    [InlineData("https://weibo.com/l/wblive/p/show/1022:2321325026370190442592", "Weibo")]
    [InlineData("https://www.huajiao.com/l/345096174", "Huajiao")]
    [InlineData("https://play.sooplive.co.kr/sw7love", "SOOP")]
    [InlineData("https://www.flextv.co.kr/channels/593127/live", "FlexTV")]
    [InlineData("https://www.popkontv.com/live/view?castId=wjfal007&partnerCode=P-00117", "PopkonTV")]
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
        Assert.Contains("Blued", Spider.SupportedPlatformNames);
        Assert.Contains("Liuxing", Spider.SupportedPlatformNames);
        Assert.Contains("Changliao", Spider.SupportedPlatformNames);
        Assert.Contains("Yinbo", Spider.SupportedPlatformNames);
        Assert.Contains("Zhihu", Spider.SupportedPlatformNames);
        Assert.Contains("PPLive", Spider.SupportedPlatformNames);
        Assert.Contains("CatShow", Spider.SupportedPlatformNames);
        Assert.Contains("Laixiu", Spider.SupportedPlatformNames);
        Assert.Contains("JD", Spider.SupportedPlatformNames);
        Assert.Contains("PandaTV", Spider.SupportedPlatformNames);
        Assert.Contains("WinkTV", Spider.SupportedPlatformNames);
        Assert.Contains("Twitch", Spider.SupportedPlatformNames);
        Assert.Contains("YouTube", Spider.SupportedPlatformNames);
        Assert.Contains("Shopee", Spider.SupportedPlatformNames);
        Assert.Contains("TwitCasting", Spider.SupportedPlatformNames);
        Assert.Contains("Faceit", Spider.SupportedPlatformNames);
        Assert.Contains("Weibo", Spider.SupportedPlatformNames);
        Assert.Contains("Huajiao", Spider.SupportedPlatformNames);
        Assert.Contains("SOOP", Spider.SupportedPlatformNames);
        Assert.Contains("FlexTV", Spider.SupportedPlatformNames);
        Assert.Contains("PopkonTV", Spider.SupportedPlatformNames);
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
    public void BluedExtractPageData_MapsLiveHlsData()
    {
        BluedSpiderResult result = new()
        {
            RoomUrl = "https://app.blued.cn/live?id=Mp6G2R",
            PlatformName = "Blued",
        };

        string encoded = Uri.EscapeDataString(
            """
            {
              "userInfo": {
                "name": "anchor",
                "avatar": "https://example.test/avatar.png",
                "onLive": true
              },
              "liveInfo": {
                "liveUrl": "https://example.test/live.m3u8"
              }
            }
            """);

        BluedSpider.ExtractPageData($"decodeURIComponent(\"{encoded}\")),window.Promise", result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
    }

    [Fact]
    public void LiuxingExtractRoomInfo_MapsFlvData()
    {
        LiuxingSpiderResult result = new()
        {
            RoomUrl = "https://www.7u66.com/100960",
            PlatformName = "Liuxing",
        };

        LiuxingSpider.ExtractRoomInfo(
            """
            {
              "data": {
                "roomInfo": {
                  "nickname": "anchor",
                  "live_stat": 1,
                  "idx": "123",
                  "liveId1": "456"
                }
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://txpull1.5see.com/live/123/456.flv", result.FlvUrl);
    }

    [Fact]
    public void ChangliaoExtractors_MapLiveStreamData()
    {
        ChangliaoSpiderResult result = new()
        {
            RoomUrl = "https://live.tlclw.com/106188",
            PlatformName = "Changliao",
        };

        ChangliaoSpider.ExtractRoomInfo(
            """
            {
              "data": {
                "roomInfo": {
                  "nickname": "anchor",
                  "live_stat": 1,
                  "liveID": "live123"
                }
              }
            }
            """,
            result);
        (string? flvDomain, string? hlsDomain) = ChangliaoSpider.ExtractLiveDomain(
            """
            var config = {
              "domainpullstream_flv": "https://flv.example.test/live",
              "domainpullstream_hls": "https://hls.example.test/live"
            };
            config.webskins
            """);
        ChangliaoSpider.ApplyLiveDomain(result, flvDomain, hlsDomain);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://flv.example.test/live/live123.flv", result.FlvUrl);
        Assert.Equal("https://hls.example.test/live/live123.m3u8", result.HlsUrl);
    }

    [Fact]
    public void YinboExtractors_MapLiveStreamData()
    {
        YinboSpiderResult result = new()
        {
            RoomUrl = "https://live.ybw1666.com/800002949",
            PlatformName = "Yinbo",
        };

        YinboSpider.ExtractRoomInfo(
            """
            {
              "data": {
                "roomInfo": {
                  "nickname": "anchor",
                  "live_stat": 1,
                  "liveID": "live123"
                }
              }
            }
            """,
            result);
        (string? flvDomain, string? hlsDomain) = YinboSpider.ExtractLiveDomain(
            """
            var config = {
              "domainpullstream_flv": "https://flv.example.test/live",
              "domainpullstream_hls": "https://hls.example.test/live"
            };
            config.webskins
            """);
        YinboSpider.ApplyLiveDomain(result, flvDomain, hlsDomain);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://flv.example.test/live/live123.flv", result.FlvUrl);
        Assert.Equal("https://hls.example.test/live/live123.m3u8", result.HlsUrl);
    }

    [Fact]
    public void ZhihuExtractors_MapProfileAndLiveStreamData()
    {
        ZhihuSpiderResult result = new()
        {
            RoomUrl = "https://www.zhihu.com/theater/web123",
            PlatformName = "Zhihu",
        };

        string? livePageUrl = ZhihuSpider.ExtractLivePageUrl("""{"drama":{"living_theater":{"theater_url":"https://www.zhihu.com/theater/web123"}}}""");
        ZhihuSpider.ExtractInitialData(
            """
            <script id="js-initialData" type="text/json">
            {
              "initialState": {
                "theater": {
                  "theaters": {
                    "web123": {
                      "actor": {
                        "name": "anchor",
                        "avatarUrl": "https://example.test/avatar.png"
                      },
                      "drama": {
                        "status": 1,
                        "playInfo": {
                          "hlsUrl": "https://example.test/live.m3u8",
                          "playUrl": "https://example.test/live.flv"
                        }
                      }
                    }
                  }
                }
              }
            }
            </script>
            """,
            "web123",
            result);

        Assert.Equal("https://www.zhihu.com/theater/web123", livePageUrl);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }

    [Fact]
    public void PpLiveExtractPreview_MapsHlsData()
    {
        PpLiveSpiderResult result = new()
        {
            RoomUrl = "https://m.pp.weimipopo.com/live/preview.html?anchorUid=91625862",
            PlatformName = "PPLive",
        };

        PpLiveSpider.ExtractPreview(
            """
            {
              "data": {
                "name": "anchor",
                "avatar": "https://example.test/avatar.png",
                "living": true,
                "pullUrl": "https://example.test/live.m3u8"
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
    public void CatShowExtractPreview_MapsHlsData()
    {
        CatShowSpiderResult result = new()
        {
            RoomUrl = "https://h.catshow168.com/live/preview.html?anchorUid=18895331",
            PlatformName = "CatShow",
        };

        CatShowSpider.ExtractPreview(
            """
            {
              "data": {
                "name": "anchor",
                "avatar": "https://example.test/avatar.png",
                "living": true,
                "pullUrl": "https://example.test/live.m3u8"
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
    public void LaixiuExtractShareLiveVideo_MapsFlvData()
    {
        LaixiuSpiderResult result = new()
        {
            RoomUrl = "https://www.imkktv.com/h5/share/video.html?roomId=1710496",
            PlatformName = "Laixiu",
        };

        LaixiuSpider.ExtractShareLiveVideo(
            """
            {
              "data": {
                "nickname": "anchor",
                "avatar": "https://example.test/avatar.png",
                "playStatus": 0,
                "playUrl": "https://example.test/live.flv"
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }

    [Fact]
    public void JdExtractors_MapTalentAndPlayData()
    {
        JdSpiderResult result = new()
        {
            RoomUrl = "https://3.cn/28MLBy-E",
            PlatformName = "JD",
        };

        string? liveId = JdSpider.ExtractTalentInfo(
            """
            {
              "result": {
                "talentName": "anchor",
                "livingRoomJump": {
                  "params": {
                    "id": "live123"
                  }
                }
              }
            }
            """,
            result);
        JdSpider.ExtractPlayInfo(
            """
            {
              "data": {
                "status": 1,
                "videoUrl": "https://example.test/live.flv",
                "h5VideoUrl": "https://example.test/live.m3u8"
              }
            }
            """,
            result);

        Assert.Equal("live123", liveId);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
    }

    [Fact]
    public void PandaLiveExtractors_MapBjAndHlsData()
    {
        PandaLiveSpiderResult result = new()
        {
            RoomUrl = "https://www.pandalive.co.kr/live/play/bara0109",
            PlatformName = "PandaTV",
        };

        PandaLiveSpider.ExtractBjInfo(
            """
            {
              "bjInfo": {
                "id": "bara0109",
                "nick": "anchor",
                "profileImg": "https://example.test/avatar.png"
              },
              "media": {}
            }
            """,
            result);
        PandaLiveSpider.ExtractPlayInfo(
            """
            {
              "PlayList": {
                "hls": [
                  { "url": "https://example.test/live.m3u8" }
                ]
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor-bara0109", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
    }

    [Fact]
    public void WinkTvExtractors_MapBjAndHlsData()
    {
        WinkTvSpiderResult result = new()
        {
            RoomUrl = "https://www.winktv.co.kr/live/play/anjer1004",
            PlatformName = "WinkTV",
        };

        WinkTvSpider.ExtractBjInfo(
            """
            {
              "bjInfo": {
                "id": "anjer1004",
                "nick": "anchor",
                "profileImg": "https://example.test/avatar.png"
              },
              "media": {}
            }
            """,
            result);
        WinkTvSpider.ExtractPlayInfo(
            """
            {
              "PlayList": {
                "hls": [
                  { "url": "https://example.test/live.m3u8" }
                ]
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor-anjer1004", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
    }

    [Fact]
    public void TwitchExtractors_MapTokenRoomAndHlsData()
    {
        TwitchSpiderResult result = new()
        {
            RoomUrl = "https://www.twitch.tv/example",
            PlatformName = "Twitch",
        };

        TwitchAccessToken? token = TwitchSpider.ExtractPlaybackAccessToken(
            """
            {
              "data": {
                "streamPlaybackAccessToken": {
                  "value": "{\"channel\":\"example\"}",
                  "signature": "sig123"
                }
              }
            }
            """);
        TwitchSpider.ExtractRoomInfo(
            """
            [
              {
                "data": {
                  "userOrError": {
                    "login": "example",
                    "displayName": "Anchor",
                    "profileImageURL": "https://example.test/avatar.png",
                    "stream": {}
                  }
                }
              }
            ]
            """,
            result);

        Assert.NotNull(token);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("Anchor-example", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Contains("https://usher.ttvnw.net/api/channel/hls/example.m3u8", TwitchSpider.BuildHlsUrl("example", token));
    }

    [Fact]
    public void YouTubeExtractInitialPlayerResponse_MapsHlsData()
    {
        YouTubeSpiderResult result = new()
        {
            RoomUrl = "https://www.youtube.com/watch?v=abc123",
            PlatformName = "YouTube",
        };

        YouTubeSpider.ExtractInitialPlayerResponse(
            """
            <script>
            var ytInitialPlayerResponse = {
              "videoDetails": {
                "author": "anchor",
                "isLive": true
              },
              "streamingData": {
                "hlsManifestUrl": "https://example.test/live.m3u8"
              }
            };var meta = document.createElement
            </script>
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
    }

    [Fact]
    public void ShopeeExtractors_MapOngoingSessionAndFlvData()
    {
        ShopeeSpiderResult result = new()
        {
            RoomUrl = "https://live.shopee.sg/share?from=live&session=802458",
            PlatformName = "Shopee",
        };

        string? sessionId = ShopeeSpider.ExtractOngoingSessionId("""{"data":{"ongoing_live":{"session_id":"802458"}}}""");
        ShopeeSpider.ExtractSession(
            """
            {
              "data": {
                "session": {
                  "nickname": "anchor",
                  "status": 1,
                  "play_url": "https://example.test/live.flv"
                }
              }
            }
            """,
            true,
            result);

        Assert.Equal("802458", sessionId);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }

    [Fact]
    public void TwitCastingExtractors_MapPageAndHlsData()
    {
        TwitCastingSpiderResult result = new()
        {
            RoomUrl = "https://twitcasting.tv/example",
            PlatformName = "TwitCasting",
        };

        TwitCastingSpider.ExtractPageData(
            """
            <title>Anchor (@example)  live - TwitCasting</title>
            <meta name="twitter:title" content="Live title">
            <div data-is-onlive="true"
                 data-view-mode="normal"
                 data-movie-id="movie123" data-audience-id="audience"></div>
            """,
            result);
        TwitCastingSpider.ExtractStreamServer(
            """
            {
              "tc-hls": {
                "streams": {
                  "medium": "https://example.test/medium.m3u8",
                  "high": "https://example.test/high.m3u8"
                }
              }
            }
            """,
            result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("Anchor-example-movie123", result.Nickname);
        Assert.Equal("https://example.test/high.m3u8", result.HlsUrl);
    }

    [Fact]
    public void FaceitExtractors_MapUserAndStreamingData()
    {
        FaceitSpiderResult result = new()
        {
            RoomUrl = "https://www.faceit.com/players/qpjzz/stream",
            PlatformName = "Faceit",
        };

        string? userId = FaceitSpider.ExtractUserId("""{"payload":{"id":"user123"}}""");
        FaceitSpider.ExtractStreaming(
            """
            {
              "payload": [
                {
                  "userNickname": "anchor",
                  "platformId": "twitch_anchor",
                  "platform": "twitch"
                }
              ]
            }
            """,
            result);

        Assert.Equal("user123", userId);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("twitch_anchor", result.PlatformId);
        Assert.Equal("twitch", result.Platform);
    }

    [Fact]
    public void WeiboExtractors_MapTimelineAndLiveDetail()
    {
        WeiboSpiderResult result = new()
        {
            RoomUrl = "https://weibo.com/l/wblive/p/show/1022:2321325026370190442592",
            PlatformName = "Weibo",
        };

        string? roomId = WeiboSpider.ExtractLiveRoomId(
            """
            {
              "data": {
                "list": [
                  {
                    "page_info": {
                      "object_type": "live",
                      "object_id": "live123"
                    }
                  }
                ]
              }
            }
            """);
        WeiboSpider.ExtractLiveDetail(
            """
            {
              "data": {
                "user_info": { "name": "anchor" },
                "item": {
                  "status": 1,
                  "stream_info": {
                    "pull": {
                      "live_origin_hls_url": "https://example.test/live.m3u8",
                      "live_origin_flv_url": "https://example.test/live.flv"
                    }
                  }
                }
              }
            }
            """,
            result);

        Assert.Equal("live123", roomId);
        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }

    [Fact]
    public void HuajiaoExtractors_MapFeedAndSubstreamData()
    {
        HuajiaoSpiderResult result = new()
        {
            RoomUrl = "https://www.huajiao.com/l/345096174",
            PlatformName = "Huajiao",
        };

        HuajiaoSpider.ExtractFeedInfo(
            """
            {
              "errmsg": "",
              "data": {
                "creatime": 123,
                "author": {
                  "nickname": "anchor",
                  "avatar": "https://example.test/avatar.png",
                  "uid": "uid123"
                },
                "feed": {
                  "sn": "sn123",
                  "relateid": "live123"
                }
              }
            }
            """,
            result);
        HuajiaoSpider.ExtractSubstream("""{"data":{"h264_url":"https://example.test/live.flv"}}""", result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("https://example.test/avatar.png", result.AvatarThumbUrl);
        Assert.Equal("sn123", result.Sn);
        Assert.Equal("live123", result.LiveId);
        Assert.Equal("uid123", result.Uid);
        Assert.Equal("https://example.test/live.flv", result.FlvUrl);
    }

    [Fact]
    public void SoopExtractors_MapWatchAndCdnData()
    {
        SoopSpiderResult result = new()
        {
            RoomUrl = "https://play.sooplive.co.kr/sw7love",
            PlatformName = "SOOP",
        };

        SoopSpider.ExtractWatchInfo(
            """
            {
              "result": 1,
              "data": {
                "user_nick": "anchor",
                "bj_id": "sw7love",
                "broad_no": "123456",
                "hls_authentication_key": "aid123"
              }
            }
            """,
            result);
        SoopSpider.ApplyCdnInfo("""{"view_url":"https://example.test/master.m3u8"}""", result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor-sw7love", result.Nickname);
        Assert.Equal("123456", result.BroadNo);
        Assert.Equal("https://example.test/master.m3u8?aid=aid123", result.HlsUrl);
    }

    [Fact]
    public void FlexTvExtractors_MapNextDataAndStreamInfo()
    {
        FlexTvSpiderResult result = new()
        {
            RoomUrl = "https://www.flextv.co.kr/channels/593127/live",
            PlatformName = "FlexTV",
        };

        FlexTvSpider.ExtractNextData(
            """
            <script id="__NEXT_DATA__" type="application/json">
            {
              "props": {
                "pageProps": {
                  "channel": {
                    "owner": {
                      "nickname": "anchor",
                      "loginId": "login123"
                    }
                  }
                }
              }
            }
            </script>
            """,
            result);
        FlexTvSpider.ExtractStreamInfo("""{"sources":[{"url":"https://example.test/live.m3u8"}]}""", result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor-login123", result.Nickname);
        Assert.Equal("https://example.test/live.m3u8", result.HlsUrl);
    }

    [Fact]
    public void PopkonTvExtractors_MapNextDataAndWatchInfo()
    {
        PopkonTvSpiderResult result = new()
        {
            RoomUrl = "https://www.popkontv.com/live/view?castId=wjfal007&partnerCode=P-00117",
            PlatformName = "PopkonTV",
        };

        PopkonTvSpider.ExtractNextData(
            """
            <script id="__NEXT_DATA__" type="application/json">
            {
              "props": {
                "pageProps": {
                  "mcData": {
                    "data": {
                      "mc_nickName": "anchor",
                      "mc_castStartDate": "20260704120000",
                      "mc_signId": "wjfal007",
                      "castType": "0",
                      "mc_isPrivate": "0"
                    }
                  }
                }
              }
            }
            </script>
            """,
            result);
        PopkonTvSpider.ExtractWatchInfo("""{"statusCd":"L0000","data":{"castHlsUrl":"https://example.test/live.m3u8"}}""", result);

        Assert.True(result.IsLiveStreaming);
        Assert.Equal("anchor", result.Nickname);
        Assert.Equal("20260704120000", result.CastStartDate);
        Assert.Equal("wjfal007", result.CastSignId);
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
