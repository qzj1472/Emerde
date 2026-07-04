using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class FlexTvSpider : ISpider
{
    public static Lazy<FlexTvSpider> Instance { get; } = new(() => new FlexTvSpider());

    public string PlatformName => "FlexTV";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.flextv.co.kr" && uri.Host != "www.ttinglive.com")
        {
            return null;
        }

        Match match = ChannelRegex.Match(uri.AbsolutePath);

        return match.Success ? $"https://www.flextv.co.kr/channels/{match.Groups[1].Value}/live" : null;
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        FlexTvSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string channelId = ChannelRegex.Match(new Uri(roomUrl).AbsolutePath).Groups[1].Value;
        string? html = SpiderRequest.Get($"https://www.ttinglive.com/channels/{Uri.EscapeDataString(channelId)}/live", Headers(), Configurations.CookieOversea.Get());
        ExtractNextData(html, result);

        if (result.IsLiveStreaming == true)
        {
            string? streamJson = SpiderRequest.Get($"https://www.ttinglive.com/api/channels/{Uri.EscapeDataString(channelId)}/stream?option=all", Headers(), Configurations.CookieOversea.Get());
            ExtractStreamInfo(streamJson, result);
        }

        return result;
    }

    internal static void ExtractNextData(string? html, FlexTvSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        try
        {
            Match match = NextDataRegex.Match(html);

            if (!match.Success)
            {
                return;
            }

            JObject root = JObject.Parse(match.Groups[1].Value);
            JObject? channel = root["props"]?["pageProps"]?["channel"] as JObject;

            if (channel == null)
            {
                return;
            }

            result.IsLiveStreaming = channel["message"] == null;

            if (result.IsLiveStreaming == true)
            {
                string? nickname = channel["owner"]?["nickname"]?.ToString();
                string? loginId = channel["owner"]?["loginId"]?.ToString();
                result.Nickname = string.IsNullOrWhiteSpace(loginId) ? nickname : $"{nickname}-{loginId}";
            }
        }
        catch
        {
        }
    }

    internal static void ExtractStreamInfo(string? json, FlexTvSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            string? playUrl = root["sources"]?.FirstOrDefault()?["url"]?.ToString();

            if (string.IsNullOrWhiteSpace(playUrl))
            {
                return;
            }

            if (playUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                result.HlsUrl = playUrl;
            }
            else
            {
                result.FlvUrl = playUrl;
            }
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["accept"] = "application/json, text/plain, */*",
            ["accept-language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["referer"] = "https://www.ttinglive.com/",
            ["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0",
        };
    }

    [GeneratedRegex("/channels/([^/]+)/live", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelRegex { get; }

    [GeneratedRegex("<script id=\"__NEXT_DATA__\" type=\".*?\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex NextDataRegex { get; }
}

public sealed class FlexTvSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

