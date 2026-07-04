using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class NeteaseCcSpider : ISpider
{
    public static Lazy<NeteaseCcSpider> Instance { get; } = new(() => new NeteaseCcSpider());

    public string PlatformName => "NeteaseCC";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "cc.163.com")
        {
            return null;
        }

        string[] segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length == 0)
        {
            return null;
        }

        return $"https://cc.163.com/{string.Join('/', segments)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        NeteaseCcSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string requestUrl = roomUrl.EndsWith('/') ? roomUrl : $"{roomUrl}/";
        string? html = RequestUrl(requestUrl);
        ExtractData(html, result);

        return result;
    }

    internal static void ExtractData(string? html, NeteaseCcSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        Match match = NextDataRegex.Match(html);

        if (!match.Success)
        {
            return;
        }

        ExtractNextData(WebUtility.HtmlDecode(match.Groups[1].Value), result);
    }

    internal static void ExtractNextData(string? json, NeteaseCcSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? roomData = root["props"]?["pageProps"]?["roomInfoInitData"] as JObject;
            JObject? liveData = roomData?["live"] as JObject;

            if (roomData == null || liveData == null)
            {
                return;
            }

            result.Nickname = liveData["nickname"]?.ToString();

            if (string.IsNullOrWhiteSpace(result.Nickname))
            {
                result.Nickname = roomData["nickname"]?.ToString();
            }

            result.IsLiveStreaming = liveData["status"]?.Value<int>() == 1;

            if (result.IsLiveStreaming != true)
            {
                return;
            }

            result.HlsUrl = liveData["sharefile"]?.ToString();
            result.FlvUrl = ExtractFlvUrl(liveData["quickplay"] as JObject);
        }
        catch
        {
        }
    }

    private static string? ExtractFlvUrl(JObject? quickplay)
    {
        JObject? resolution = quickplay?["resolution"] as JObject;

        if (resolution == null)
        {
            return null;
        }

        string[] order = ["blueray", "ultra", "high", "standard"];

        foreach (string quality in order)
        {
            JObject? qualityNode = resolution[quality] as JObject;
            JObject? cdn = qualityNode?["cdn"] as JObject;
            string? flvUrl = cdn?
                .Properties()
                .Select(property => property.Value?.ToString())
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

            if (!string.IsNullOrWhiteSpace(flvUrl))
            {
                return flvUrl;
            }
        }

        return null;
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
                options.Proxy = new WebProxy($"http://{proxyUrl}");
            }
        }

        RestClient client = new(options);
        RestRequest request = new()
        {
            Method = Method.Get,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = Configurations.CookieChina.Get();

        request.AddHeader("Accept", "application/json, text/plain, */*");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6");
        request.AddHeader("Referer", "https://cc.163.com/");
        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    [GeneratedRegex("<script id=\"__NEXT_DATA__\"[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex NextDataRegex { get; }
}

public sealed class NeteaseCcSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
