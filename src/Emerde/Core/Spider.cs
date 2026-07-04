namespace Emerde.Core;

internal static class Spider
{
    private static readonly Lazy<ISpider[]> Spiders = new(() =>
    [
        DouyinSpider.Instance.Value,
        TiktokSpider.Instance.Value,
        BilibiliSpider.Instance.Value,
        KuaishouSpider.Instance.Value,
        BigoSpider.Instance.Value,
        XiaohongshuSpider.Instance.Value,
        KugouSpider.Instance.Value,
        YingkeSpider.Instance.Value,
        ShowRoomSpider.Instance.Value,
        AcFunSpider.Instance.Value,
        YySpider.Instance.Value,
        NeteaseCcSpider.Instance.Value,
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

    public static ISpiderResult? GetResult(string url)
    {
        foreach (ISpider spider in Spiders.Value)
        {
            if (!string.IsNullOrWhiteSpace(spider.ParseUrl(url)))
            {
                return spider.GetResult(url);
            }
        }

        return null;
    }

    public static string GetPlatformName(string url)
    {
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
