using Newtonsoft.Json.Linq;

namespace Emerde.Core;

public sealed class LianjieSpider : ISpider
{
    public static Lazy<LianjieSpider> Instance { get; } = new(() => new LianjieSpider());

    public string PlatformName => "Lianjie";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (!uri.Host.Contains("lailianjie.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string roomNumber = uri.Segments.Select(segment => segment.Trim('/')).FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomNumber))
        {
            return null;
        }

        return $"https://show.lailianjie.com/{roomNumber}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        LianjieSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomNumber = roomUrl.Split('/').Last();
        result.RoomNumber = roomNumber;

        string? json = SpiderRequest.Get(
            $"https://api.lailianjie.com/ApiServices/service/live/getRoomInfo?&_$t=&_sign=&roomNumber={Uri.EscapeDataString(roomNumber)}",
            Headers(),
            Configurations.CookieChina.Get());
        ExtractRoomInfo(json, result);

        return result;
    }

    internal static void ExtractRoomInfo(string? json, LianjieSpiderResult result)
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

            result.Nickname = data["nickname"]?.ToString();
            result.IsLiveStreaming = data["isonline"]?.Value<int>() == 1;

            if (result.IsLiveStreaming != true)
            {
                return;
            }

            string? videoUrl = data["videoUrl"]?.ToString();

            if (string.IsNullOrWhiteSpace(videoUrl))
            {
                result.IsLiveStreaming = false;
                return;
            }

            string httpsUrl = videoUrl.StartsWith("webrtc://", StringComparison.OrdinalIgnoreCase)
                ? $"https://{videoUrl[9..]}"
                : videoUrl;
            result.FlvUrl = httpsUrl.Replace("?", ".flv?", StringComparison.Ordinal);
            result.HlsUrl = httpsUrl.Replace("?", ".m3u8?", StringComparison.Ordinal);
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["Accept-Language"] = "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36 Edg/121.0.0.0",
        };
    }
}

public sealed class LianjieSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? RoomNumber { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
