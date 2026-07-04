using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class SixRoomsSpider : ISpider
{
    public static Lazy<SixRoomsSpider> Instance { get; } = new(() => new SixRoomsSpider());

    public string PlatformName => "6Rooms";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "v.6.cn" && uri.Host != "6.cn" && uri.Host != "www.6.cn")
        {
            return null;
        }

        string roomPath = uri.Segments.Select(segment => segment.Trim('/')).FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomPath))
        {
            return null;
        }

        return $"https://v.6.cn/{roomPath}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        SixRoomsSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string? html = SpiderRequest.Get(roomUrl, Headers(), Configurations.CookieChina.Get());
        string? roomId = ExtractRoomId(html);
        result.RoomId = roomId;

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return result;
        }

        string? json = SpiderRequest.PostForm(
            "https://v.6.cn/coop/mobile/index.php?padapi=coop-mobile-inroom.php",
            new Dictionary<string, string>
            {
                ["av"] = "3.1",
                ["encpass"] = string.Empty,
                ["logiuid"] = string.Empty,
                ["project"] = "v6iphone",
                ["rate"] = "1",
                ["rid"] = string.Empty,
                ["ruid"] = roomId,
            },
            Headers(),
            Configurations.CookieChina.Get());
        ExtractMobileRoom(json, result);

        return result;
    }

    internal static string? ExtractRoomId(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        Match match = RoomIdRegex.Match(html);

        return match.Success ? match.Groups[1].Value : null;
    }

    internal static void ExtractMobileRoom(string? json, SixRoomsSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? content = root["content"] as JObject;
            string? flvTitle = content?["liveinfo"]?["flvtitle"]?.ToString();

            result.Nickname = content?["roominfo"]?["alias"]?.ToString();
            result.IsLiveStreaming = !string.IsNullOrWhiteSpace(flvTitle);

            if (result.IsLiveStreaming == true)
            {
                result.FlvUrl = $"https://wlive.6rooms.com/httpflv/{flvTitle}.flv";
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
            ["Referer"] = "https://ios.6.cn/?ver=8.0.3&build=4",
            ["User-Agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
        };
    }

    [GeneratedRegex("rid:\\s*'(.*?)',\\s*\\r?\\n\\s*roomid", RegexOptions.Singleline)]
    private static partial Regex RoomIdRegex { get; }
}

public sealed class SixRoomsSpiderResult : ISpiderResult
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
