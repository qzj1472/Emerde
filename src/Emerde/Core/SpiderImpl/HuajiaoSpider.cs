using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;

namespace Emerde.Core;

public sealed class HuajiaoSpider : ISpider
{
    public static Lazy<HuajiaoSpider> Instance { get; } = new(() => new HuajiaoSpider());

    public string PlatformName => "Huajiao";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.huajiao.com")
        {
            return null;
        }

        string[] segments = uri.Segments.Select(segment => segment.Trim('/')).Where(segment => !string.IsNullOrWhiteSpace(segment)).ToArray();

        if (segments.Length >= 2 && segments[0].Equals("l", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://www.huajiao.com/l/{segments[1]}";
        }

        if (segments.Length >= 2 && segments[0].Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://www.huajiao.com/user/{segments[1]}";
        }

        return null;
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        HuajiaoSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string[] segments = uri.Segments.Select(segment => segment.Trim('/')).Where(segment => !string.IsNullOrWhiteSpace(segment)).ToArray();

        if (segments.Length < 2)
        {
            return result;
        }

        if (segments[0].Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            string? feedsJson = SpiderRequest.Get(
                $"https://webh.huajiao.com/User/getUserFeeds?uid={Uri.EscapeDataString(segments[1])}&fmt=json&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                WebHeaders(),
                PlatformCookieStore.GetCookie("Huajiao", SecretProtector.GetChinaCookie()));
            ExtractUserFeeds(feedsJson, segments[1], result);
        }
        else
        {
            string? feedJson = SpiderRequest.Get(
                $"https://live.huajiao.com/feed/getFeedInfo?relateid={Uri.EscapeDataString(segments[1])}",
                AppHeaders(),
                PlatformCookieStore.GetCookie("Huajiao", SecretProtector.GetChinaCookie()));
            ExtractFeedInfo(feedJson, result);
        }

        if (result.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(result.Sn) && !string.IsNullOrWhiteSpace(result.LiveId) && !string.IsNullOrWhiteSpace(result.Uid))
        {
            string api = "https://live.huajiao.com/live/substream"
                       + $"?time={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                       + "&version=1.0.0"
                       + $"&sn={Uri.EscapeDataString(result.Sn)}"
                       + $"&liveid={Uri.EscapeDataString(result.LiveId)}"
                       + $"&uid={Uri.EscapeDataString(result.Uid)}"
                       + "&encode=h265";
            string? substreamJson = SpiderRequest.Get(api, WebHeaders(), PlatformCookieStore.GetCookie("Huajiao", SecretProtector.GetChinaCookie()));
            ExtractSubstream(substreamJson, result);
        }

        return result;
    }

    internal static void ExtractFeedInfo(string? json, HuajiaoSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);

            if (!string.IsNullOrWhiteSpace(root["errmsg"]?.ToString()))
            {
                return;
            }

            JObject? data = root["data"] as JObject;

            if (data == null || data["creatime"] == null)
            {
                return;
            }

            result.Nickname = data["author"]?["nickname"]?.ToString();
            result.AvatarThumbUrl = data["author"]?["avatar"]?.ToString();
            result.Sn = data["feed"]?["sn"]?.ToString();
            result.LiveId = data["feed"]?["relateid"]?.ToString();
            result.Uid = data["author"]?["uid"]?.ToString();
            result.IsLiveStreaming = true;
        }
        catch
        {
        }
    }

    internal static void ExtractUserFeeds(string? json, string uid, HuajiaoSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? feed = root["data"]?["feeds"]?.FirstOrDefault()?["feed"] as JObject;

            if (feed == null || string.IsNullOrWhiteSpace(feed["sn"]?.ToString()))
            {
                result.IsLiveStreaming = false;
                return;
            }

            result.Nickname = feed["author"]?["nickname"]?.ToString();
            result.Sn = feed["sn"]?.ToString();
            result.LiveId = feed["relateid"]?.ToString();
            result.Uid = uid;
            result.IsLiveStreaming = true;
        }
        catch
        {
        }
    }

    internal static void ExtractSubstream(string? json, HuajiaoSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            result.FlvUrl = root["data"]?["h264_url"]?.ToString();
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string> WebHeaders()
    {
        return new Dictionary<string, string>
        {
            ["accept-language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["referer"] = "https://www.huajiao.com/",
            ["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0",
        };
    }

    private static IReadOnlyDictionary<string, string> AppHeaders()
    {
        return new Dictionary<string, string>
        {
            ["accept-language"] = "zh-Hans-US;q=1.0",
            ["sdk_version"] = "1",
            ["User-Agent"] = "living/9.4.0 (com.huajiao.seeding; build:2410231746; iOS 17.0.0) Alamofire/9.4.0",
        };
    }
}

public sealed class HuajiaoSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? Sn { get; set; }

    public string? LiveId { get; set; }

    public string? Uid { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

