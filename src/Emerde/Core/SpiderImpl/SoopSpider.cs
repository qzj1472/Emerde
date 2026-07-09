using Newtonsoft.Json.Linq;

namespace Emerde.Core;

public sealed class SoopSpider : ISpider
{
    public static Lazy<SoopSpider> Instance { get; } = new(() => new SoopSpider());

    public string PlatformName => "SOOP";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (!uri.Host.EndsWith("sooplive.co.kr", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith("sooplive.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] segments = uri.Segments.Select(segment => segment.Trim('/')).Where(segment => !string.IsNullOrWhiteSpace(segment)).ToArray();

        if (segments.Length == 0)
        {
            return null;
        }

        string bjId = segments.Length >= 3 && segments[0].Equals("live", StringComparison.OrdinalIgnoreCase)
            ? segments[2]
            : segments[0];

        return uri.Host.EndsWith("sooplive.com", StringComparison.OrdinalIgnoreCase)
            ? $"https://www.sooplive.com/{bjId}"
            : $"https://play.sooplive.co.kr/{bjId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        SoopSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string bjId = uri.Segments.Select(segment => segment.Trim('/')).First(segment => !string.IsNullOrWhiteSpace(segment));

        if (uri.Host.EndsWith("sooplive.com", StringComparison.OrdinalIgnoreCase))
        {
            string? channelJson = SpiderRequest.Get($"https://api.sooplive.com/v2/channel/info/{Uri.EscapeDataString(bjId)}", GlobalHeaders(), PlatformCookieStore.GetCookie("SOOP", Configurations.CookieOversea.Get()));
            ExtractGlobalChannelInfo(channelJson, result);
            string? streamJson = SpiderRequest.Get($"https://api.sooplive.com/v2/stream/info/{Uri.EscapeDataString(bjId)}", GlobalHeaders(), PlatformCookieStore.GetCookie("SOOP", Configurations.CookieOversea.Get()));
            ExtractGlobalStreamInfo(streamJson, bjId, result);
            return result;
        }

        string? watchJson = SpiderRequest.PostForm(
            "http://api.m.sooplive.co.kr/broad/a/watch",
            new Dictionary<string, string>
            {
                ["bj_id"] = bjId,
                ["broad_no"] = string.Empty,
                ["agent"] = "web",
                ["confirm_adult"] = "true",
                ["player_type"] = "webm",
                ["mode"] = "live",
            },
            WatchHeaders(),
            PlatformCookieStore.GetCookie("SOOP", Configurations.CookieOversea.Get()));
        ExtractWatchInfo(watchJson, result);

        if (result.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(result.BroadNo) && !string.IsNullOrWhiteSpace(result.HlsAuthenticationKey))
        {
            string? cdnJson = SpiderRequest.Get(BuildCdnUrl(result.BroadNo), WatchHeaders(), PlatformCookieStore.GetCookie("SOOP", Configurations.CookieOversea.Get()));
            ApplyCdnInfo(cdnJson, result);
        }

        return result;
    }

    internal static void ExtractGlobalChannelInfo(string? json, SoopSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject channel = JObject.Parse(json)["data"]?["streamerChannelInfo"] as JObject ?? new JObject();
            string? nickname = channel["nickname"]?.ToString();
            string? channelId = channel["channelId"]?.ToString();
            result.Nickname = string.IsNullOrWhiteSpace(channelId) ? nickname : $"{nickname}-{channelId}";
        }
        catch
        {
        }
    }

    internal static void ExtractGlobalStreamInfo(string? json, string bjId, SoopSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject data = JObject.Parse(json)["data"] as JObject ?? new JObject();
            result.IsLiveStreaming = data["isStream"]?.Value<bool>() == true;

            if (result.IsLiveStreaming == true)
            {
                result.HlsUrl = $"https://global-media.sooplive.com/live/{bjId}/master.m3u8";
            }
        }
        catch
        {
        }
    }

    internal static void ExtractWatchInfo(string? json, SoopSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? data = root["data"] as JObject;

            if (data == null)
            {
                return;
            }

            string? nick = data["user_nick"]?.ToString();
            string? bjId = data["bj_id"]?.ToString();
            result.Nickname = string.IsNullOrWhiteSpace(bjId) ? nick : $"{nick}-{bjId}";
            result.IsLiveStreaming = root["result"]?.Value<int>() == 1 && !string.IsNullOrWhiteSpace(result.Nickname);
            result.BroadNo = data["broad_no"]?.ToString();
            result.HlsAuthenticationKey = data["hls_authentication_key"]?.ToString();
        }
        catch
        {
        }
    }

    internal static void ApplyCdnInfo(string? json, SoopSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            string? viewUrl = JObject.Parse(json)["view_url"]?.ToString();

            if (!string.IsNullOrWhiteSpace(viewUrl) && !string.IsNullOrWhiteSpace(result.HlsAuthenticationKey))
            {
                result.HlsUrl = $"{viewUrl}?aid={result.HlsAuthenticationKey}";
            }
        }
        catch
        {
        }
    }

    private static string BuildCdnUrl(string broadNo)
    {
        string broadKey = $"{broadNo}-common-master-hls";

        return "http://livestream-manager.sooplive.co.kr/broad_stream_assign.html"
             + "?return_type=gcp_cdn"
             + "&use_cors=false"
             + "&cors_origin_url=play.sooplive.co.kr"
             + $"&broad_key={Uri.EscapeDataString(broadKey)}"
             + "&time=8361.086329376785";
    }

    private static IReadOnlyDictionary<string, string> GlobalHeaders()
    {
        return new Dictionary<string, string>
        {
            ["client-id"] = Guid.NewGuid().ToString(),
            ["user-agent"] = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1 Edg/141.0.0.0",
        };
    }

    private static IReadOnlyDictionary<string, string> WatchHeaders()
    {
        return new Dictionary<string, string>
        {
            ["Accept-Language"] = "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2",
            ["Content-Type"] = "application/x-www-form-urlencoded",
            ["Referer"] = "https://m.sooplive.co.kr/",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/119.0",
        };
    }
}

public sealed class SoopSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? BroadNo { get; set; }

    public string? HlsAuthenticationKey { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

