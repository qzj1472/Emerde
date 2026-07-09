using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class JdSpider : ISpider
{
    public static Lazy<JdSpider> Instance { get; } = new(() => new JdSpider());

    public string PlatformName => "JD";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "3.cn" && uri.Host != "m.jd.com" && uri.Host != "eco.m.jd.com")
        {
            return null;
        }

        if (uri.Host == "3.cn")
        {
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + uri.Fragment;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + uri.Query + uri.Fragment;
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        JdSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string redirectUrl = ResolveRedirectUrl(roomUrl) ?? roomUrl;
        result.RoomUrl = redirectUrl;
        string? authorId = GetQueryValue(redirectUrl, "authorId");
        string? liveId = null;

        if (string.IsNullOrWhiteSpace(authorId))
        {
            Match liveIdMatch = LiveIdFragmentRegex.Match(redirectUrl);
            liveId = liveIdMatch.Success ? liveIdMatch.Groups[1].Value : GetQueryValue(redirectUrl, "id");
            result.Nickname = string.IsNullOrWhiteSpace(liveId) ? string.Empty : $"jd_{liveId}";
        }
        else
        {
            string? talentJson = SpiderRequest.PostForm(
                "https://api.m.jd.com/talent_head_findTalentMsg",
                new Dictionary<string, string>
                {
                    ["functionId"] = "talent_head_findTalentMsg",
                    ["appid"] = "dr_detail",
                    ["body"] = "{\"authorId\":\"" + authorId + "\",\"monitorSource\":\"1\",\"userId\":\"\"}",
                },
                Headers(),
                PlatformCookieStore.GetCookie("JD", Configurations.CookieChina.Get()));
            liveId = ExtractTalentInfo(talentJson, result);
        }

        if (string.IsNullOrWhiteSpace(liveId))
        {
            return result;
        }

        string? playJson = SpiderRequest.Get(BuildPlayApiUrl(liveId), Headers(), PlatformCookieStore.GetCookie("JD", Configurations.CookieChina.Get()));
        ExtractPlayInfo(playJson, result);

        return result;
    }

    internal static string? ExtractTalentInfo(string? json, JdSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? data = root["result"] as JObject;

            if (data == null)
            {
                return null;
            }

            result.Nickname = data["talentName"]?.ToString();
            return data["livingRoomJump"]?["params"]?["id"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static void ExtractPlayInfo(string? json, JdSpiderResult result)
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

            result.IsLiveStreaming = data["status"]?.Value<int>() == 1;

            if (result.IsLiveStreaming == true)
            {
                result.FlvUrl = data["videoUrl"]?.ToString();
                result.HlsUrl = data["h5VideoUrl"]?.ToString();
            }
        }
        catch
        {
        }
    }

    private static string BuildPlayApiUrl(string liveId)
    {
        string body = "{\"liveId\": \"" + liveId + "\"}";

        return "https://api.m.jd.com/client.action"
             + $"?body={Uri.EscapeDataString(body)}"
             + "&functionId=getImmediatePlayToM"
             + "&appid=h5-live";
    }

    private static string? ResolveRedirectUrl(string url)
    {
        try
        {
            RestClientOptions options = new()
            {
                BaseUrl = new Uri(url),
            };

            if (Configurations.IsUseProxy.Get())
            {
                string proxyUrl = Configurations.ProxyUrl.Get();

                if (!string.IsNullOrWhiteSpace(proxyUrl))
                {
                    options.Proxy = new WebProxy($"http://{proxyUrl}");
                }
            }

            RestClient client = new(options);
            RestRequest request = new()
            {
                Method = Method.Get,
                Timeout = TimeSpan.FromSeconds(5),
            };

            foreach ((string key, string value) in Headers())
            {
                request.AddHeader(key, value);
            }

            string cookie = PlatformCookieStore.GetCookie("JD", Configurations.CookieChina.Get());

            if (!string.IsNullOrWhiteSpace(cookie))
            {
                request.AddHeader("Cookie", cookie);
            }

            RestResponse response = client.Execute(request);

            return response.ResponseUri?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetQueryValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match match = Regex.Match(value, $"(?:\\?|&|#){Regex.Escape(name)}=([^&#]+)", RegexOptions.IgnoreCase);

        return match.Success ? WebUtility.UrlDecode(match.Groups[1].Value) : null;
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["origin"] = "https://lives.jd.com",
            ["referer"] = "https://lives.jd.com/",
            ["User-Agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
            ["x-referer-page"] = "https://lives.jd.com/",
        };
    }

    [GeneratedRegex("#/(.*?)\\?origin", RegexOptions.IgnoreCase)]
    private static partial Regex LiveIdFragmentRegex { get; }
}

public sealed class JdSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
