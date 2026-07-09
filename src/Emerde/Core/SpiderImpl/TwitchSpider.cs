using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Emerde.Core;

public sealed class TwitchSpider : ISpider
{
    public static Lazy<TwitchSpider> Instance { get; } = new(() => new TwitchSpider());

    public string PlatformName => "Twitch";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.twitch.tv" && uri.Host != "twitch.tv")
        {
            return null;
        }

        string? channel = uri.Segments.Select(segment => segment.Trim('/')).FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment));

        if (string.IsNullOrWhiteSpace(channel) || channel.Equals("videos", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"https://www.twitch.tv/{channel}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        TwitchSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string channel = roomUrl.Split('/').Last();
        string? tokenJson = SpiderRequest.PostJson(
            "https://gql.twitch.tv/gql",
            BuildAccessTokenBody(channel),
            Headers(),
            PlatformCookieStore.GetCookie("Twitch", Configurations.CookieOversea.Get()));
        TwitchAccessToken? token = ExtractPlaybackAccessToken(tokenJson);
        string? roomJson = SpiderRequest.PostJson(
            "https://gql.twitch.tv/gql",
            BuildRoomInfoBody(channel),
            Headers(),
            PlatformCookieStore.GetCookie("Twitch", Configurations.CookieOversea.Get()));
        ExtractRoomInfo(roomJson, result);

        if (result.IsLiveStreaming == true && token != null)
        {
            result.HlsUrl = BuildHlsUrl(channel, token);
        }

        return result;
    }

    internal static TwitchAccessToken? ExtractPlaybackAccessToken(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? token = root["data"]?["streamPlaybackAccessToken"] as JObject;
            string? value = token?["value"]?.ToString();
            string? signature = token?["signature"]?.ToString();

            return string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(signature)
                ? null
                : new TwitchAccessToken(value, signature);
        }
        catch
        {
            return null;
        }
    }

    internal static void ExtractRoomInfo(string? json, TwitchSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JToken root = JToken.Parse(json);
            JObject? user = root.Type == JTokenType.Array
                ? root.First?["data"]?["userOrError"] as JObject
                : root["data"]?["userOrError"] as JObject;

            if (user == null)
            {
                return;
            }

            string? login = user["login"]?.ToString();
            string? displayName = user["displayName"]?.ToString();
            result.Nickname = string.IsNullOrWhiteSpace(login) ? displayName : $"{displayName}-{login}";
            result.AvatarThumbUrl = user["profileImageURL"]?.ToString() ?? user["profileImageUrl"]?.ToString();
            result.IsLiveStreaming = user["stream"] != null && user["stream"]?.Type != JTokenType.Null;
        }
        catch
        {
        }
    }

    internal static string BuildHlsUrl(string channel, TwitchAccessToken token)
    {
        Dictionary<string, string> parameters = new()
        {
            ["acmb"] = "e30=",
            ["allow_source"] = "true",
            ["browser_family"] = "firefox",
            ["browser_version"] = "124.0",
            ["cdm"] = "wv",
            ["fast_bread"] = "true",
            ["os_name"] = "Windows",
            ["os_version"] = "NT%2010.0",
            ["p"] = Random.Shared.Next(1000000, 9999999).ToString(),
            ["platform"] = "web",
            ["play_session_id"] = Guid.NewGuid().ToString("N"),
            ["player_backend"] = "mediaplayer",
            ["player_version"] = "1.28.0-rc.1",
            ["playlist_include_framerate"] = "true",
            ["reassignments_supported"] = "true",
            ["sig"] = token.Signature,
            ["token"] = token.Value,
            ["transcode_mode"] = "cbr_v1",
        };
        string query = string.Join("&", parameters.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));

        return $"https://usher.ttvnw.net/api/channel/hls/{Uri.EscapeDataString(channel)}.m3u8?{query}";
    }

    private static string BuildAccessTokenBody(string channel)
    {
        return JsonConvert.SerializeObject(new
        {
            operationName = "PlaybackAccessToken_Template",
            query = "query PlaybackAccessToken_Template($login: String!, $isLive: Boolean!, $vodID: ID!, $isVod: Boolean!, $playerType: String!) {  streamPlaybackAccessToken(channelName: $login, params: {platform: \"web\", playerBackend: \"mediaplayer\", playerType: $playerType}) @include(if: $isLive) {    value    signature   authorization { isForbidden forbiddenReasonCode }   __typename  }  videoPlaybackAccessToken(id: $vodID, params: {platform: \"web\", playerBackend: \"mediaplayer\", playerType: $playerType}) @include(if: $isVod) {    value    signature   __typename  }}",
            variables = new
            {
                isLive = true,
                login = channel,
                isVod = false,
                vodID = string.Empty,
                playerType = "site",
            },
        });
    }

    private static string BuildRoomInfoBody(string channel)
    {
        return JsonConvert.SerializeObject(new[]
        {
            new
            {
                operationName = "ChannelShell",
                variables = new
                {
                    login = channel,
                },
                extensions = new
                {
                    persistedQuery = new
                    {
                        version = 1,
                        sha256Hash = "580ab410bcd0c1ad194224957ae2241e5d252b2c5173d8e0cce9d32d5bb14efe",
                    },
                },
            },
        });
    }

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["Accept-Language"] = "en-US",
            ["Client-ID"] = "kimne78kx3ncx6brgo4mv6wki5h1ko",
            ["Content-Type"] = "text/plain;charset=UTF-8",
            ["device-id"] = Guid.NewGuid().ToString("N")[..16],
            ["Referer"] = "https://www.twitch.tv/",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0",
        };
    }
}

internal sealed record TwitchAccessToken(string Value, string Signature);

public sealed class TwitchSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

