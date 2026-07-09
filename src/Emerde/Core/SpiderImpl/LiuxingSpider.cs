using Newtonsoft.Json.Linq;

namespace Emerde.Core;

public sealed class LiuxingSpider : ISpider
{
    public static Lazy<LiuxingSpider> Instance { get; } = new(() => new LiuxingSpider());

    public string PlatformName => "Liuxing";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (!uri.Host.EndsWith("7u66.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? roomId = uri.Segments.Select(segment => segment.Trim('/')).FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return null;
        }

        return $"https://www.7u66.com/{roomId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        LiuxingSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomId = roomUrl.Split('/').Last();
        string currentUrl = $"https://www.7u66.com/{roomId}?promoters=0";
        string api = "https://wap.7u66.com/api/ui/room/v1.0.0/live.ashx"
                   + $"?promoters=0&roomidx={Uri.EscapeDataString(roomId)}&currentUrl={Uri.EscapeDataString(currentUrl)}";
        string? json = SpiderRequest.Get(api, Headers(), PlatformCookieStore.GetCookie("Liuxing", Configurations.CookieChina.Get()));
        ExtractRoomInfo(json, result);

        return result;
    }

    internal static void ExtractRoomInfo(string? json, LiuxingSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? roomInfo = root["data"]?["roomInfo"] as JObject;

            if (roomInfo == null)
            {
                return;
            }

            result.Nickname = roomInfo["nickname"]?.ToString();
            result.IsLiveStreaming = roomInfo["live_stat"]?.Value<int>() == 1;

            if (result.IsLiveStreaming == true)
            {
                string? idx = roomInfo["idx"]?.ToString();
                string? liveId = roomInfo["liveId1"]?.ToString();

                if (!string.IsNullOrWhiteSpace(idx) && !string.IsNullOrWhiteSpace(liveId))
                {
                    result.FlvUrl = $"https://txpull1.5see.com/live/{idx}/{liveId}.flv";
                }
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
            ["Accept"] = "application/json, text/plain, */*",
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["Referer"] = "https://wap.7u66.com/198189?promoters=0",
            ["User-Agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
        };
    }
}

public sealed class LiuxingSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

