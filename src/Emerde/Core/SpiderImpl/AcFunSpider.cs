using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;

namespace Emerde.Core;

public sealed class AcFunSpider : ISpider
{
    public static Lazy<AcFunSpider> Instance { get; } = new(() => new AcFunSpider());

    public string PlatformName => "AcFun";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "live.acfun.cn" && uri.Host != "m.acfun.cn")
        {
            return null;
        }

        string? authorId = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(authorId) || !authorId.All(char.IsDigit))
        {
            return null;
        }

        return $"https://live.acfun.cn/live/{authorId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        AcFunSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string authorId = roomUrl.Split('/').Last();
        string? userInfoJson = RequestUrl($"https://live.acfun.cn/rest/pc-direct/user/userInfo?userId={authorId}", Method.Get, null, null);
        ExtractUserInfo(userInfoJson, result);

        if (result.IsLiveStreaming == true)
        {
            AcFunVisitorSign? visitorSign = RequestVisitorSign();

            if (visitorSign != null)
            {
                string? playJson = RequestStartPlay(authorId, visitorSign);
                ExtractStartPlay(playJson, result);
            }
        }

        return result;
    }

    internal static void ExtractUserInfo(string? json, AcFunSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? profile = root["profile"] as JObject;

            if (profile == null)
            {
                return;
            }

            result.Nickname = profile["name"]?.ToString();
            result.IsLiveStreaming = profile["liveId"] != null;
        }
        catch
        {
        }
    }

    internal static void ExtractStartPlay(string? json, AcFunSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            string? videoPlayRes = root["data"]?["videoPlayRes"]?.ToString();

            if (string.IsNullOrWhiteSpace(videoPlayRes))
            {
                return;
            }

            JObject playRoot = JObject.Parse(videoPlayRes);
            JToken? adaptationSet = playRoot["liveAdaptiveManifest"]?.FirstOrDefault()?["adaptationSet"];
            JArray? representation = adaptationSet switch
            {
                JObject obj => obj["representation"] as JArray,
                JArray array => array.FirstOrDefault()?["representation"] as JArray,
                _ => null,
            };

            if (representation == null || representation.Count == 0)
            {
                return;
            }

            string? flvUrl = representation
                .OfType<JObject>()
                .OrderByDescending(item => item["bitrate"]?.Value<int>() ?? 0)
                .Select(item => item["url"]?.ToString())
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

            if (!string.IsNullOrWhiteSpace(flvUrl))
            {
                result.FlvUrl = flvUrl;
                result.IsLiveStreaming = true;
            }
        }
        catch
        {
        }
    }

    internal static AcFunVisitorSign? ExtractVisitorSign(string? json, string did)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(json);
            string? userId = root["userId"]?.ToString();
            string? visitorSt = root["acfun.api.visitor_st"]?.ToString();

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(visitorSt))
            {
                return null;
            }

            return new AcFunVisitorSign(userId, did, visitorSt);
        }
        catch
        {
            return null;
        }
    }

    private static AcFunVisitorSign? RequestVisitorSign()
    {
        string did = $"web_{Guid.NewGuid().ToString("N")[..16]}";
        string? json = RequestUrl(
            "https://id.app.acfun.cn/rest/app/visitor/login",
            Method.Post,
            [new KeyValuePair<string, string>("sid", "acfun.api.visitor")],
            $"_did={did};");

        return ExtractVisitorSign(json, did);
    }

    private static string? RequestStartPlay(string authorId, AcFunVisitorSign visitorSign)
    {
        string url = "https://api.kuaishouzt.com/rest/zt/live/web/startPlay"
                  + "?subBiz=mainApp"
                  + "&kpn=ACFUN_APP"
                  + "&kpf=PC_WEB"
                  + $"&userId={Uri.EscapeDataString(visitorSign.UserId)}"
                  + $"&did={Uri.EscapeDataString(visitorSign.Did)}"
                  + $"&acfun.api.visitor_st={Uri.EscapeDataString(visitorSign.VisitorSt)}";

        return RequestUrl(
            url,
            Method.Post,
            [
                new KeyValuePair<string, string>("authorId", authorId),
                new KeyValuePair<string, string>("pullStreamType", "FLV"),
            ],
            null);
    }

    private static string? RequestUrl(string url, Method method, IReadOnlyList<KeyValuePair<string, string>>? parameters, string? cookie)
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
            Method = method,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string configuredCookie = PlatformCookieStore.GetCookie("AcFun", Configurations.CookieChina.Get());

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0");
        request.AddHeader("Referer", "https://live.acfun.cn/");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }
        else if (!string.IsNullOrWhiteSpace(configuredCookie))
        {
            request.AddHeader("Cookie", configuredCookie);
        }

        if (parameters != null)
        {
            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                request.AddParameter(parameter.Key, parameter.Value);
            }
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }
}

public sealed record AcFunVisitorSign(string UserId, string Did, string VisitorSt);

public sealed class AcFunSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
