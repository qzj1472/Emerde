using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed class CatShowSpider : ISpider
{
    public static Lazy<CatShowSpider> Instance { get; } = new(() => new CatShowSpider());

    public string PlatformName => "CatShow";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "h.catshow168.com")
        {
            return null;
        }

        string? anchorUid = GetQueryValue(uri.Query, "anchorUid");

        if (string.IsNullOrWhiteSpace(anchorUid))
        {
            return null;
        }

        return $"https://h.catshow168.com/live/preview.html?anchorUid={Uri.EscapeDataString(anchorUid)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        CatShowSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string? anchorUid = GetQueryValue(uri.Query, "anchorUid");

        if (string.IsNullOrWhiteSpace(anchorUid))
        {
            return result;
        }

        string body = JsonConvert.SerializeObject(new { inviteUuid = "", anchorUuid = anchorUid });
        string? json = SpiderRequest.PostJson("https://api.catshow168.com/live/preview", body, Headers(), PlatformCookieStore.GetCookie("CatShow", Configurations.CookieChina.Get()));
        ExtractPreview(json, result);

        return result;
    }

    internal static void ExtractPreview(string? json, CatShowSpiderResult result)
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

            result.Nickname = data["name"]?.ToString();
            result.AvatarThumbUrl = data["avatar"]?.ToString();
            result.IsLiveStreaming = data["living"]?.Value<bool>() == true;

            if (result.IsLiveStreaming == true)
            {
                result.HlsUrl = data["pullUrl"]?.ToString();
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
            ["Content-Type"] = "application/json",
            ["Origin"] = "https://h.catshow168.com",
            ["Referer"] = "https://h.catshow168.com/",
            ["User-Agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
        };
    }
}

public sealed class CatShowSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

