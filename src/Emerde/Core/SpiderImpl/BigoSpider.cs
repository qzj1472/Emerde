using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class BigoSpider : ISpider
{
    public static Lazy<BigoSpider> Instance { get; } = new(() => new BigoSpider());

    public string PlatformName => "Bigo";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.bigo.tv" && uri.Host != "bigo.tv")
        {
            return null;
        }

        string? roomId = ExtractRoomId(uri);

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return null;
        }

        return $"https://www.bigo.tv/{roomId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        BigoSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomId = roomUrl.Split('/').Last();
        string? json = RequestStudioInfo(roomId);
        ExtractStudioInfo(json, result);

        return result;
    }

    internal static void ExtractStudioInfo(string? json, BigoSpiderResult result)
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

            result.Nickname = data["nick_name"]?.ToString();
            result.AvatarThumbUrl = data["avatar"]?.ToString();
            result.IsLiveStreaming = data["alive"]?.Value<int>() == 1;

            if (result.IsLiveStreaming == true)
            {
                result.HlsUrl = data["hls_src"]?.ToString();
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

    private static string? RequestStudioInfo(string roomId)
    {
        RestClientOptions options = new()
        {
            BaseUrl = new Uri("https://ta.bigo.tv/official_website/studio/getInternalStudioInfo"),
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
            Method = Method.Post,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = PlatformCookieStore.GetCookie("Bigo", Configurations.CookieOversea.Get());

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/119.0");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2");
        request.AddHeader("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
        request.AddHeader("Referer", "https://www.bigo.tv/");
        request.AddParameter("siteId", roomId);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    [GeneratedRegex("(?:\\?|&)h=([^&]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RoomIdQueryRegex { get; }
}

public sealed class BigoSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
