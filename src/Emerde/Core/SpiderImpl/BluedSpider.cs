using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class BluedSpider : ISpider
{
    public static Lazy<BluedSpider> Instance { get; } = new(() => new BluedSpider());

    public string PlatformName => "Blued";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "app.blued.cn")
        {
            return null;
        }

        string? roomId = GetQueryValue(uri.Query, "id");

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return null;
        }

        return $"https://app.blued.cn/live?id={Uri.EscapeDataString(roomId)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        BluedSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string? html = SpiderRequest.Get(roomUrl, Headers(), PlatformCookieStore.GetCookie("Blued", SecretProtector.GetChinaCookie()));
        ExtractPageData(html, result);

        return result;
    }

    internal static void ExtractPageData(string? html, BluedSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        try
        {
            Match match = EncodedStateRegex.Match(html);

            if (!match.Success)
            {
                return;
            }

            string json = Uri.UnescapeDataString(match.Groups[1].Value);
            JObject root = JObject.Parse(json);
            JObject? userInfo = root["userInfo"] as JObject;
            JObject? liveInfo = root["liveInfo"] as JObject;

            result.Nickname = userInfo?["name"]?.ToString();
            result.AvatarThumbUrl = userInfo?["avatar"]?.ToString();
            result.IsLiveStreaming = userInfo?["onLive"]?.Value<bool>() == true;

            if (result.IsLiveStreaming == true)
            {
                result.HlsUrl = liveInfo?["liveUrl"]?.ToString();
            }
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
            ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
            ["Accept-Language"] = "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2",
            ["User-Agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
        };
    }

    [GeneratedRegex("decodeURIComponent\\(\"(.*?)\"\\)\\),window\\.Promise", RegexOptions.Singleline)]
    private static partial Regex EncodedStateRegex { get; }
}

public sealed class BluedSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

