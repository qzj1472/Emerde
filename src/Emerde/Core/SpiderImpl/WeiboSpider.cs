using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class WeiboSpider : ISpider
{
    public static Lazy<WeiboSpider> Instance { get; } = new(() => new WeiboSpider());

    public string PlatformName => "Weibo";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "weibo.com")
        {
            return null;
        }

        Match showMatch = ShowRegex.Match(uri.AbsolutePath);

        if (showMatch.Success)
        {
            return $"https://weibo.com/l/wblive/p/show/{showMatch.Groups[1].Value}";
        }

        Match userMatch = UserRegex.Match(uri.AbsolutePath);

        return userMatch.Success ? $"https://weibo.com/u/{userMatch.Groups[1].Value}" : null;
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        WeiboSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string? roomId = null;
        Match showMatch = ShowRegex.Match(new Uri(roomUrl).AbsolutePath);

        if (showMatch.Success)
        {
            roomId = showMatch.Groups[1].Value;
        }
        else
        {
            Match userMatch = UserRegex.Match(new Uri(roomUrl).AbsolutePath);

            if (userMatch.Success)
            {
                string? timelineJson = SpiderRequest.Get(
                    $"https://weibo.com/ajax/statuses/mymblog?uid={Uri.EscapeDataString(userMatch.Groups[1].Value)}&page=1&feature=0",
                    Headers(),
                    Configurations.CookieChina.Get());
                roomId = ExtractLiveRoomId(timelineJson);
            }
        }

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return result;
        }

        string? liveJson = SpiderRequest.Get(
            $"https://weibo.com/l/pc/anchor/live?live_id={Uri.EscapeDataString(roomId)}",
            Headers(),
            Configurations.CookieChina.Get());
        ExtractLiveDetail(liveJson, result);

        return result;
    }

    internal static string? ExtractLiveRoomId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JArray? list = root["data"]?["list"] as JArray;

            return list?
                .OfType<JObject>()
                .Select(item => item["page_info"] as JObject)
                .FirstOrDefault(pageInfo => pageInfo?["object_type"]?.ToString() == "live")?["object_id"]
                ?.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static void ExtractLiveDetail(string? json, WeiboSpiderResult result)
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

            result.Nickname = data["user_info"]?["name"]?.ToString();
            JObject? item = data["item"] as JObject;
            result.IsLiveStreaming = item?["status"]?.Value<int>() == 1;

            if (result.IsLiveStreaming == true)
            {
                JObject? pull = item?["stream_info"]?["pull"] as JObject;
                result.HlsUrl = pull?["live_origin_hls_url"]?.ToString();
                result.FlvUrl = pull?["live_origin_flv_url"]?.ToString();
            }
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["Referer"] = "https://weibo.com/u/5885340893",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0",
        };
    }

    [GeneratedRegex("/show/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ShowRegex { get; }

    [GeneratedRegex("/u/(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex UserRegex { get; }
}

public sealed class WeiboSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

