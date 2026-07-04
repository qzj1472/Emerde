using Newtonsoft.Json.Linq;
using System.Web;

namespace Emerde.Core;

public sealed class VvXqiuSpider : ISpider
{
    public static Lazy<VvXqiuSpider> Instance { get; } = new(() => new VvXqiuSpider());

    public string PlatformName => "VVXqiu";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (!uri.Host.Contains("vvxqiu.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string roomId = HttpUtility.ParseQueryString(uri.Query)["roomId"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return null;
        }

        return $"https://h5webcdn-pro.vvxqiu.com/activity/videoShare/videoShare.html?roomId={Uri.EscapeDataString(roomId)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        VvXqiuSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string roomId = HttpUtility.ParseQueryString(new Uri(roomUrl).Query)["roomId"] ?? string.Empty;
        result.RoomId = roomId;

        string? json = SpiderRequest.Get($"https://h5p.vvxqiu.com/activity-center/fanclub/activity/captain/banner?roomId={Uri.EscapeDataString(roomId)}&product=vvstar", Headers(), Configurations.CookieChina.Get());
        ExtractBanner(json, result);

        string hlsUrl = $"https://liveplay-pro.wasaixiu.com/live/1400442770_{roomId}_{(roomId.Length > 2 ? roomId[2..] : roomId)}_single.m3u8";
        result.HlsUrl = hlsUrl;

        string? response = SpiderRequest.Get(hlsUrl, Headers(), Configurations.CookieChina.Get());

        if (!string.IsNullOrWhiteSpace(response) && !response.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
        {
            result.IsLiveStreaming = true;
        }

        return result;
    }

    internal static void ExtractBanner(string? json, VvXqiuSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? data = root["data"] as JObject;

            result.Nickname = data?["anchorName"]?.ToString()
                ?? data?["memberVO"]?["memberName"]?.ToString();
            result.AvatarThumbUrl = data?["anchorAvatar"]?.ToString();
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["Access-Control-Request-Method"] = "GET",
            ["Origin"] = "https://h5webcdn-pro.vvxqiu.com",
            ["Referer"] = "https://h5webcdn-pro.vvxqiu.com/",
            ["User-Agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
        };
    }
}

public sealed class VvXqiuSpiderResult : ISpiderResult
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
