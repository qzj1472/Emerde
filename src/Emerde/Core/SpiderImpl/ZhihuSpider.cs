using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class ZhihuSpider : ISpider
{
    public static Lazy<ZhihuSpider> Instance { get; } = new(() => new ZhihuSpider());

    public string PlatformName => "Zhihu";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.zhihu.com")
        {
            return null;
        }

        string[] segments = uri.Segments.Select(segment => segment.Trim('/')).Where(segment => !string.IsNullOrWhiteSpace(segment)).ToArray();

        if (segments.Length >= 2 && segments[0].Equals("people", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://www.zhihu.com/people/{segments[1]}";
        }

        if (segments.Length >= 2 && segments[0].Equals("theater", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://www.zhihu.com/theater/{segments[^1]}";
        }

        return null;
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        ZhihuSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string livePageUrl = roomUrl;
        string[] segments = uri.Segments.Select(segment => segment.Trim('/')).Where(segment => !string.IsNullOrWhiteSpace(segment)).ToArray();

        if (segments.Length >= 2 && segments[0].Equals("people", StringComparison.OrdinalIgnoreCase))
        {
            string? profileJson = SpiderRequest.Get(
                $"https://api.zhihu.com/people/{Uri.EscapeDataString(segments[1])}/profile?profile_new_version=",
                Headers(),
                PlatformCookieStore.GetCookie("Zhihu", Configurations.CookieChina.Get()));
            livePageUrl = ExtractLivePageUrl(profileJson) ?? livePageUrl;
            result.RoomUrl = livePageUrl;
        }

        if (!Uri.TryCreate(livePageUrl, UriKind.Absolute, out Uri? liveUri))
        {
            return result;
        }

        string webId = liveUri.Segments.Select(segment => segment.Trim('/')).LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(webId))
        {
            return result;
        }

        string? html = SpiderRequest.Get(livePageUrl, Headers(), PlatformCookieStore.GetCookie("Zhihu", Configurations.CookieChina.Get()));
        ExtractInitialData(html, webId, result);

        return result;
    }

    internal static string? ExtractLivePageUrl(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(json);
            return root["drama"]?["living_theater"]?["theater_url"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static void ExtractInitialData(string? html, string webId, ZhihuSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(webId))
        {
            return;
        }

        try
        {
            Match match = InitialDataRegex.Match(html);

            if (!match.Success)
            {
                return;
            }

            string json = WebUtility.HtmlDecode(match.Groups[1].Value);
            JObject root = JObject.Parse(json);
            JObject? liveData = root["initialState"]?["theater"]?["theaters"]?[webId] as JObject;

            if (liveData == null)
            {
                return;
            }

            JObject? drama = liveData["drama"] as JObject;
            JObject? playInfo = drama?["playInfo"] as JObject;

            result.Nickname = liveData["actor"]?["name"]?.ToString();
            result.AvatarThumbUrl = liveData["actor"]?["avatarUrl"]?.ToString();
            result.IsLiveStreaming = drama?["status"]?.Value<int>() == 1;

            if (result.IsLiveStreaming == true)
            {
                result.HlsUrl = playInfo?["hlsUrl"]?.ToString();
                result.FlvUrl = playInfo?["playUrl"]?.ToString();
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
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0",
        };
    }

    [GeneratedRegex("<script id=\"js-initialData\" type=\"text/json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex InitialDataRegex { get; }
}

public sealed class ZhihuSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

