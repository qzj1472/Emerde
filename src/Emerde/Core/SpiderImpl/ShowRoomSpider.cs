using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class ShowRoomSpider : ISpider
{
    public static Lazy<ShowRoomSpider> Instance { get; } = new(() => new ShowRoomSpider());

    public string PlatformName => "ShowRoom";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.showroom-live.com")
        {
            return null;
        }

        string? roomId = GetQueryValue(uri.Query, "room_id");

        if (uri.AbsolutePath == "/room/profile" && !string.IsNullOrWhiteSpace(roomId))
        {
            return $"https://www.showroom-live.com/room/profile?room_id={Uri.EscapeDataString(roomId)}";
        }

        string[] segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length == 0)
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        ShowRoomSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string? roomId = GetQueryValue(uri.Query, "room_id");

        if (string.IsNullOrWhiteSpace(roomId))
        {
            string? html = RequestUrl(roomUrl);
            roomId = ExtractRoomIdFromHtml(html);
        }

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return result;
        }

        result.RoomUrl = $"https://www.showroom-live.com/room/profile?room_id={roomId}";

        string? liveInfoJson = RequestUrl($"https://www.showroom-live.com/api/live/live_info?room_id={Uri.EscapeDataString(roomId)}");
        ExtractLiveInfo(liveInfoJson, result);

        if (result.IsLiveStreaming == true)
        {
            string? streamJson = RequestUrl($"https://www.showroom-live.com/api/live/streaming_url?room_id={Uri.EscapeDataString(roomId)}&abr_available=1");
            ExtractStreamingUrl(streamJson, result);
        }

        return result;
    }

    internal static string? ExtractRoomIdFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        Match match = RoomProfileRegex.Match(html);

        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    internal static void ExtractLiveInfo(string? json, ShowRoomSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            result.Nickname = root["room_name"]?.ToString();
            result.IsLiveStreaming = root["live_status"]?.Value<int>() == 2;
        }
        catch
        {
        }
    }

    internal static void ExtractStreamingUrl(string? json, ShowRoomSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JArray? urls = root["streaming_url_list"] as JArray;

            if (urls == null || urls.Count == 0)
            {
                return;
            }

            string? hlsUrl = urls
                .OfType<JObject>()
                .OrderByDescending(item => item["type"]?.ToString() == "hls_all")
                .Select(item => item["url"]?.ToString())
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

            if (!string.IsNullOrWhiteSpace(hlsUrl))
            {
                result.HlsUrl = hlsUrl;
            }
        }
        catch
        {
        }
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

        string cookie = PlatformCookieStore.GetCookie("ShowRoom", Configurations.CookieOversea.Get());

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    [GeneratedRegex("href=\"/room/profile\\?room_id=([^\"&]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RoomProfileRegex { get; }
}

public sealed class ShowRoomSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
