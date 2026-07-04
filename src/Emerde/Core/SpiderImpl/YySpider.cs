using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class YySpider : ISpider
{
    public static Lazy<YySpider> Instance { get; } = new(() => new YySpider());

    public string PlatformName => "YY";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.yy.com")
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

        return $"https://www.yy.com/{string.Join('/', segments)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        YySpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string? html = RequestUrl(roomUrl, Method.Get, null);
        ExtractPageData(html, result);

        if (!string.IsNullOrWhiteSpace(result.ChannelId))
        {
            string? streamJson = RequestUrl(BuildStreamApiUrl(result.ChannelId), Method.Post, BuildStreamRequestBody(result.ChannelId));
            ExtractStreamInfo(streamJson, result);
        }

        return result;
    }

    internal static void ExtractPageData(string? html, YySpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        Match nicknameMatch = NicknameRegex.Match(html);
        Match channelMatch = ChannelIdRegex.Match(html);

        if (nicknameMatch.Success)
        {
            result.Nickname = WebUtility.HtmlDecode(nicknameMatch.Groups[1].Value);
        }

        if (channelMatch.Success)
        {
            result.ChannelId = channelMatch.Groups[1].Value;
        }
    }

    internal static void ExtractStreamInfo(string? json, YySpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? streamLineAddr = root["avp_info_res"]?["stream_line_addr"] as JObject;

            if (streamLineAddr == null || !streamLineAddr.HasValues)
            {
                result.IsLiveStreaming = false;
                return;
            }

            string? flvUrl = streamLineAddr
                .Properties()
                .Select(property => property.Value["cdn_info"]?["url"]?.ToString())
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

            if (string.IsNullOrWhiteSpace(flvUrl))
            {
                result.IsLiveStreaming = false;
                return;
            }

            result.IsLiveStreaming = true;
            result.FlvUrl = flvUrl;
        }
        catch
        {
        }
    }

    private static string BuildStreamApiUrl(string channelId)
    {
        string sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        return "https://stream-manager.yy.com/v3/channel/streams"
             + "?uid=0"
             + $"&cid={Uri.EscapeDataString(channelId)}"
             + $"&sid={Uri.EscapeDataString(channelId)}"
             + "&appid=0"
             + $"&sequence={sequence}"
             + "&encode=json";
    }

    private static string BuildStreamRequestBody(string channelId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        JObject body = new()
        {
            ["head"] = new JObject
            {
                ["seq"] = sequence,
                ["appidstr"] = "0",
                ["bidstr"] = "121",
                ["cidstr"] = channelId,
                ["sidstr"] = channelId,
                ["uid64"] = 0,
                ["client_type"] = 108,
                ["client_ver"] = "5.17.0",
                ["stream_sys_ver"] = 1,
                ["app"] = "yylive_web",
                ["playersdk_ver"] = "5.17.0",
                ["thundersdk_ver"] = "0",
                ["streamsdk_ver"] = "5.17.0",
            },
            ["client_attribute"] = new JObject
            {
                ["client"] = "web",
                ["model"] = "web0",
                ["cpu"] = string.Empty,
                ["graphics_card"] = string.Empty,
                ["os"] = "chrome",
                ["osversion"] = "0",
                ["vsdk_version"] = string.Empty,
                ["app_identify"] = string.Empty,
                ["app_version"] = string.Empty,
                ["business"] = string.Empty,
                ["width"] = "1920",
                ["height"] = "1080",
                ["scale"] = string.Empty,
                ["client_type"] = 8,
                ["h265"] = 0,
            },
            ["avp_parameter"] = new JObject
            {
                ["version"] = 1,
                ["client_type"] = 8,
                ["service_type"] = 0,
                ["imsi"] = 0,
                ["send_time"] = now,
                ["line_seq"] = -1,
                ["gear"] = 4,
                ["ssl"] = 1,
                ["stream_format"] = 0,
            },
        };

        return body.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string? RequestUrl(string url, Method method, string? body)
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
            Method = method,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = Configurations.CookieChina.Get();

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36 Edg/121.0.0.0");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2");
        request.AddHeader("Referer", "https://www.yy.com/");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            request.AddStringBody(body, DataFormat.Json);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    [GeneratedRegex("nick:\\s*\"(.*?)\",\\s*logo", RegexOptions.Singleline)]
    private static partial Regex NicknameRegex { get; }

    [GeneratedRegex("sid\\s*:\\s*\"(.*?)\",\\s*ssid", RegexOptions.Singleline)]
    private static partial Regex ChannelIdRegex { get; }
}

public sealed class YySpiderResult : ISpiderResult
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
