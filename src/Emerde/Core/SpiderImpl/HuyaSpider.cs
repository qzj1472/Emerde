using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class HuyaSpider : ISpider
{
    public static Lazy<HuyaSpider> Instance { get; } = new(() => new HuyaSpider());

    public string PlatformName => "Huya";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.huya.com" && uri.Host != "huya.com")
        {
            return null;
        }

        string roomPath = uri.Segments
            .Select(segment => segment.Trim('/'))
            .FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomPath))
        {
            return null;
        }

        return $"https://www.huya.com/{roomPath}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        HuyaSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string? roomId = roomUrl.Split('/').LastOrDefault();

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return result;
        }

        if (roomId.Any(char.IsLetter))
        {
            string? html = RequestUrl(roomUrl, Method.Get, null);
            roomId = ExtractProfileRoomId(html);
        }

        result.RoomId = roomId;

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return result;
        }

        string apiUrl = $"https://mp.huya.com/cache.php?m=Live&do=profileRoom&roomid={Uri.EscapeDataString(roomId)}&showSecret=1";
        string? json = RequestUrl(apiUrl, Method.Get, null);
        ExtractProfileRoom(json, result);

        return result;
    }

    internal static string? ExtractProfileRoomId(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        Match match = ProfileRoomRegex.Match(html);

        return match.Success ? match.Groups[1].Value : null;
    }

    internal static void ExtractProfileRoom(string? json, HuyaSpiderResult result)
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

            JObject? profileInfo = data["profileInfo"] as JObject;
            JObject? liveData = data["liveData"] as JObject;

            result.Nickname = profileInfo?["nick"]?.ToString();
            result.AvatarThumbUrl = profileInfo?["avatar180"]?.ToString()
                ?? profileInfo?["avatar"]?.ToString();
            result.Title = liveData?["introduction"]?.ToString();
            result.IsLiveStreaming = string.Equals(data["realLiveStatus"]?.ToString(), "ON", StringComparison.OrdinalIgnoreCase);

            if (result.IsLiveStreaming != true)
            {
                return;
            }

            JArray? streamList = data["stream"]?["baseSteamInfoList"] as JArray;
            JObject? stream = SelectStream(streamList);

            if (stream == null)
            {
                result.IsLiveStreaming = false;
                return;
            }

            string? streamName = stream["sStreamName"]?.ToString();
            string? flvBaseUrl = stream["sFlvUrl"]?.ToString();
            string? flvAntiCode = stream["sFlvAntiCode"]?.ToString();
            string? hlsBaseUrl = stream["sHlsUrl"]?.ToString();
            string? hlsAntiCode = stream["sHlsAntiCode"]?.ToString();

            result.FlvUrl = BuildMediaUrl(flvBaseUrl, streamName, "flv", flvAntiCode);
            result.HlsUrl = BuildMediaUrl(hlsBaseUrl, streamName, "m3u8", hlsAntiCode);

            if (string.Equals(stream["sCdnType"]?.ToString(), "TX", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(result.FlvUrl))
            {
                result.FlvUrl = result.FlvUrl
                    .Replace("&ctype=tars_mp", "&ctype=huya_webh5", StringComparison.OrdinalIgnoreCase)
                    .Replace("&fs=bhct", "&fs=bgct", StringComparison.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(result.FlvUrl) && string.IsNullOrWhiteSpace(result.HlsUrl))
            {
                result.IsLiveStreaming = false;
            }
        }
        catch
        {
        }
    }

    private static JObject? SelectStream(JArray? streamList)
    {
        if (streamList == null || streamList.Count == 0)
        {
            return null;
        }

        string[] priority = ["TX", "HW", "HS", "AL"];

        foreach (string cdn in priority)
        {
            JObject? stream = streamList
                .OfType<JObject>()
                .FirstOrDefault(item => string.Equals(item["sCdnType"]?.ToString(), cdn, StringComparison.OrdinalIgnoreCase));

            if (stream != null)
            {
                return stream;
            }
        }

        return streamList.OfType<JObject>().FirstOrDefault();
    }

    private static string? BuildMediaUrl(string? baseUrl, string? streamName, string suffix, string? antiCode)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(streamName))
        {
            return null;
        }

        string url = $"{baseUrl.TrimEnd('/')}/{streamName}.{suffix}";

        if (!string.IsNullOrWhiteSpace(antiCode))
        {
            url += $"?{antiCode}";
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{url[7..]}";
        }

        return url;
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
                options.Proxy = ProxyAddress.Create(proxyUrl);
            }
        }

        using RestClient client = new(options);
        RestRequest request = new()
        {
            Method = method,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = PlatformCookieStore.GetCookie("Huya", SecretProtector.GetChinaCookie());

        request.AddHeader("User-Agent", "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.9");
        request.AddHeader("Referer", "https://servicewechat.com/wx74767bf0b684f7d3/301/page-frame.html");
        request.AddHeader("xweb_xhr", "1");
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

    [GeneratedRegex("\"ProfileRoom\"\\s*:\\s*(\\d+)\\s*,\\s*\"sPrivateHost", RegexOptions.Singleline)]
    private static partial Regex ProfileRoomRegex { get; }
}

public sealed class HuyaSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? RoomId { get; set; }

    public string? Title { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
