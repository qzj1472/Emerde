using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class KuaishouSpider : ISpider, IQualitySelectableSpider
{
    public static Lazy<KuaishouSpider> Instance { get; } = new(() => new KuaishouSpider());

    public string PlatformName => "Kuaishou";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "live.kuaishou.com")
        {
            return null;
        }

        string[] segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length < 2 || segments[0] != "u")
        {
            return null;
        }

        return $"https://live.kuaishou.com/u/{segments[1]}";
    }

    public ISpiderResult GetResult(string url)
    {
        return GetResult(url, StreamQualityCatalog.Original);
    }

    public ISpiderResult GetResult(string url, string? preferredQuality)
    {
        string? roomUrl = ParseUrl(url);
        KuaishouSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string? html = RequestUrl(roomUrl);
        ExtractData(html, result, preferredQuality);

        return result;
    }

    internal static void ExtractData(string? html, KuaishouSpiderResult result, string? preferredQuality = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        Match match = InitialStateRegex.Match(html);

        if (!match.Success)
        {
            return;
        }

        ExtractInitialState(match.Groups[1].Value, result, preferredQuality);
    }

    internal static void ExtractInitialState(string? json, KuaishouSpiderResult result, string? preferredQuality = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? liveNode = FindLiveNode(root);

            if (liveNode == null)
            {
                result.IsLiveStreaming = false;
                return;
            }

            JObject? author = liveNode["author"] as JObject;
            JObject? liveStream = liveNode["liveStream"] as JObject;

            result.Nickname = author?["name"]?.ToString();

            if (liveStream == null || !liveStream.HasValues)
            {
                result.IsLiveStreaming = false;
                return;
            }

            JArray? representation = liveStream["playUrls"]?["h264"]?["adaptationSet"]?["representation"] as JArray;

            if (representation == null || representation.Count == 0)
            {
                result.IsLiveStreaming = false;
                return;
            }

            JObject[] variants = representation
                .OfType<JObject>()
                .OrderByDescending(item => item["bitrate"]?.Value<int>() ?? 0)
                .Where(item => !string.IsNullOrWhiteSpace(item["url"]?.ToString()))
                .ToArray();

            if (variants.Length == 0)
            {
                result.IsLiveStreaming = false;
                return;
            }

            JObject selected = variants[StreamQualityCatalog.GetVariantIndex(preferredQuality, variants.Length)];
            string? flvUrl = selected["url"]?.ToString();

            if (string.IsNullOrWhiteSpace(flvUrl))
            {
                result.IsLiveStreaming = false;
                return;
            }

            result.IsLiveStreaming = true;
            result.FlvUrl = flvUrl;
            result.Quality = StreamQualityCatalog.NormalizePreference(preferredQuality);
            int? width = selected["width"]?.Value<int?>();
            int? height = selected["height"]?.Value<int?>();
            if (width > 0 && height > 0)
            {
                result.Resolution = $"{width}x{height}";
            }
            result.Bitrate = StreamQualityCatalog.FormatBitrate(selected["bitrate"]?.Value<double?>());
        }
        catch
        {
        }
    }

    private static JObject? FindLiveNode(JToken token)
    {
        if (token is JObject obj
         && obj["liveStream"] != null
         && obj["author"] != null)
        {
            return obj;
        }

        foreach (JToken child in token.Children())
        {
            JObject? found = FindLiveNode(child);

            if (found != null)
            {
                return found;
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

        string cookie = PlatformCookieStore.GetCookie("Kuaishou", Configurations.CookieChina.Get());

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2");
        request.AddHeader("Referer", "https://live.kuaishou.com/");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    [GeneratedRegex("<script>window.__INITIAL_STATE__=(.*?);\\(function\\(\\)\\{var s;", RegexOptions.Singleline)]
    private static partial Regex InitialStateRegex { get; }
}

public sealed class KuaishouSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }

    public string? Quality { get; set; }

    public string? Resolution { get; set; }

    public string? Bitrate { get; set; }
}
