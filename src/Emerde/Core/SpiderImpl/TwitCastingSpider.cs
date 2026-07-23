using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class TwitCastingSpider : ISpider
{
    public static Lazy<TwitCastingSpider> Instance { get; } = new(() => new TwitCastingSpider());

    public string PlatformName => "TwitCasting";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "twitcasting.tv")
        {
            return null;
        }

        string? anchorId = uri.Segments.Select(segment => segment.Trim('/')).FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));

        if (string.IsNullOrWhiteSpace(anchorId))
        {
            return null;
        }

        return $"https://twitcasting.tv/{anchorId}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        TwitCastingSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string? html = SpiderRequest.Get(roomUrl, Headers(), PlatformCookieStore.GetCookie("TwitCasting", SecretProtector.GetOverseaCookie()));
        ExtractPageData(html, result);

        if (result.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(result.AnchorId))
        {
            string api = $"https://twitcasting.tv/streamserver.php?target={Uri.EscapeDataString(result.AnchorId)}&mode=client&player=pc_web";
            string? streamJson = SpiderRequest.Get(api, Headers(), PlatformCookieStore.GetCookie("TwitCasting", SecretProtector.GetOverseaCookie()));
            ExtractStreamServer(streamJson, result);
        }

        return result;
    }

    internal static void ExtractPageData(string? html, TwitCastingSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        try
        {
            Match anchorMatch = AnchorRegex.Match(html);
            Match statusMatch = StatusRegex.Match(html);
            Match movieMatch = MovieIdRegex.Match(html);
            Match titleMatch = TitleRegex.Match(html);

            if (anchorMatch.Success)
            {
                string name = WebUtility.HtmlDecode(anchorMatch.Groups[1].Value.Trim());
                string anchorId = anchorMatch.Groups[2].Value.Trim();
                string movieId = movieMatch.Success ? movieMatch.Groups[1].Value.Trim() : string.Empty;
                result.AnchorId = anchorId;
                result.Nickname = string.IsNullOrWhiteSpace(movieId) ? $"{name}-{anchorId}" : $"{name}-{anchorId}-{movieId}";
            }

            result.Title = titleMatch.Success ? WebUtility.HtmlDecode(titleMatch.Groups[1].Value) : null;
            result.IsLiveStreaming = statusMatch.Success && statusMatch.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
        }
    }

    internal static void ExtractStreamServer(string? json, TwitCastingSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? streams = root["tc-hls"]?["streams"] as JObject;

            if (streams == null)
            {
                return;
            }

            result.HlsUrl = new[] { "high", "medium", "low" }
                .Select(quality => streams[quality]?.ToString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        catch
        {
        }
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7",
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["Referer"] = "https://twitcasting.tv/?ch0",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0",
        };
    }

    [GeneratedRegex("<title>(.*?) \\(@(.*?)\\).*?Twit", RegexOptions.Singleline)]
    private static partial Regex AnchorRegex { get; }

    [GeneratedRegex("data-is-onlive=\"(.*?)\"", RegexOptions.Singleline)]
    private static partial Regex StatusRegex { get; }

    [GeneratedRegex("data-movie-id=\"(.*?)\"", RegexOptions.Singleline)]
    private static partial Regex MovieIdRegex { get; }

    [GeneratedRegex("<meta name=\"twitter:title\" content=\"(.*?)\"", RegexOptions.Singleline)]
    private static partial Regex TitleRegex { get; }
}

public sealed class TwitCastingSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? AnchorId { get; set; }

    public string? Title { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

