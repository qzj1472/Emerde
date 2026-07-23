using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class TaobaoSpider : ISpider
{
    private const string AppKey = "12574478";

    public static Lazy<TaobaoSpider> Instance { get; } = new(() => new TaobaoSpider());

    public string PlatformName => "Taobao";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "tbzb.taobao.com" && uri.Host != "huodong.m.taobao.com" && !uri.Host.EndsWith("tb.cn", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? liveId = GetQueryValue(uri.Query, "liveId") ?? GetQueryValue(uri.Query, "id");

        if (!string.IsNullOrWhiteSpace(liveId))
        {
            return $"https://tbzb.taobao.com/live?liveId={Uri.EscapeDataString(liveId)}";
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        TaobaoSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string? liveId = GetQueryValue(uri.Query, "liveId");

        if (string.IsNullOrWhiteSpace(liveId))
        {
            string? html = SpiderRequest.Get(roomUrl, Headers(), PlatformCookieStore.GetCookie("Taobao", SecretProtector.GetChinaCookie()));
            liveId = ExtractRedirectLiveId(html);
        }

        if (string.IsNullOrWhiteSpace(liveId))
        {
            return result;
        }

        string cookie = PlatformCookieStore.GetCookie("Taobao", SecretProtector.GetChinaCookie());
        string? token = ExtractCookieValue(cookie, "_m_h5_tk")?.Split('_').FirstOrDefault();

        if (string.IsNullOrWhiteSpace(token))
        {
            return result;
        }

        string data = "{\"liveId\":\"" + liveId + "\",\"creatorId\":null}";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string sign = Md5($"{token}&{timestamp}&{AppKey}&{data}");
        string api = BuildApiUrl(timestamp, sign, data);
        string? jsonp = RequestApi(api, cookie);
        ExtractLiveDetail(jsonp, result);

        return result;
    }

    internal static string? ExtractRedirectLiveId(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        Match match = RedirectUrlRegex.Match(html);

        return match.Success ? GetQueryValue(match.Groups[1].Value, "id") ?? GetQueryValue(match.Groups[1].Value, "liveId") : null;
    }

    internal static void ExtractLiveDetail(string? jsonp, TaobaoSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(jsonp))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(UnwrapJsonp(jsonp));
            result.Nickname = root["data"]?["broadCaster"]?["accountName"]?.ToString();
            result.IsLiveStreaming = root["data"]?["streamStatus"]?.ToString() == "1";

            if (result.IsLiveStreaming == true)
            {
                JObject? best = root["data"]?["liveUrlList"]?
                    .OfType<JObject>()
                    .OrderByDescending(GetDefinitionPriority)
                    .FirstOrDefault();

                result.HlsUrl = best?["hlsUrl"]?.ToString();
                result.FlvUrl = best?["flvUrl"]?.ToString();
            }
        }
        catch
        {
        }
    }

    internal static string Md5(string value)
    {
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static int GetDefinitionPriority(JObject item)
    {
        string? definition = item["definition"]?.ToString() ?? item["newDefinition"]?.ToString();

        return definition switch
        {
            "ud" => 4,
            "hd" => 3,
            "md" => 2,
            "ld" => 1,
            "lld" => 0,
            _ => -1,
        };
    }

    private static string BuildApiUrl(long timestamp, string sign, string data)
    {
        Dictionary<string, string> parameters = new()
        {
            ["jsv"] = "2.7.0",
            ["appKey"] = AppKey,
            ["t"] = timestamp.ToString(),
            ["sign"] = sign,
            ["AntiFlood"] = "true",
            ["AntiCreep"] = "true",
            ["api"] = "mtop.mediaplatform.live.livedetail",
            ["v"] = "4.0",
            ["preventFallback"] = "true",
            ["type"] = "jsonp",
            ["dataType"] = "jsonp",
            ["callback"] = "mtopjsonp1",
            ["data"] = data,
        };
        string query = string.Join("&", parameters.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));

        return $"https://h5api.m.taobao.com/h5/mtop.mediaplatform.live.livedetail/4.0/?{query}";
    }

    private static string? RequestApi(string url, string cookie)
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

        using RestClient client = new(options);
        RestRequest request = new()
        {
            Method = Method.Get,
            Timeout = TimeSpan.FromSeconds(20),
        };

        foreach ((string key, string value) in Headers())
        {
            request.AddHeader(key, value);
        }

        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    private static string UnwrapJsonp(string jsonp)
    {
        Match match = JsonpRegex.Match(jsonp);

        return match.Success ? match.Groups[1].Value : jsonp;
    }

    private static string? ExtractCookieValue(string cookie, string name)
    {
        Match match = Regex.Match(cookie, $"(?:^|;\\s*){Regex.Escape(name)}=([^;]+)", RegexOptions.IgnoreCase);

        return match.Success ? WebUtility.UrlDecode(match.Groups[1].Value) : null;
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
            ["Referer"] = "https://huodong.m.taobao.com/",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0",
        };
    }

    [GeneratedRegex("var url = '(.*?)';", RegexOptions.Singleline)]
    private static partial Regex RedirectUrlRegex { get; }

    [GeneratedRegex("^[^(]+\\((.*)\\)\\s*;?$", RegexOptions.Singleline)]
    private static partial Regex JsonpRegex { get; }
}

public sealed class TaobaoSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
