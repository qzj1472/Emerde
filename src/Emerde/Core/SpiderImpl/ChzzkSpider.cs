using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;

namespace Emerde.Core;

public sealed class ChzzkSpider : ISpider
{
    public static Lazy<ChzzkSpider> Instance { get; } = new(() => new ChzzkSpider());

    public string PlatformName => "CHZZK";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "chzzk.naver.com" && uri.Host != "m.chzzk.naver.com")
        {
            return null;
        }

        string[] segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        int liveIndex = Array.FindIndex(segments, segment => string.Equals(segment, "live", StringComparison.OrdinalIgnoreCase));

        if (liveIndex < 0 || liveIndex + 1 >= segments.Length)
        {
            return null;
        }

        string channelId = segments[liveIndex + 1];

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return null;
        }

        return $"https://chzzk.naver.com/live/{channelId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        ChzzkSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string channelId = roomUrl.Split('/').Last();
        result.ChannelId = channelId;

        string? json = RequestUrl($"https://api.chzzk.naver.com/service/v3/channels/{Uri.EscapeDataString(channelId)}/live-detail", roomUrl);
        ExtractLiveDetail(json, result);

        return result;
    }

    internal static void ExtractLiveDetail(string? json, ChzzkSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? content = root["content"] as JObject;

            if (content == null)
            {
                return;
            }

            JObject? channel = content["channel"] as JObject;
            result.Nickname = channel?["channelName"]?.ToString();
            result.AvatarThumbUrl = channel?["channelImageUrl"]?.ToString();
            result.IsLiveStreaming = string.Equals(content["status"]?.ToString(), "OPEN", StringComparison.OrdinalIgnoreCase);

            if (result.IsLiveStreaming != true)
            {
                return;
            }

            ExtractPlaybackJson(content["livePlaybackJson"]?.ToString(), result);

            if (string.IsNullOrWhiteSpace(result.HlsUrl))
            {
                result.IsLiveStreaming = false;
            }
        }
        catch
        {
        }
    }

    internal static void ExtractPlaybackJson(string? json, ChzzkSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JArray? media = root["media"] as JArray;

            result.HlsUrl = media?
                .OfType<JObject>()
                .Select(item => item["path"]?.ToString())
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
        }
        catch
        {
        }
    }

    private static string? RequestUrl(string url, string referer)
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

        string cookie = Configurations.CookieOversea.Get();

        request.AddHeader("Accept", "application/json, text/plain, */*");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6");
        request.AddHeader("Origin", "https://chzzk.naver.com");
        request.AddHeader("Referer", referer);
        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }
}

public sealed class ChzzkSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? ChannelId { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
