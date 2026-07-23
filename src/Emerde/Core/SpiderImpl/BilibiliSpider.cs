using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;

namespace Emerde.Core;

public sealed class BilibiliSpider : ISpider, IQualitySelectableSpider
{
    public static Lazy<BilibiliSpider> Instance { get; } = new(() => new BilibiliSpider());

    public string PlatformName => "Bilibili";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "live.bilibili.com")
        {
            return null;
        }

        string roomId = uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomId) || !roomId.All(char.IsDigit))
        {
            return null;
        }

        return $"https://live.bilibili.com/{roomId}";
    }

    public ISpiderResult GetResult(string url)
    {
        return GetResult(url, StreamQualityCatalog.Original);
    }

    public ISpiderResult GetResult(string url, string? preferredQuality)
    {
        string? roomUrl = ParseUrl(url);
        BilibiliSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomId = roomUrl.Split('/').Last();
        string? roomInfoJson = RequestUrl($"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId}", roomUrl);
        ExtractRoomInfo(roomInfoJson, result);

        if (string.IsNullOrWhiteSpace(result.Nickname) && !string.IsNullOrWhiteSpace(result.Uid))
        {
            string? masterJson = RequestUrl($"https://api.live.bilibili.com/live_user/v1/Master/info?uid={result.Uid}", roomUrl);
            ExtractMasterInfo(masterJson, result);
        }

        if (result.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(result.RoomId))
        {
            int qualityNumber = StreamQualityCatalog.GetBilibiliQualityNumber(preferredQuality);
            string? playJson = RequestUrl($"https://api.live.bilibili.com/room/v1/Room/playUrl?cid={result.RoomId}&qn={qualityNumber}&platform=web", roomUrl);
            ExtractPlayUrl(playJson, result, qualityNumber.ToString());
        }

        return result;
    }

    internal static void ExtractRoomInfo(string? json, BilibiliSpiderResult result)
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

            result.RoomId = data["room_id"]?.ToString();
            result.Uid = data["uid"]?.ToString();
            result.IsLiveStreaming = data["live_status"]?.Value<int>() == 1;

            if (!string.IsNullOrWhiteSpace(result.RoomId))
            {
                result.RoomUrl = $"https://live.bilibili.com/{result.RoomId}";
            }
        }
        catch
        {
        }
    }

    internal static void ExtractMasterInfo(string? json, BilibiliSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? data = root["data"] as JObject;
            JObject? info = data?["info"] as JObject;

            if (info == null)
            {
                return;
            }

            result.Nickname = info["uname"]?.ToString();
            result.AvatarThumbUrl = info["face"]?.ToString();
        }
        catch
        {
        }
    }

    internal static void ExtractPlayUrl(string? json, BilibiliSpiderResult result, string? quality = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JToken? data = root["data"];
            JArray? durl = data?["durl"] as JArray;

            if (durl == null || durl.Count == 0)
            {
                return;
            }

            string? url = durl
                .Select(item => item["url"]?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .LastOrDefault();

            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                result.HlsUrl = url;
            }
            else
            {
                result.FlvUrl = url;
            }
            result.Quality = data?["current_qn"]?.ToString() ?? quality;
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
                options.Proxy = ProxyAddress.Create(proxyUrl);
            }
        }

        using RestClient client = new(options);
        RestRequest request = new()
        {
            Method = Method.Get,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = PlatformCookieStore.GetCookie("Bilibili", SecretProtector.GetChinaCookie());

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:127.0) Gecko/20100101 Firefox/127.0");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2");
        request.AddHeader("Origin", "https://live.bilibili.com");
        request.AddHeader("Referer", referer);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }
}

public sealed class BilibiliSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? RoomId { get; set; }

    public string? Uid { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }

    public string? Quality { get; set; }
}
