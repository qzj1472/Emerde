using Newtonsoft.Json.Linq;

namespace Emerde.Core;

public sealed class PicartoSpider : ISpider
{
    public static Lazy<PicartoSpider> Instance { get; } = new(() => new PicartoSpider());

    public string PlatformName => "Picarto";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.picarto.tv" && uri.Host != "picarto.tv")
        {
            return null;
        }

        string channel = uri.Segments.Select(segment => segment.Trim('/')).FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(channel))
        {
            return null;
        }

        return $"https://www.picarto.tv/{channel}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        PicartoSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string channel = roomUrl.Split('/').Last();
        result.Channel = channel;

        string? json = SpiderRequest.Get($"https://ptvintern.picarto.tv/api/channel/detail/{Uri.EscapeDataString(channel)}", Headers(), PlatformCookieStore.GetCookie("Picarto", SecretProtector.GetOverseaCookie()));
        ExtractChannelDetail(json, result);

        return result;
    }

    internal static void ExtractChannelDetail(string? json, PicartoSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? channel = root["channel"] as JObject;

            if (channel == null)
            {
                return;
            }

            result.Nickname = channel["name"]?.ToString();
            result.AvatarThumbUrl = channel["avatar"]?.ToString();
            result.IsLiveStreaming = channel["online"]?.Value<bool>() == true;

            if (result.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(result.Nickname))
            {
                result.HlsUrl = $"https://1-edge1-us-newyork.picarto.tv/stream/hls/golive+{result.Nickname}/index.m3u8";
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
            ["Accept-Language"] = "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36 Edg/121.0.0.0",
        };
    }
}

public sealed class PicartoSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? Channel { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
