using Newtonsoft.Json.Linq;

namespace Emerde.Core;

public sealed class MaoerFmSpider : ISpider
{
    public static Lazy<MaoerFmSpider> Instance { get; } = new(() => new MaoerFmSpider());

    public string PlatformName => "MaoerFM";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "fm.missevan.com")
        {
            return null;
        }

        string[] segments = uri.Segments.Select(segment => segment.Trim('/')).Where(segment => !string.IsNullOrWhiteSpace(segment)).ToArray();
        int liveIndex = Array.FindIndex(segments, segment => string.Equals(segment, "live", StringComparison.OrdinalIgnoreCase));

        if (liveIndex < 0 || liveIndex + 1 >= segments.Length || !segments[liveIndex + 1].All(char.IsDigit))
        {
            return null;
        }

        return $"https://fm.missevan.com/live/{segments[liveIndex + 1]}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        MaoerFmSpiderResult result = new()
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

        string? json = SpiderRequest.Get(
            $"https://fm.missevan.com/api/v2/live/{Uri.EscapeDataString(roomId)}",
            Headers(roomUrl),
            Configurations.CookieChina.Get());
        ExtractRoomInfo(json, result);

        return result;
    }

    internal static void ExtractRoomInfo(string? json, MaoerFmSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? info = root["info"] as JObject;
            JObject? room = info?["room"] as JObject;
            JObject? channel = room?["channel"] as JObject;

            result.Nickname = info?["creator"]?["username"]?.ToString();
            result.AvatarThumbUrl = info?["creator"]?["icon"]?.ToString();
            result.IsLiveStreaming = room?["status"]?["broadcasting"]?.Value<bool>() == true;

            if (result.IsLiveStreaming != true)
            {
                return;
            }

            result.HlsUrl = channel?["hls_pull_url"]?.ToString();
            result.FlvUrl = channel?["flv_pull_url"]?.ToString();

            if (string.IsNullOrWhiteSpace(result.HlsUrl) && string.IsNullOrWhiteSpace(result.FlvUrl))
            {
                result.IsLiveStreaming = false;
            }
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string> Headers(string referer)
    {
        return new Dictionary<string, string>
        {
            ["Accept"] = "application/json, text/plain, */*",
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["Referer"] = referer,
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0",
        };
    }
}

public sealed class MaoerFmSpiderResult : ISpiderResult
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
