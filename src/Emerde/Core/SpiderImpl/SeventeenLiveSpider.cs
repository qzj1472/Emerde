using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;

namespace Emerde.Core;

public sealed class SeventeenLiveSpider : ISpider
{
    public static Lazy<SeventeenLiveSpider> Instance { get; } = new(() => new SeventeenLiveSpider());

    public string PlatformName => "17Live";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "17.live" && uri.Host != "www.17.live")
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

        int liveIndex = Array.FindIndex(segments, segment => string.Equals(segment, "live", StringComparison.OrdinalIgnoreCase));
        string? roomId = liveIndex >= 0 && liveIndex + 1 < segments.Length
            ? segments[liveIndex + 1]
            : segments.LastOrDefault(segment => segment.All(char.IsDigit));

        if (string.IsNullOrWhiteSpace(roomId) || !roomId.All(char.IsDigit))
        {
            return null;
        }

        return $"https://17.live/en/live/{roomId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        SeventeenLiveSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomId = roomUrl.Split('/').Last();
        result.RoomId = roomId;

        string? roomJson = RequestUrl($"https://wap-api.17app.co/api/v1/user/room/{Uri.EscapeDataString(roomId)}", Method.Get, null);
        ExtractRoomInfo(roomJson, result);

        string aliveBody = new JObject
        {
            ["liveStreamID"] = roomId,
        }.ToString(Newtonsoft.Json.Formatting.None);

        string? aliveJson = RequestUrl($"https://wap-api.17app.co/api/v1/lives/{Uri.EscapeDataString(roomId)}/viewers/alive", Method.Post, aliveBody);
        ExtractAliveInfo(aliveJson, result);

        return result;
    }

    internal static void ExtractRoomInfo(string? json, SeventeenLiveSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);

            result.Nickname = FirstString(root, "displayName", "name", "nickname");
            result.AvatarThumbUrl = FirstString(root, "profilePic", "picture", "avatar");
        }
        catch
        {
        }
    }

    internal static void ExtractAliveInfo(string? json, SeventeenLiveSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            result.IsLiveStreaming = root["status"]?.Value<int>() == 2;

            if (result.IsLiveStreaming != true)
            {
                return;
            }

            JObject? pullUrls = root["pullURLsInfo"] as JObject;
            result.FlvUrl = ExtractStreamUrl(pullUrls?["rtmpURLs"] as JArray, "urlHighQuality", "urlMediumQuality", "urlLowQuality", "url");
            result.HlsUrl = ExtractStreamUrl(pullUrls?["hlsURLs"] as JArray, "urlHighQuality", "urlMediumQuality", "urlLowQuality", "url");

            if (string.IsNullOrWhiteSpace(result.FlvUrl) && string.IsNullOrWhiteSpace(result.HlsUrl))
            {
                result.IsLiveStreaming = false;
            }
        }
        catch
        {
        }
    }

    private static string? ExtractStreamUrl(JArray? urls, params string[] names)
    {
        if (urls == null)
        {
            return null;
        }

        foreach (JObject item in urls.OfType<JObject>())
        {
            string? value = FirstString(item, names);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? FirstString(JObject node, params string[] names)
    {
        foreach (string name in names)
        {
            string? value = node[name]?.ToString();

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
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

        RestClient client = new(options);
        RestRequest request = new()
        {
            Method = method,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = PlatformCookieStore.GetCookie("17Live", Configurations.CookieOversea.Get());

        request.AddHeader("Origin", "https://17.live");
        request.AddHeader("Referer", "https://17.live/");
        request.AddHeader("User-Agent", "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))");
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
}

public sealed class SeventeenLiveSpiderResult : ISpiderResult
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
