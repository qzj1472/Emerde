using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class XiaohongshuSpider : ISpider
{
    public static Lazy<XiaohongshuSpider> Instance { get; } = new(() => new XiaohongshuSpider());

    public string PlatformName => "Xiaohongshu";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host == "xhslink.com")
        {
            return uri.GetLeftPart(UriPartial.Path);
        }

        if (uri.Host != "www.xiaohongshu.com")
        {
            return null;
        }

        string? userId = ExtractUserId(uri);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return $"https://www.xiaohongshu.com/user/profile/{userId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        XiaohongshuSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string requestUrl = roomUrl;

        if (IsShortLink(roomUrl))
        {
            string? redirectUrl = RequestRedirectUrl(roomUrl);
            string? parsedRedirectUrl = string.IsNullOrWhiteSpace(redirectUrl) ? null : ParseUrl(redirectUrl);

            if (!string.IsNullOrWhiteSpace(parsedRedirectUrl))
            {
                requestUrl = parsedRedirectUrl;
                result.RoomUrl = parsedRedirectUrl;
            }
        }

        string? html = RequestUrl(requestUrl);
        ExtractData(html, result);

        return result;
    }

    internal static void ExtractData(string? html, XiaohongshuSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        Match initialStateMatch = InitialStateRegex.Match(html);

        if (initialStateMatch.Success)
        {
            ExtractInitialState(initialStateMatch.Groups[1].Value.Replace("undefined", "null"), result);
        }

        if (string.IsNullOrWhiteSpace(result.Nickname))
        {
            Match titleMatch = ProfileTitleRegex.Match(WebUtility.HtmlDecode(html));

            if (titleMatch.Success)
            {
                result.Nickname = titleMatch.Groups[1].Value.Trim();
            }
        }
    }

    internal static void ExtractInitialState(string? json, XiaohongshuSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? liveStream = root["liveStream"] as JObject;

            if (liveStream == null)
            {
                return;
            }

            result.IsLiveStreaming = false;

            if (liveStream["liveStatus"]?.ToString() != "success")
            {
                return;
            }

            JObject? roomInfo = liveStream["roomData"]?["roomInfo"] as JObject;
            string? title = roomInfo?["roomTitle"]?.ToString();

            if (string.IsNullOrWhiteSpace(title) || title.Contains("\u56de\u653e", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string? deeplink = roomInfo?["deeplink"]?.ToString();
            string? flvUrl = GetQueryValue(deeplink, "flvUrl");
            string? liveId = ExtractLiveId(flvUrl);

            if (string.IsNullOrWhiteSpace(liveId))
            {
                return;
            }

            result.Nickname = GetQueryValue(deeplink, "host_nickname");
            result.FlvUrl = $"http://live-source-play.xhscdn.com/live/{liveId}.flv";
            result.HlsUrl = $"http://live-source-play.xhscdn.com/live/{liveId}.m3u8";
            result.IsLiveStreaming = true;
        }
        catch
        {
        }
    }

    private static string? ExtractUserId(Uri uri)
    {
        string[] segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length >= 3 && segments[0] == "user" && segments[1] == "profile")
        {
            return segments[2];
        }

        return GetQueryValue(uri.Query, "host_id");
    }

    private static string? ExtractLiveId(string? flvUrl)
    {
        if (string.IsNullOrWhiteSpace(flvUrl))
        {
            return null;
        }

        Match match = LiveIdRegex.Match(flvUrl);

        return match.Success ? match.Groups[1].Value : null;
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

    private static bool IsShortLink(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Host == "xhslink.com";
    }

    private static string? RequestRedirectUrl(string url)
    {
        RestResponse response = ExecuteRequest(url);

        return response.ResponseUri?.AbsoluteUri;
    }

    private static string? RequestUrl(string url)
    {
        RestResponse response = ExecuteRequest(url);

        return response.IsSuccessful ? response.Content : null;
    }

    private static RestResponse ExecuteRequest(string url)
    {
        RestClientOptions options = new()
        {
            BaseUrl = new Uri(url),
            FollowRedirects = true,
        };

        if (Configurations.IsUseProxy.Get())
        {
            string proxyUrl = Configurations.ProxyUrl.Get();

            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                options.Proxy = ProxyAddress.Create(proxyUrl);
            }
        }

        using RestClient client = new(options);
        RestRequest request = new()
        {
            Method = Method.Get,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = PlatformCookieStore.GetCookie("Xiaohongshu", SecretProtector.GetChinaCookie());

        request.AddHeader("User-Agent", "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))");
        request.AddHeader("xy-common-params", "platform=iOS&sid=session.1722166379345546829388");
        request.AddHeader("Referer", "https://app.xhs.cn/");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        return client.Execute(request);
    }

    [GeneratedRegex("<script>window.__INITIAL_STATE__=(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex InitialStateRegex { get; }

    [GeneratedRegex("<title>@(.*?)(?:\\s|</title>)", RegexOptions.Singleline)]
    private static partial Regex ProfileTitleRegex { get; }

    [GeneratedRegex("/live/([^/?#]+)\\.flv", RegexOptions.IgnoreCase)]
    private static partial Regex LiveIdRegex { get; }
}

public sealed class XiaohongshuSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
