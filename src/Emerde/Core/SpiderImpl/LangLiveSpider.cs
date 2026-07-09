using Newtonsoft.Json.Linq;

namespace Emerde.Core;

public sealed class LangLiveSpider : ISpider
{
    public static Lazy<LangLiveSpider> Instance { get; } = new(() => new LangLiveSpider());

    public string PlatformName => "LangLive";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.lang.live" && uri.Host != "lang.live")
        {
            return null;
        }

        string roomId = uri.Segments.Select(segment => segment.Trim('/')).LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomId) || !roomId.All(char.IsDigit))
        {
            return null;
        }

        return $"https://www.lang.live/en-US/room/{roomId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        LangLiveSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomId = roomUrl.Split('/').Last();
        result.RoomId = roomId;

        string? json = SpiderRequest.Get($"https://api.lang.live/langweb/v1/room/liveinfo?room_id={Uri.EscapeDataString(roomId)}", Headers(), PlatformCookieStore.GetCookie("LangLive", Configurations.CookieOversea.Get()));
        ExtractLiveInfo(json, result);

        return result;
    }

    internal static void ExtractLiveInfo(string? json, LangLiveSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? liveInfo = root["data"]?["live_info"] as JObject;

            if (liveInfo == null)
            {
                return;
            }

            result.Nickname = liveInfo["nickname"]?.ToString();
            result.AvatarThumbUrl = liveInfo["avatar"]?.ToString();
            result.IsLiveStreaming = liveInfo["live_status"]?.Value<int>() == 1;

            if (result.IsLiveStreaming == true)
            {
                result.FlvUrl = liveInfo["liveurl"]?.ToString();
                result.HlsUrl = liveInfo["liveurl_hls"]?.ToString();
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
            ["Origin"] = "https://www.lang.live",
            ["Referer"] = "https://www.lang.live/",
            ["User-Agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
        };
    }
}

public sealed class LangLiveSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? RoomId { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
