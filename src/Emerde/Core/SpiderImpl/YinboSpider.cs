using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class YinboSpider : ISpider
{
    public static Lazy<YinboSpider> Instance { get; } = new(() => new YinboSpider());

    public string PlatformName => "Yinbo";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (!uri.Host.EndsWith("ybw1666.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? roomId = uri.Segments.Select(segment => segment.Trim('/')).FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return null;
        }

        return $"https://live.ybw1666.com/{roomId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        YinboSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomId = roomUrl.Split('/').Last();
        string currentUrl = $"https://wap.ybw1666.com/{roomId}";
        string api = "https://wap.ybw1666.com/api/ui/room/v1.0.0/live.ashx"
                   + $"?roomidx={Uri.EscapeDataString(roomId)}&currentUrl={Uri.EscapeDataString(currentUrl)}";
        string? json = SpiderRequest.Get(api, Headers(), Configurations.CookieChina.Get());
        ExtractRoomInfo(json, result);

        if (result.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(result.LiveId))
        {
            string? html = SpiderRequest.Get(roomUrl, Headers(), Configurations.CookieChina.Get());
            (string? flvDomain, string? hlsDomain) = ExtractLiveDomain(html);
            ApplyLiveDomain(result, flvDomain, hlsDomain);
        }

        return result;
    }

    internal static void ExtractRoomInfo(string? json, YinboSpiderResult result)
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
            result.LiveId = roomInfo["liveID"]?.ToString();
        }
        catch
        {
        }
    }

    internal static (string? FlvDomain, string? HlsDomain) ExtractLiveDomain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return (null, null);
        }

        try
        {
            Match match = ConfigRegex.Match(html);

            if (!match.Success)
            {
                return (null, null);
            }

            string configJson = match.Groups[1].Value;
            int semicolonIndex = configJson.LastIndexOf(';');

            if (semicolonIndex >= 0)
            {
                configJson = configJson[..semicolonIndex];
            }

            JObject config = JObject.Parse(configJson.Trim());

            return (
                config["domainpullstream_flv"]?.ToString(),
                config["domainpullstream_hls"]?.ToString());
        }
        catch
        {
            return (null, null);
        }
    }

    internal static void ApplyLiveDomain(YinboSpiderResult result, string? flvDomain, string? hlsDomain)
    {
        if (string.IsNullOrWhiteSpace(result.LiveId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(flvDomain))
        {
            result.FlvUrl = $"{flvDomain.TrimEnd('/')}/{result.LiveId}.flv";
        }

        if (!string.IsNullOrWhiteSpace(hlsDomain))
        {
            result.HlsUrl = $"{hlsDomain.TrimEnd('/')}/{result.LiveId}.m3u8";
        }
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["Accept"] = "application/json, text/plain, */*",
            ["Accept-Language"] = "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2",
            ["Referer"] = "https://live.ybw1666.com/800005143?promoters=0",
            ["User-Agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
        };
    }

    [GeneratedRegex("var config = (.*?)config\\.webskins", RegexOptions.Singleline)]
    private static partial Regex ConfigRegex { get; }
}

public sealed class YinboSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? LiveId { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

