namespace Emerde.Core;

internal static class Spider
{
    private static readonly Lazy<ISpider[]> Spiders = new(() =>
    [
        DouyinSpider.Instance.Value,
        TiktokSpider.Instance.Value,
        BilibiliSpider.Instance.Value,
        KuaishouSpider.Instance.Value,
        HuyaSpider.Instance.Value,
        DouyuSpider.Instance.Value,
        BaiduSpider.Instance.Value,
        BigoSpider.Instance.Value,
        SeventeenLiveSpider.Instance.Value,
        ChzzkSpider.Instance.Value,
        MaoerFmSpider.Instance.Value,
        PicartoSpider.Instance.Value,
        LianjieSpider.Instance.Value,
        LangLiveSpider.Instance.Value,
        SixRoomsSpider.Instance.Value,
        VvXqiuSpider.Instance.Value,
        BluedSpider.Instance.Value,
        LiuxingSpider.Instance.Value,
        ChangliaoSpider.Instance.Value,
        YinboSpider.Instance.Value,
        ZhihuSpider.Instance.Value,
        PpLiveSpider.Instance.Value,
        CatShowSpider.Instance.Value,
        LaixiuSpider.Instance.Value,
        JdSpider.Instance.Value,
        PandaLiveSpider.Instance.Value,
        WinkTvSpider.Instance.Value,
        TwitchSpider.Instance.Value,
        YouTubeSpider.Instance.Value,
        ShopeeSpider.Instance.Value,
        TwitCastingSpider.Instance.Value,
        FaceitSpider.Instance.Value,
        WeiboSpider.Instance.Value,
        HuajiaoSpider.Instance.Value,
        SoopSpider.Instance.Value,
        FlexTvSpider.Instance.Value,
        PopkonTvSpider.Instance.Value,
        LookLiveSpider.Instance.Value,
        TaobaoSpider.Instance.Value,
        LiveMeSpider.Instance.Value,
        XiaohongshuSpider.Instance.Value,
        KugouSpider.Instance.Value,
        YingkeSpider.Instance.Value,
        ShowRoomSpider.Instance.Value,
        AcFunSpider.Instance.Value,
        YySpider.Instance.Value,
        NeteaseCcSpider.Instance.Value,
        QianduReboSpider.Instance.Value,
        DirectStreamSpider.Instance.Value,
    ]);

    public static IReadOnlyList<string> SupportedPlatformNames => Spiders.Value.Select(spider => spider.PlatformName).ToArray();

    public static string? ParseUrl(string url)
    {
        foreach (ISpider spider in Spiders.Value)
        {
            string? roomUrl = spider.ParseUrl(url);

            if (!string.IsNullOrWhiteSpace(roomUrl))
            {
                return roomUrl;
            }
        }

        return null;
    }

    public static ISpiderResult? GetResult(string url, string? preferredQuality = null)
    {
        ISpiderResult? resolverResult = StreamResolver.GetResult(url, preferredQuality);
        if (StreamResolver.HasUsableData(resolverResult))
        {
            return resolverResult;
        }

        foreach (ISpider spider in Spiders.Value)
        {
            if (!string.IsNullOrWhiteSpace(spider.ParseUrl(url)))
            {
                return spider is IQualitySelectableSpider qualitySelectable
                    ? qualitySelectable.GetResult(url, preferredQuality)
                    : spider.GetResult(url);
            }
        }

        return null;
    }

    public static string GetPlatformName(string url)
    {
        string resolverPlatformName = StreamResolver.GetPlatformName(url);
        if (!string.IsNullOrWhiteSpace(resolverPlatformName))
        {
            return resolverPlatformName;
        }

        foreach (ISpider spider in Spiders.Value)
        {
            if (!string.IsNullOrWhiteSpace(spider.ParseUrl(url)))
            {
                return spider.PlatformName;
            }
        }

        return string.Empty;
    }
}

public interface ISpider
{
    public string PlatformName { get; }

    public string? ParseUrl(string url);

    public ISpiderResult GetResult(string url);
}

public interface IQualitySelectableSpider
{
    public ISpiderResult GetResult(string url, string? preferredQuality);
}

public interface ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
