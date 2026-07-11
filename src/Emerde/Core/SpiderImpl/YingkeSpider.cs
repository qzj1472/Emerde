using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class YingkeSpider : ISpider
{
    public static Lazy<YingkeSpider> Instance { get; } = new(() => new YingkeSpider());

    public string PlatformName => "Yingke";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.inke.cn")
        {
            return null;
        }

        string? uid = GetQueryValue(uri.Query, "uid");
        string? liveId = GetQueryValue(uri.Query, "id");

        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(liveId))
        {
            return null;
        }

        return $"https://www.inke.cn/liveroom.html?uid={Uri.EscapeDataString(uid)}&id={Uri.EscapeDataString(liveId)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        YingkeSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string? uid = GetQueryValue(uri.Query, "uid");
        string? liveId = GetQueryValue(uri.Query, "id");

        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(liveId))
        {
            return result;
        }

        string? json = RequestUrl(BuildApiUrl(uid, liveId));
        ExtractShareData(json, result);

        return result;
    }

    internal static void ExtractShareData(string? json, YingkeSpiderResult result)
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

            result.Nickname = data["media_info"]?["nick"]?.ToString();
            result.IsLiveStreaming = data["status"]?.Value<int>() == 1;

            if (result.IsLiveStreaming == true)
            {
                JObject? liveAddr = data["live_addr"]?.FirstOrDefault() as JObject;
                result.HlsUrl = liveAddr?["hls_stream_addr"]?.ToString();
                result.FlvUrl = liveAddr?["stream_addr"]?.ToString();
            }
        }
        catch
        {
        }
    }

    private static string BuildApiUrl(string uid, string liveId)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        return "https://webapi.busi.inke.cn/web/live_share_pc"
             + $"?uid={Uri.EscapeDataString(uid)}"
             + $"&id={Uri.EscapeDataString(liveId)}"
             + $"&_t={timestamp}";
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

    private static string? RequestUrl(string url)
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
                options.Proxy = ProxyAddress.Create(proxyUrl);
            }
        }

        RestClient client = new(options);
        RestRequest request = new()
        {
            Method = Method.Get,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = PlatformCookieStore.GetCookie("Yingke", Configurations.CookieChina.Get());

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0");
        request.AddHeader("Referer", "https://www.inke.cn/");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }
}

public sealed class YingkeSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
