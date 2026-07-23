using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class YouTubeSpider : ISpider
{
    public static Lazy<YouTubeSpider> Instance { get; } = new(() => new YouTubeSpider());

    public string PlatformName => "YouTube";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host == "youtu.be")
        {
            string? videoId = uri.Segments.Select(segment => segment.Trim('/')).FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
            return string.IsNullOrWhiteSpace(videoId) ? null : $"https://youtu.be/{videoId}";
        }

        if (uri.Host != "www.youtube.com" && uri.Host != "youtube.com")
        {
            return null;
        }

        string? watchId = GetQueryValue(uri.Query, "v");

        if (!string.IsNullOrWhiteSpace(watchId))
        {
            return $"https://www.youtube.com/watch?v={Uri.EscapeDataString(watchId)}";
        }

        string[] segments = uri.Segments.Select(segment => segment.Trim('/')).Where(segment => !string.IsNullOrWhiteSpace(segment)).ToArray();

        if (segments.Length >= 2 && segments[0].Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://www.youtube.com/live/{segments[1]}";
        }

        return null;
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        YouTubeSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string? html = SpiderRequest.Get(roomUrl, Headers(), PlatformCookieStore.GetCookie("YouTube", SecretProtector.GetOverseaCookie()));
        ExtractInitialPlayerResponse(html, result);

        return result;
    }

    internal static void ExtractInitialPlayerResponse(string? html, YouTubeSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        try
        {
            Match match = InitialPlayerResponseRegex.Match(html);

            if (!match.Success)
            {
                return;
            }

            string json = WebUtility.HtmlDecode(match.Groups[1].Value);
            JObject root = JObject.Parse(json);
            JObject? videoDetails = root["videoDetails"] as JObject;

            if (videoDetails == null)
            {
                return;
            }

            result.Nickname = videoDetails["author"]?.ToString();
            result.IsLiveStreaming = videoDetails["isLive"]?.Value<bool>() == true;

            if (result.IsLiveStreaming == true)
            {
                result.HlsUrl = root["streamingData"]?["hlsManifestUrl"]?.ToString();
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

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0",
        };
    }

    [GeneratedRegex("ytInitialPlayerResponse\\s*=\\s*(\\{.*?\\});\\s*(?:var meta|</script>)", RegexOptions.Singleline)]
    private static partial Regex InitialPlayerResponseRegex { get; }
}

public sealed class YouTubeSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

