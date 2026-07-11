using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class KugouSpider : ISpider
{
    public static Lazy<KugouSpider> Instance { get; } = new(() => new KugouSpider());

    public string PlatformName => "Kugou";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "fanxing.kugou.com"
         && uri.Host != "fanxing2.kugou.com"
         && uri.Host != "mfanxing.kugou.com")
        {
            return null;
        }

        string? roomId = ExtractRoomId(uri);

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return null;
        }

        return $"https://fanxing.kugou.com/{roomId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        KugouSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomId = roomUrl.Split('/').Last();
        string? roomInfoJson = RequestUrl($"https://service2.fanxing.kugou.com/roomcen/room/web/cdn/getEnterRoomInfo?roomId={roomId}");
        ExtractRoomInfo(roomInfoJson, result);

        if (result.IsLiveStreaming == true)
        {
            string? streamJson = RequestUrl(BuildStreamApiUrl(roomId));
            ExtractStreamUrl(streamJson, result);
        }

        return result;
    }

    internal static void ExtractRoomInfo(string? json, KugouSpiderResult result)
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

            result.Nickname = data["normalRoomInfo"]?["nickName"]?.ToString();
            int? liveType = data["liveType"]?.Value<int>();
            result.IsLiveStreaming = liveType.HasValue && liveType.Value != -1;
        }
        catch
        {
        }
    }

    internal static void ExtractStreamUrl(string? json, KugouSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JArray? lines = root["data"]?["lines"] as JArray;

            if (lines == null || lines.Count == 0)
            {
                return;
            }

            string? flvUrl = lines
                .OfType<JObject>()
                .Reverse()
                .Select(item => item["streamProfiles"]?.FirstOrDefault()?["httpsFlv"]?.FirstOrDefault()?.ToString())
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

    private static string? ExtractRoomId(Uri uri)
    {
        Match queryMatch = RoomIdQueryRegex.Match(uri.Query);

        if (queryMatch.Success)
        {
            return Uri.UnescapeDataString(queryMatch.Groups[1].Value);
        }

        return uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .LastOrDefault();
    }

    private static string BuildStreamApiUrl(string roomId)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        return "https://fx1.service.kugou.com/video/pc/live/pull/mutiline/streamaddr"
             + $"?std_rid={Uri.EscapeDataString(roomId)}"
             + "&std_plat=7"
             + "&std_kid=0"
             + "&streamType=1-2-4-5-8"
             + "&ua=fx-flash"
             + "&targetLiveTypes=1-5-6"
             + "&version=1000"
             + "&supportEncryptMode=1"
             + "&appid=1010"
             + $"&_={timestamp}";
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

        string cookie = PlatformCookieStore.GetCookie("Kugou", Configurations.CookieChina.Get());

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0");
        request.AddHeader("Accept", "application/json");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2");
        request.AddHeader("Referer", "https://fanxing2.kugou.com/");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    [GeneratedRegex("(?:\\?|&)roomId=(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RoomIdQueryRegex { get; }
}

public sealed class KugouSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
