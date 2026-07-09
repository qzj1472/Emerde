using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Web;

namespace Emerde.Core;

public sealed class BaiduSpider : ISpider
{
    private const string DeviceId = "h5-683e85bdf741bf2492586f7ca39bf465";

    public static Lazy<BaiduSpider> Instance { get; } = new(() => new BaiduSpider());

    public string PlatformName => "Baidu";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "live.baidu.com")
        {
            return null;
        }

        string roomId = HttpUtility.ParseQueryString(uri.Query)["room_id"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomId) || !roomId.All(char.IsDigit))
        {
            return null;
        }

        return $"https://live.baidu.com/m/media/pclive/pchome/live.html?room_id={roomId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        BaiduSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomId = HttpUtility.ParseQueryString(new Uri(roomUrl).Query)["room_id"] ?? string.Empty;
        result.RoomId = roomId;

        string? json = RequestRoomInfo(roomId);
        ExtractRoomInfo(json, result);

        return result;
    }

    internal static void ExtractRoomInfo(string? json, BaiduSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? dataContainer = root["data"] as JObject;
            JObject? data = dataContainer?.Properties().Select(property => property.Value).OfType<JObject>().FirstOrDefault();

            if (data == null)
            {
                return;
            }

            JObject? host = data["host"] as JObject;
            result.Nickname = host?["name"]?.ToString();
            result.AvatarThumbUrl = host?["avatar"]?.ToString()
                ?? host?["avatar_url"]?.ToString();
            result.IsLiveStreaming = data["status"]?.ToString() == "0";

            if (result.IsLiveStreaming != true)
            {
                return;
            }

            JObject? video = data["video"] as JObject;
            ExtractVideoUrls(video, result);

            if (string.IsNullOrWhiteSpace(result.FlvUrl) && string.IsNullOrWhiteSpace(result.HlsUrl))
            {
                result.IsLiveStreaming = false;
            }
        }
        catch
        {
        }
    }

    private static void ExtractVideoUrls(JObject? video, BaiduSpiderResult result)
    {
        JArray? clarityList = video?["url_clarity_list"] as JArray;

        foreach (JObject item in clarityList?.OfType<JObject>() ?? [])
        {
            JObject? urls = item["urls"] as JObject;
            string? flvUrl = urls?["flv"]?.ToString();
            string? hlsUrl = urls?["hls"]?.ToString();

            SetStreamUrls(flvUrl, hlsUrl, result);

            if (!string.IsNullOrWhiteSpace(result.FlvUrl) || !string.IsNullOrWhiteSpace(result.HlsUrl))
            {
                return;
            }
        }

        JArray? urlList = video?["url_list"] as JArray;

        foreach (JObject item in urlList?.OfType<JObject>() ?? [])
        {
            JArray? urls = item["urls"] as JArray;
            JObject? firstUrl = urls?.OfType<JObject>().FirstOrDefault();

            SetStreamUrls(firstUrl?["flv"]?.ToString(), firstUrl?["hls"]?.ToString(), result);

            if (!string.IsNullOrWhiteSpace(result.FlvUrl) || !string.IsNullOrWhiteSpace(result.HlsUrl))
            {
                return;
            }
        }
    }

    private static void SetStreamUrls(string? flvUrl, string? hlsUrl, BaiduSpiderResult result)
    {
        if (!string.IsNullOrWhiteSpace(flvUrl))
        {
            result.FlvUrl = flvUrl;
        }

        if (!string.IsNullOrWhiteSpace(hlsUrl))
        {
            result.HlsUrl = hlsUrl;
        }

        if (string.IsNullOrWhiteSpace(result.HlsUrl) && !string.IsNullOrWhiteSpace(flvUrl))
        {
            result.HlsUrl = BuildHlsUrl(flvUrl);
        }
    }

    private static string? BuildHlsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string path = url.Split('?')[0];
        string name = path.Split('/').LastOrDefault() ?? string.Empty;
        string streamId = Path.GetFileNameWithoutExtension(name);

        return string.IsNullOrWhiteSpace(streamId)
            ? null
            : $"https://hls.liveshow.bdstatic.com/live/{streamId}.m3u8";
    }

    private static string? RequestRoomInfo(string roomId)
    {
        RestClientOptions options = new()
        {
            BaseUrl = new Uri("https://mbd.baidu.com/searchbox"),
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

        string data = new JObject
        {
            ["data"] = new JObject
            {
                ["room_id"] = roomId,
                ["device_id"] = DeviceId,
                ["source_type"] = 0,
                ["osname"] = "baiduboxapp",
            },
            ["replay_slice"] = 0,
            ["nid"] = string.Empty,
            ["schemeParams"] = new JObject
            {
                ["src_pre"] = "pc",
                ["src_suf"] = "other",
                ["bd_vid"] = string.Empty,
                ["share_uid"] = string.Empty,
                ["share_cuk"] = string.Empty,
                ["share_ecid"] = string.Empty,
                ["zb_tag"] = string.Empty,
                ["shareTaskInfo"] = new JObject
                {
                    ["room_id"] = roomId,
                }.ToString(Newtonsoft.Json.Formatting.None),
                ["share_from"] = string.Empty,
                ["ext_params"] = string.Empty,
                ["nid"] = string.Empty,
            },
        }.ToString(Newtonsoft.Json.Formatting.None);

        string cookie = PlatformCookieStore.GetCookie("Baidu", Configurations.CookieChina.Get());

        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6");
        request.AddHeader("Connection", "keep-alive");
        request.AddHeader("Referer", "https://live.baidu.com/");
        request.AddHeader("User-Agent", "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        request.AddQueryParameter("cmd", "371");
        request.AddQueryParameter("action", "star");
        request.AddQueryParameter("service", "bdbox");
        request.AddQueryParameter("osname", "baiduboxapp");
        request.AddQueryParameter("data", data);
        request.AddQueryParameter("ua", "360_740_ANDROID_0");
        request.AddQueryParameter("bd_vid", string.Empty);
        request.AddQueryParameter("uid", DeviceId);
        request.AddQueryParameter("_", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }
}

public sealed class BaiduSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? RoomId { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
