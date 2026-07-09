using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed class WinkTvSpider : ISpider
{
    public static Lazy<WinkTvSpider> Instance { get; } = new(() => new WinkTvSpider());

    public string PlatformName => "WinkTV";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.winktv.co.kr")
        {
            return null;
        }

        string? userId = uri.Segments.Select(segment => segment.Trim('/')).LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment));

        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        string? password = GetQueryValue(uri.Query, "pwd");
        string normalized = $"https://www.winktv.co.kr/live/play/{Uri.EscapeDataString(userId)}";

        return string.IsNullOrWhiteSpace(password) ? normalized : $"{normalized}?pwd={Uri.EscapeDataString(password)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        WinkTvSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string userId = uri.Segments.Select(segment => segment.Trim('/')).Last(segment => !string.IsNullOrWhiteSpace(segment));
        string password = GetQueryValue(uri.Query, "pwd") ?? string.Empty;
        string? infoJson = SpiderRequest.PostForm(
            "https://api.winktv.co.kr/v1/member/bj",
            new Dictionary<string, string>
            {
                ["userId"] = userId,
                ["info"] = "media",
            },
            Headers(),
            PlatformCookieStore.GetCookie("WinkTV", Configurations.CookieOversea.Get()));
        ExtractBjInfo(infoJson, result);

        if (result.IsLiveStreaming == true)
        {
            string? playJson = SpiderRequest.PostForm(
                "https://api.winktv.co.kr/v1/live/play",
                new Dictionary<string, string>
                {
                    ["action"] = "watch",
                    ["userId"] = userId,
                    ["password"] = password,
                    ["shareLinkType"] = string.Empty,
                },
                Headers(),
                PlatformCookieStore.GetCookie("WinkTV", Configurations.CookieOversea.Get()));
            ExtractPlayInfo(playJson, result);
        }

        return result;
    }

    internal static void ExtractBjInfo(string? json, WinkTvSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? bjInfo = root["bjInfo"] as JObject;

            if (bjInfo == null)
            {
                return;
            }

            string? nick = bjInfo["nick"]?.ToString();
            string? id = bjInfo["id"]?.ToString();
            result.Nickname = string.IsNullOrWhiteSpace(id) ? nick : $"{nick}-{id}";
            result.AvatarThumbUrl = bjInfo["profileImg"]?.ToString();
            result.IsLiveStreaming = root["media"] != null;
        }
        catch
        {
        }
    }

    internal static void ExtractPlayInfo(string? json, WinkTvSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);

            if (root["errorData"] != null)
            {
                return;
            }

            result.HlsUrl = root["PlayList"]?["hls"]?.FirstOrDefault()?["url"]?.ToString();
        }
        catch
        {
        }
    }

    private static string? GetQueryValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match match = Regex.Match(value, $"(?:\\?|&){Regex.Escape(name)}=([^&]+)", RegexOptions.IgnoreCase);

        return match.Success ? WebUtility.UrlDecode(match.Groups[1].Value) : null;
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["accept"] = "application/json, text/plain, */*",
            ["accept-language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["content-type"] = "application/x-www-form-urlencoded",
            ["origin"] = "https://www.winktv.co.kr",
            ["referer"] = "https://www.winktv.co.kr",
            ["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0",
        };
    }
}

public sealed class WinkTvSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

