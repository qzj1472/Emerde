using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;


public sealed partial class YouTubeSpider : ISpider
{
    internal const string WebUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0";
    private static readonly (string Name, string Version, string UserAgent, bool Android)[] PlayerClients =
    [
        ("ANDROID", "20.10.38", "com.google.android.youtube/20.10.38 (Linux; U; Android 11)", true),
        ("IOS", "20.10.38", "com.google.ios.youtube/20.10.38 (iPhone; U; CPU iOS 16_5 like Mac OS X)", false),
        ("WEB", "2.20250918.01.00", WebUserAgent, false),
    ];

    public static Lazy<YouTubeSpider> Instance { get; } = new(() => new YouTubeSpider());

    public string PlatformName => "YouTube";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            string? videoId = uri.Segments.Select(segment => segment.Trim('/')).FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
            return string.IsNullOrWhiteSpace(videoId) ? null : $"https://youtu.be/{videoId}";
        }

        if (!IsYouTubeHost(uri.Host))
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

        if (segments.Length >= 2 && segments[^1].Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://www.youtube.com/{string.Join('/', segments)}";
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

        string? cookie = PlatformCookieStore.GetCookie("YouTube", SecretProtector.GetOverseaCookie());
        string? html = SpiderRequest.Get(roomUrl, Headers(roomUrl), cookie);
        ExtractInitialPlayerResponse(html, result);
        if (result.IsLiveStreaming == true
            && string.IsNullOrWhiteSpace(result.HlsUrl)
            && string.IsNullOrWhiteSpace(result.RecordUrl))
        {
            TryEnrichWithPlayerResponses(html, roomUrl, result);
        }

        if (result.IsLiveStreaming == true && (!string.IsNullOrWhiteSpace(result.HlsUrl) || !string.IsNullOrWhiteSpace(result.RecordUrl)))
        {
            result.Headers = BuildPlaybackHeaders(roomUrl, cookie, result.PlaybackUserAgent);
        }

        return result;
    }

    internal static void ExtractInitialPlayerResponse(string? html, YouTubeSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        ExtractInitialData(html, result);

        try
        {
            Match match = InitialPlayerResponseRegex.Match(html);

            if (!match.Success)
            {
                return;
            }

            string json = WebUtility.HtmlDecode(match.Groups[1].Value);
            ApplyPlayerResponse(JObject.Parse(json), result);
        }
        catch
        {
        }
    }

    internal static void ApplyPlayerResponse(JObject root, YouTubeSpiderResult result)
    {
        JObject? videoDetails = root["videoDetails"] as JObject;
        bool? isLive = null;
        bool isLiveContent = false;
        if (videoDetails != null)
        {
            result.VideoId ??= videoDetails["videoId"]?.ToString();
            result.Nickname = videoDetails["author"]?.ToString() ?? result.Nickname;
            result.Title = videoDetails["title"]?.ToString() ?? result.Title;
            if (videoDetails["isLive"]?.Type == JTokenType.Boolean)
            {
                isLive = videoDetails["isLive"]!.Value<bool>();
                result.IsLiveStreaming = isLive;
            }
            isLiveContent = videoDetails["isLiveContent"]?.Type == JTokenType.Boolean
                && videoDetails["isLiveContent"]!.Value<bool>();
        }

        JObject? streamingData = root["streamingData"] as JObject;
        string? hlsUrl = streamingData?["hlsManifestUrl"]?.ToString();
        string? serverAbrUrl = streamingData?["serverAbrStreamingUrl"]?.ToString();
        string? muxedUrl = SelectMuxedFormatUrl(streamingData?["formats"]);
        if (result.IsLiveStreaming != false
            && (isLive == true || isLiveContent)
            && (!string.IsNullOrWhiteSpace(hlsUrl)
                || !string.IsNullOrWhiteSpace(muxedUrl)
                || !string.IsNullOrWhiteSpace(serverAbrUrl)))
        {
            result.IsLiveStreaming = true;
        }
        if (result.IsLiveStreaming != true)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(hlsUrl))
        {
            result.HlsUrl = hlsUrl;
            result.RecordUrl = hlsUrl;
            return;
        }

        if (!string.IsNullOrWhiteSpace(muxedUrl))
        {
            result.RecordUrl = muxedUrl;
        }
        else if (!string.IsNullOrWhiteSpace(serverAbrUrl))
        {
            result.RecordUrl = null;
        }
    }

    private static string? SelectMuxedFormatUrl(JToken? formatsToken)
    {
        if (formatsToken is not JArray formats)
        {
            return null;
        }

        return formats
            .OfType<JObject>()
            .Select(format => new
            {
                Url = format["url"]?.ToString(),
                MimeType = format["mimeType"]?.ToString() ?? string.Empty,
                AudioQuality = format["audioQuality"]?.ToString(),
                Width = ReadInt(format["width"]),
                Height = ReadInt(format["height"]),
                Fps = ReadInt(format["fps"]),
                Bitrate = ReadInt(format["bitrate"]),
            })
            .Where(format => !string.IsNullOrWhiteSpace(format.Url)
                && format.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(format.AudioQuality)
                    || format.MimeType.Contains("mp4a", StringComparison.OrdinalIgnoreCase)
                    || format.MimeType.Contains("opus", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(format => format.Height)
            .ThenByDescending(format => format.Width)
            .ThenByDescending(format => format.Fps)
            .ThenByDescending(format => format.Bitrate)
            .Select(format => format.Url)
            .FirstOrDefault();
    }

    private static int ReadInt(JToken? value)
    {
        return int.TryParse(value?.ToString(), out int result) ? result : 0;
    }

    private static void TryEnrichWithPlayerResponses(string? html, string roomUrl, YouTubeSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        Match apiKeyMatch = Regex.Match(
            html,
            "\"INNERTUBE_API_KEY\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        string? videoId = GetVideoId(roomUrl) ?? result.VideoId;
        if (!apiKeyMatch.Success || string.IsNullOrWhiteSpace(videoId))
        {
            return;
        }

        string endpoint = $"https://www.youtube.com/youtubei/v1/player?key={Uri.EscapeDataString(apiKeyMatch.Groups[1].Value)}";
        string? cookie = PlatformCookieStore.GetCookie("YouTube", SecretProtector.GetOverseaCookie());
        foreach ((string name, string version, string userAgent, bool android) in PlayerClients)
        {
            JObject client = new()
            {
                ["clientName"] = name,
                ["clientVersion"] = version,
                ["hl"] = "en",
                ["gl"] = "US",
            };
            if (android)
            {
                client["androidSdkVersion"] = 30;
            }

            JObject request = new()
            {
                ["context"] = new JObject { ["client"] = client },
                ["videoId"] = videoId,
                ["contentCheckOk"] = true,
                ["racyCheckOk"] = true,
            };
            string? response = SpiderRequest.PostJson(
                endpoint,
                request.ToString(Formatting.None),
                Headers(roomUrl, userAgent, name, version),
                cookie);
            if (string.IsNullOrWhiteSpace(response))
            {
                continue;
            }

            try
            {
                YouTubeSpiderResult candidate = new()
                {
                    RoomUrl = result.RoomUrl,
                    PlatformName = result.PlatformName,
                    IsLiveStreaming = result.IsLiveStreaming,
                    Nickname = result.Nickname,
                    AvatarThumbUrl = result.AvatarThumbUrl,
                    Title = result.Title,
                    VideoId = result.VideoId,
                };
                ApplyPlayerResponse(JObject.Parse(response), candidate);
                if (string.IsNullOrWhiteSpace(candidate.HlsUrl)
                    && string.IsNullOrWhiteSpace(candidate.RecordUrl))
                {
                    continue;
                }

                result.IsLiveStreaming = candidate.IsLiveStreaming;
                result.HlsUrl = candidate.HlsUrl;
                result.RecordUrl = candidate.RecordUrl;
                result.PlaybackUserAgent = userAgent;
                return;
            }
            catch
            {
            }
        }
    }

    private static string? GetVideoId(string roomUrl)
    {
        if (!Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return uri.Segments
                .Select(segment => segment.Trim('/'))
                .FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
        }

        string? watchId = GetQueryValue(uri.Query, "v");
        if (!string.IsNullOrWhiteSpace(watchId))
        {
            return watchId;
        }

        string[] segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        int liveIndex = Array.FindIndex(segments, segment => segment.Equals("live", StringComparison.OrdinalIgnoreCase));
        return liveIndex >= 0 && liveIndex + 1 < segments.Length ? segments[liveIndex + 1] : null;
    }

    private static void ExtractInitialData(string html, YouTubeSpiderResult result)
    {
        Match match = InitialDataRegex.Match(html);
        if (!match.Success)
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
            result.Nickname ??= FirstText(root.SelectTokens("$..videoOwnerRenderer.title.runs[*].text"));
            result.AvatarThumbUrl ??= FirstText(root.SelectTokens("$..videoOwnerRenderer.thumbnail.thumbnails[*].url").Reverse());
            result.AvatarThumbUrl ??= FirstText(root.SelectTokens("$..channelMetadataRenderer.avatar.thumbnails[*].url").Reverse());
        }
        catch
        {
        }
    }

    private static string? FirstText(IEnumerable<JToken> values)
    {
        return values
            .Select(value => value.ToString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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

    private static IReadOnlyDictionary<string, string> Headers(
        string? referer = null,
        string? userAgent = null,
        string? clientName = null,
        string? clientVersion = null)
    {
        Dictionary<string, string> headers = new()
        {
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["User-Agent"] = string.IsNullOrWhiteSpace(userAgent) ? WebUserAgent : userAgent,
        };
        if (!string.IsNullOrWhiteSpace(referer))
        {
            headers["Referer"] = referer;
        }
        if (!string.IsNullOrWhiteSpace(clientName))
        {
            headers["X-YouTube-Client-Name"] = clientName;
        }
        if (!string.IsNullOrWhiteSpace(clientVersion))
        {
            headers["X-YouTube-Client-Version"] = clientVersion;
        }

        return headers;
    }

    private static string BuildPlaybackHeaders(string roomUrl, string? cookie, string? userAgent = null)
    {
        string headers = $"User-Agent: {(string.IsNullOrWhiteSpace(userAgent) ? WebUserAgent : userAgent)}\r\nReferer: {roomUrl}\r\nOrigin: https://www.youtube.com";
        return string.IsNullOrWhiteSpace(cookie) ? headers : $"{headers}\r\nCookie: {cookie}";
    }

    private static bool IsYouTubeHost(string host)
    {
        return host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("www.youtube-nocookie.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("ytInitialPlayerResponse\\s*=\\s*(\\{.*?\\});\\s*(?:var meta|</script>)", RegexOptions.Singleline)]
    private static partial Regex InitialPlayerResponseRegex { get; }

    [GeneratedRegex("ytInitialData\\s*=\\s*(\\{.*?\\});\\s*(?:var|</script>)", RegexOptions.Singleline)]
    private static partial Regex InitialDataRegex { get; }
}

public sealed class YouTubeSpiderResult : ISpiderResult
{
    internal string? VideoId { get; set; }

    internal string? PlaybackUserAgent { get; set; }

    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? RecordUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }

    public string? Headers { get; set; }

    public string? Title { get; set; }
}

