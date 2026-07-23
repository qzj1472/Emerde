using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class FaceitSpider : ISpider
{
    public static Lazy<FaceitSpider> Instance { get; } = new(() => new FaceitSpider());

    public string PlatformName => "Faceit";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.faceit.com")
        {
            return null;
        }

        Match match = PlayerStreamRegex.Match(uri.AbsolutePath);

        return match.Success ? $"https://www.faceit.com/players/{match.Groups[1].Value}/stream" : null;
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        FaceitSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        Match match = PlayerStreamRegex.Match(new Uri(roomUrl).AbsolutePath);

        if (!match.Success)
        {
            return result;
        }

        string nickname = match.Groups[1].Value;
        string? userJson = SpiderRequest.Get($"https://www.faceit.com/api/users/v1/nicknames/{Uri.EscapeDataString(nickname)}", Headers(), PlatformCookieStore.GetCookie("Faceit", SecretProtector.GetOverseaCookie()));
        string? userId = ExtractUserId(userJson);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return result;
        }

        string? streamJson = SpiderRequest.Get($"https://www.faceit.com/api/stream/v1/streamings?userId={Uri.EscapeDataString(userId)}", Headers(), PlatformCookieStore.GetCookie("Faceit", SecretProtector.GetOverseaCookie()));
        ExtractStreaming(streamJson, result);

        if (result.Platform == "twitch" && !string.IsNullOrWhiteSpace(result.PlatformId))
        {
            ISpiderResult twitchResult = TwitchSpider.Instance.Value.GetResult($"https://www.twitch.tv/{result.PlatformId}");
            result.IsLiveStreaming = twitchResult.IsLiveStreaming;
            result.FlvUrl = twitchResult.FlvUrl;
            result.HlsUrl = twitchResult.HlsUrl;
            result.AvatarThumbUrl = twitchResult.AvatarThumbUrl;
        }

        return result;
    }

    internal static string? ExtractUserId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(json);
            return root["payload"]?["id"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static void ExtractStreaming(string? json, FaceitSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? platformInfo = root["payload"]?.FirstOrDefault() as JObject;

            if (platformInfo == null)
            {
                return;
            }

            result.Nickname = platformInfo["userNickname"]?.ToString();
            result.PlatformId = platformInfo["platformId"]?.ToString();
            result.Platform = platformInfo["platform"]?.ToString();
            result.IsLiveStreaming = false;
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["faceit-referer"] = "web-next",
            ["Referer"] = "https://www.faceit.com/zh/players/qpjzz/stream",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0",
        };
    }

    [GeneratedRegex("/players/([^/]+)/stream", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerStreamRegex { get; }
}

public sealed class FaceitSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? Platform { get; set; }

    public string? PlatformId { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

