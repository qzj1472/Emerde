using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Emerde.Core;

internal static partial class StreamResolver
{
    private const int RequestTimeoutSeconds = 5;
    private const int RedirectTimeoutSeconds = 3;
    private const int PlaylistTimeoutSeconds = 2;
    private const string DouyinDefaultCookie = "ttwid=1%7C2iDIYVmjzMcpZ20fcaFde0VghXAA3NaNXE_SLR68IyE%7C1761045455%7Cab35197d5cfb21df6cbb2fa7ef1c9262206b062c315b9d04da746d0b37dfbc7d";
    private const string DouyinWebUserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.5845.97 Safari/537.36 Core/1.116.567.400 QQBrowser/19.7.6764.400";
    private static readonly ConcurrentDictionary<string, string> LastErrors = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, HlsVariant> HlsVariantCache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetLastError(string url)
    {
        string key = NormalizeUrl(url, allowNetwork: false) ?? url.Trim();
        return LastErrors.TryGetValue(key, out string? error) ? error : string.Empty;
    }

    public static string GetPlatformName(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return string.Empty;
        }

        if (IsDirectStream(uri))
        {
            return "Direct";
        }

        string host = uri.Host.ToLowerInvariant();
        if (host.EndsWith("douyin.com", StringComparison.Ordinal) || host.EndsWith("iesdouyin.com", StringComparison.Ordinal))
        {
            return "Douyin";
        }

        if (host.EndsWith("tiktok.com", StringComparison.Ordinal))
        {
            return "TikTok";
        }

        return string.Empty;
    }

    public static string? NormalizeUrl(string url, bool allowNetwork = false)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (IsDirectStream(uri))
        {
            return trimmed;
        }

        string host = uri.Host.ToLowerInvariant();
        if (host is "v.douyin.com" or "www.iesdouyin.com" or "iesdouyin.com")
        {
            return allowNetwork && TryResolveRedirect(trimmed, out string? redirected)
                ? NormalizeUrl(redirected ?? string.Empty, allowNetwork: false) ?? redirected
                : trimmed;
        }

        if (host.EndsWith("douyin.com", StringComparison.Ordinal))
        {
            string? roomId = ExtractDouyinRoomId(uri);
            return string.IsNullOrWhiteSpace(roomId) ? null : $"https://live.douyin.com/{roomId}";
        }

        if (host is "vm.tiktok.com" or "vt.tiktok.com")
        {
            return allowNetwork && TryResolveRedirect(trimmed, out string? redirected)
                ? NormalizeUrl(redirected ?? string.Empty, allowNetwork: false) ?? redirected
                : trimmed;
        }

        if (host.EndsWith("tiktok.com", StringComparison.Ordinal))
        {
            string? userId = uri.Segments
                .Select(segment => segment.Trim('/'))
                .FirstOrDefault(segment => segment.StartsWith("@", StringComparison.Ordinal));

            return string.IsNullOrWhiteSpace(userId) ? null : $"https://www.tiktok.com/{userId}/live";
        }

        return null;
    }

    public static ISpiderResult? GetResult(string url, string? preferredQuality = null)
    {
        string? normalizedUrl = NormalizeUrl(url, allowNetwork: true);
        string resolverUrl = normalizedUrl ?? url.Trim();

        try
        {
            if (!Uri.TryCreate(resolverUrl, UriKind.Absolute, out Uri? uri))
            {
                SetLastError(resolverUrl, "Invalid room url.");
                return null;
            }

            if (IsDirectStream(uri))
            {
                StreamResolverResult directResult = CreateDirectResult(resolverUrl);
                EnrichHighestHlsVariant(directResult, preferredQuality, resolverUrl, null, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                return directResult;
            }

            string host = uri.Host.ToLowerInvariant();
            if (host.EndsWith("douyin.com", StringComparison.Ordinal))
            {
                return ResolveDouyin(resolverUrl, preferredQuality);
            }

            if (host.EndsWith("tiktok.com", StringComparison.Ordinal))
            {
                return ResolveTiktok(resolverUrl, preferredQuality);
            }

            return null;
        }
        catch (Exception e)
        {
            SetLastError(resolverUrl, e.Message);
            return null;
        }
    }

    public static bool HasUsableData(ISpiderResult? result)
    {
        if (result == null)
        {
            return false;
        }

        return result.IsLiveStreaming != null
            || !string.IsNullOrWhiteSpace(result.FlvUrl)
            || !string.IsNullOrWhiteSpace(result.HlsUrl);
    }

    internal static StreamResolverResult ExtractDouyinData(string roomUrl, string? html, string? preferredQuality = null)
    {
        StreamResolverResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = "Douyin",
        };

        if (string.IsNullOrWhiteSpace(html))
        {
            return result;
        }

        string normalized = NormalizeEscapedText(html);
        DouyinSpiderResult legacy = DouyinSpider.ExtractData(html);
        StreamCandidate hls = ExtractPreferredStreamCandidate(normalized, "hls", ".m3u8", preferredQuality);
        StreamCandidate flv = ExtractPreferredStreamCandidate(normalized, "flv", ".flv", preferredQuality);

        result.Nickname = FirstNonEmpty(CleanOptionalText(legacy.Nickname), ExtractFirstJsonString(normalized, "nickname", "nick_name"));
        result.AvatarThumbUrl = FirstNonEmpty(CleanOptionalUrl(legacy.AvatarThumbUrl), ExtractFirstAvatar(normalized));
        result.HlsUrl = FirstNonEmpty(hls.Url, CleanOptionalUrl(legacy.HlsUrl));
        result.FlvUrl = FirstNonEmpty(flv.Url, CleanOptionalUrl(legacy.FlvUrl));
        result.Quality = FirstNonEmpty(hls.Quality, flv.Quality);
        result.IsLiveStreaming = legacy.IsLiveStreaming ?? ExtractLiveStatus(normalized);

        if ((!string.IsNullOrWhiteSpace(result.HlsUrl) || !string.IsNullOrWhiteSpace(result.FlvUrl))
            && result.IsLiveStreaming != false)
        {
            result.IsLiveStreaming = true;
        }
        result.Title = result.IsLiveStreaming == true ? ExtractDouyinLiveTitle(html, normalized) : null;
        return result;
    }

    internal static StreamResolverResult ExtractDouyinWebEnterData(string roomUrl, string? json, string? preferredQuality = null)
    {
        StreamResolverResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = "Douyin",
        };

        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JToken? data = root["data"];
            JToken? room = data?["data"]?.FirstOrDefault();
            if (room == null)
            {
                return result;
            }

            JToken? user = data?["user"] ?? room["user"];
            result.Nickname = FirstNonEmpty(
                CleanOptionalText(user?["nickname"]?.ToString()),
                CleanOptionalText(room["anchor_name"]?.ToString()),
                CleanOptionalText(room["owner"]?["nickname"]?.ToString()));
            result.AvatarThumbUrl = FirstNonEmpty(
                ExtractFirstUrlFromList(user?["avatar_thumb"]?["url_list"]),
                ExtractFirstUrlFromList(room["owner"]?["avatar_thumb"]?["url_list"]),
                ExtractFirstAvatar(NormalizeEscapedText(root.ToString(Newtonsoft.Json.Formatting.None))));
            int? status = room["status"]?.Value<int?>()
                ?? ParseNullableInt(room["status_str"]?.ToString());
            result.IsLiveStreaming = status switch
            {
                2 => true,
                null => null,
                _ => false,
            };

            JToken? streamUrl = room["stream_url"];
            StreamCandidate hls = SelectPreferredStream(streamUrl?["hls_pull_url_map"], preferredQuality);
            StreamCandidate flv = SelectPreferredStream(streamUrl?["flv_pull_url"], preferredQuality);
            result.HlsUrl = hls.Url;
            result.FlvUrl = flv.Url;
            result.Quality = FirstNonEmpty(hls.Quality, flv.Quality);

            if ((!string.IsNullOrWhiteSpace(result.HlsUrl) || !string.IsNullOrWhiteSpace(result.FlvUrl))
                && result.IsLiveStreaming != false)
            {
                result.IsLiveStreaming = true;
            }
            result.Title = result.IsLiveStreaming == true ? CleanOptionalText(room["title"]?.ToString()) : null;
        }
        catch
        {
        }

        return result;
    }

    internal static StreamResolverResult ExtractTiktokData(string roomUrl, string? html, string? preferredQuality = null)
    {
        StreamResolverResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = "TikTok",
        };

        if (string.IsNullOrWhiteSpace(html))
        {
            return result;
        }

        string normalized = NormalizeEscapedText(html);
        TiktokSpiderResult legacy = TiktokSpider.ExtractData(html);
        StreamCandidate hls = ExtractPreferredStreamCandidate(normalized, "hls", ".m3u8", preferredQuality);
        StreamCandidate flv = ExtractPreferredStreamCandidate(normalized, "flv", ".flv", preferredQuality);

        result.Nickname = FirstNonEmpty(CleanOptionalText(legacy.Nickname), ExtractFirstJsonString(normalized, "nickname", "nickName", "uniqueId"));
        result.AvatarThumbUrl = FirstNonEmpty(CleanOptionalUrl(legacy.AvatarThumbUrl), ExtractFirstJsonString(normalized, "avatarThumb", "avatarMedium", "avatarLarger", "avatar"));
        result.HlsUrl = FirstNonEmpty(hls.Url, CleanOptionalUrl(legacy.HlsUrl));
        result.FlvUrl = FirstNonEmpty(flv.Url, CleanOptionalUrl(legacy.FlvUrl));
        result.Quality = FirstNonEmpty(hls.Quality, flv.Quality);
        result.IsLiveStreaming = legacy.IsLiveStreaming ?? ExtractLiveStatus(normalized);

        if ((!string.IsNullOrWhiteSpace(result.HlsUrl) || !string.IsNullOrWhiteSpace(result.FlvUrl))
            && result.IsLiveStreaming != false)
        {
            result.IsLiveStreaming = true;
        }
        result.Title = result.IsLiveStreaming == true ? ExtractTiktokLiveTitle(html, normalized) : null;

        return result;
    }

    private static StreamResolverResult? ResolveDouyin(string roomUrl, string? preferredQuality)
    {
        StreamResolverResult? webEnterResult = ResolveDouyinWebEnter(roomUrl, preferredQuality);
        if (HasUsableData(webEnterResult))
        {
            EnrichHighestHlsVariant(webEnterResult!, preferredQuality, roomUrl, PlatformCookieStore.GetCookie("Douyin", Configurations.CookieChina.Get()), DouyinWebUserAgent);
            LastErrors.TryRemove(roomUrl, out _);
            return webEnterResult;
        }

        string? html = RequestText(
            roomUrl,
            "https://live.douyin.com/",
            PlatformCookieStore.GetCookie("Douyin", Configurations.CookieChina.Get()),
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");

        StreamResolverResult result = ExtractDouyinData(roomUrl, html, preferredQuality);

        if (HasUsableData(result))
        {
            EnrichHighestHlsVariant(result, preferredQuality, roomUrl, PlatformCookieStore.GetCookie("Douyin", Configurations.CookieChina.Get()), DouyinWebUserAgent);
            LastErrors.TryRemove(roomUrl, out _);
            return result;
        }

        SetLastError(roomUrl, "Douyin room data was empty or blocked.");
        return null;
    }

    private static StreamResolverResult? ResolveDouyinWebEnter(string roomUrl, string? preferredQuality)
    {
        if (!Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string? webRid = ExtractDouyinRoomId(uri);
        if (string.IsNullOrWhiteSpace(webRid))
        {
            return null;
        }

        string query = string.Join("&",
        [
            "aid=6383",
            "app_name=douyin_web",
            "live_id=1",
            "device_platform=web",
            "language=zh-CN",
            "browser_language=zh-CN",
            "browser_platform=Win32",
            "browser_name=Chrome",
            "browser_version=116.0.0.0",
            $"web_rid={Uri.EscapeDataString(webRid)}",
            "msToken=",
        ]);
        string aBogus = DouyinWebSignature.CreateABogus(query, DouyinWebUserAgent);
        string api = $"https://live.douyin.com/webcast/room/web/enter/?{query}&a_bogus={Uri.EscapeDataString(aBogus)}";
        string configuredCookie = PlatformCookieStore.GetCookie("Douyin", Configurations.CookieChina.Get());
        string cookie = string.IsNullOrWhiteSpace(configuredCookie) ? DouyinDefaultCookie : configuredCookie;
        string? json = RequestText(
            api,
            "https://live.douyin.com/335354047186",
            cookie,
            DouyinWebUserAgent,
            "application/json,text/plain,*/*");

        StreamResolverResult result = ExtractDouyinWebEnterData(roomUrl, json, preferredQuality);
        return HasUsableData(result) ? result : null;
    }

    private static StreamResolverResult? ResolveTiktok(string roomUrl, string? preferredQuality)
    {
        string? html = RequestText(
            roomUrl,
            "https://www.tiktok.com/",
            PlatformCookieStore.GetCookie("TikTok", Configurations.CookieOversea.Get()),
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");

        StreamResolverResult result = ExtractTiktokData(roomUrl, html, preferredQuality);

        if (HasUsableData(result))
        {
            EnrichHighestHlsVariant(result, preferredQuality, roomUrl, PlatformCookieStore.GetCookie("TikTok", Configurations.CookieOversea.Get()), "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");
            LastErrors.TryRemove(roomUrl, out _);
            return result;
        }

        SetLastError(roomUrl, "TikTok room data was empty, blocked, or region restricted.");
        return null;
    }

    private static StreamResolverResult CreateDirectResult(string url)
    {
        string name = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : "Direct";

        return new StreamResolverResult()
        {
            RoomUrl = url,
            PlatformName = "Direct",
            Nickname = string.IsNullOrWhiteSpace(name) ? "Direct" : name,
            IsLiveStreaming = true,
            HlsUrl = url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ? url : null,
            FlvUrl = url.Contains(".flv", StringComparison.OrdinalIgnoreCase) || url.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase) ? url : null,
            Title = string.IsNullOrWhiteSpace(name) ? "Direct stream" : name,
        };
    }

    private static string? RequestText(
        string url,
        string referer,
        string? cookie,
        string userAgent,
        string accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
    {
        using HttpClient client = CreateHttpClient();
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", string.IsNullOrWhiteSpace(Configurations.UserAgent.Get()) ? userAgent : Configurations.UserAgent.Get());
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.7");
        request.Headers.TryAddWithoutValidation("Referer", referer);

        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        using HttpResponseMessage response = client.Send(request);
        return response.IsSuccessStatusCode ? response.Content.ReadAsStringAsync().GetAwaiter().GetResult() : null;
    }

    private static HttpClient CreateHttpClient(bool allowAutoRedirect = true, int timeoutSeconds = RequestTimeoutSeconds)
    {
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = allowAutoRedirect,
            AutomaticDecompression = DecompressionMethods.All,
        };

        if (Configurations.IsUseProxy.Get())
        {
            string proxyUrl = Configurations.ProxyUrl.Get();
            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                WebProxy? proxy = ProxyAddress.Create(proxyUrl);
                handler.Proxy = proxy;
                handler.UseProxy = proxy != null;
            }
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
    }

    private static void EnrichHighestHlsVariant(StreamResolverResult result, string? preferredQuality, string referer, string? cookie, string userAgent)
    {
        if (StreamQualityCatalog.NormalizePreference(preferredQuality) != StreamQualityCatalog.Original
            || string.IsNullOrWhiteSpace(result.HlsUrl))
        {
            return;
        }

        if (!HlsVariantCache.TryGetValue(result.HlsUrl, out HlsVariant variant))
        {
            variant = ProbeHighestHlsVariant(result.HlsUrl, referer, cookie, userAgent);
            if (!string.IsNullOrWhiteSpace(variant.Url))
            {
                HlsVariantCache[result.HlsUrl] = variant;
            }
        }
        if (string.IsNullOrWhiteSpace(variant.Url))
        {
            return;
        }

        result.HlsUrl = variant.Url;
        if (variant.Height > 0)
        {
            result.Resolution = variant.Width > 0 ? $"{variant.Width}x{variant.Height}" : $"{variant.Height}p";
        }
        if (variant.Bandwidth > 0)
        {
            result.Bitrate = StreamQualityCatalog.FormatBitrate(variant.Bandwidth);
        }
    }

    private static HlsVariant ProbeHighestHlsVariant(string playlistUrl, string referer, string? cookie, string userAgent)
    {
        try
        {
            using HttpClient client = CreateHttpClient(timeoutSeconds: PlaylistTimeoutSeconds);
            using HttpRequestMessage request = new(HttpMethod.Get, playlistUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", string.IsNullOrWhiteSpace(Configurations.UserAgent.Get()) ? userAgent : Configurations.UserAgent.Get());
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.apple.mpegurl,application/x-mpegURL,*/*");
            request.Headers.TryAddWithoutValidation("Referer", referer);
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            using HttpResponseMessage response = client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return default;
            }

            string playlist = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return ParseHighestHlsVariant(playlistUrl, playlist);
        }
        catch
        {
            return default;
        }
    }

    internal static HlsVariant ParseHighestHlsVariant(string playlistUrl, string? playlist)
    {
        if (string.IsNullOrWhiteSpace(playlist)
            || !Uri.TryCreate(playlistUrl, UriKind.Absolute, out Uri? baseUri))
        {
            return default;
        }

        string[] lines = playlist.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        List<HlsVariant> variants = [];
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (!line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? variantPath = lines.Skip(index + 1).Select(value => value.Trim()).FirstOrDefault(value => value.Length > 0 && !value.StartsWith('#'));
            Uri? variantUri = string.IsNullOrWhiteSpace(variantPath) ? null : ResolveHlsVariantUri(baseUri, variantPath);
            if (variantUri == null)
            {
                continue;
            }

            double bandwidth = ParseHlsAttributeNumber(line, "AVERAGE-BANDWIDTH") ?? ParseHlsAttributeNumber(line, "BANDWIDTH") ?? 0;
            Match resolution = Regex.Match(line, @"(?:^|,)RESOLUTION=(\d+)x(\d+)", RegexOptions.IgnoreCase);
            int width = resolution.Success && int.TryParse(resolution.Groups[1].Value, out int parsedWidth) ? parsedWidth : 0;
            int height = resolution.Success && int.TryParse(resolution.Groups[2].Value, out int parsedHeight) ? parsedHeight : 0;
            variants.Add(new HlsVariant(variantUri.ToString(), width, height, bandwidth));
        }

        return variants
            .OrderByDescending(variant => variant.Height)
            .ThenByDescending(variant => variant.Bandwidth)
            .FirstOrDefault();
    }

    private static Uri? ResolveHlsVariantUri(Uri baseUri, string variantPath)
    {
        if (!Uri.TryCreate(baseUri, variantPath, out Uri? variantUri))
        {
            return null;
        }

        bool isAbsoluteVariant = Uri.TryCreate(variantPath, UriKind.Absolute, out _);
        bool hasSameOrigin = variantUri.Scheme.Equals(baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && variantUri.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase)
            && variantUri.Port == baseUri.Port;
        if (isAbsoluteVariant
            || !hasSameOrigin
            || string.IsNullOrWhiteSpace(baseUri.Query)
            || !string.IsNullOrWhiteSpace(variantUri.Query))
        {
            return variantUri;
        }

        UriBuilder builder = new(variantUri)
        {
            Query = baseUri.Query.TrimStart('?'),
        };
        return builder.Uri;
    }

    private static double? ParseHlsAttributeNumber(string line, string attribute)
    {
        Match match = Regex.Match(line, $@"(?:^|[:,]){Regex.Escape(attribute)}=(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    private static bool TryResolveRedirect(string url, out string? redirected)
    {
        redirected = null;

        try
        {
            using HttpClient client = CreateHttpClient(timeoutSeconds: RedirectTimeoutSeconds);
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");
            using HttpResponseMessage response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
            redirected = response.RequestMessage?.RequestUri?.ToString();
            return !string.IsNullOrWhiteSpace(redirected);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDirectStream(Uri uri)
    {
        string path = uri.AbsolutePath;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".flv", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("rtmp", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("rtmps", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractDouyinRoomId(Uri uri)
    {
        string[] segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length == 0)
        {
            return null;
        }

        int liveIndex = Array.FindIndex(segments, segment => segment.Equals("live", StringComparison.OrdinalIgnoreCase));
        if (liveIndex >= 0 && liveIndex < segments.Length - 1)
        {
            return segments[liveIndex + 1];
        }

        return segments[^1];
    }

    private static string? TryFetchDouyinProfileAvatar(string roomUrl, string? liveHtml)
    {
        foreach (string profileUrl in GetDouyinProfileCandidates(roomUrl, liveHtml))
        {
            string? profileHtml = RequestText(
                profileUrl,
                "https://www.douyin.com/",
                PlatformCookieStore.GetCookie("Douyin", Configurations.CookieChina.Get()),
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");
            string? avatar = ExtractFirstAvatar(NormalizeEscapedText(profileHtml ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(avatar))
            {
                return avatar;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetDouyinProfileCandidates(string roomUrl, string? html)
    {
        HashSet<string> candidates = [];
        string normalized = NormalizeEscapedText(html ?? string.Empty);

        foreach (Match match in DouyinProfileUrlRegex.Matches(normalized))
        {
            if (candidates.Add(match.Value))
            {
                yield return match.Value;
            }
        }

        foreach (Match match in DouyinSecUidRegex.Matches(normalized))
        {
            string profileUrl = $"https://www.douyin.com/user/{match.Groups[1].Value}";
            if (candidates.Add(profileUrl))
            {
                yield return profileUrl;
            }
        }

        if (Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            string? id = uri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(id) && !id.All(char.IsDigit))
            {
                string profileUrl = $"https://www.douyin.com/user/{id}";
                if (candidates.Add(profileUrl))
                {
                    yield return profileUrl;
                }
            }
        }
    }

    private static bool? ExtractLiveStatus(string normalizedText)
    {
        if (Regex.IsMatch(normalizedText, "\"(?:status_str|liveStatus|live_status|status)\"\\s*:\\s*\"?(?:2|1|LIVE|living|on)\"?", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(normalizedText, "\"(?:status_str|liveStatus|live_status|status)\"\\s*:\\s*\"?(?:4|0|OFFLINE|offline|end|ended)\"?", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static string? ExtractDouyinLiveTitle(string rawText, string normalizedText)
    {
        return ExtractBestLiveTitle(
            normalizedText,
            ["room_title", "roomTitle", "liveTitle", "live_title", "title"],
            [
                "stream_url",
                "hls_pull_url_map",
                "flv_pull_url",
                "live_core_sdk_data",
                "pull_datas",
                "status_str",
                "\"status\":2",
                "web_rid",
                "anchor_name",
                "avatar_thumb",
                "room_id",
            ]);
    }

    private static string? ExtractTiktokLiveTitle(string rawText, string normalizedText)
    {
        string? sigiState = ExtractScriptContent(rawText, "SIGI_STATE");
        string? title = ExtractJsonStringByPath(
            sigiState,
            ["LiveRoom", "liveRoomUserInfo", "liveRoom", "title"],
            ["LiveRoom", "liveRoom", "title"]);
        if (!IsRejectedTitle(title))
        {
            return title;
        }

        return ExtractBestLiveTitle(
            normalizedText,
            ["room_title", "roomTitle", "liveTitle", "live_title", "title"],
            [
                "LiveRoom",
                "liveRoomUserInfo",
                "liveRoom",
                "streamData",
                "pull_data",
                "avatarThumb",
                "uniqueId",
                "\"status\":\"2\"",
            ]);
    }

    private static string? ExtractBestLiveTitle(string normalizedText, string[] keys, string[] contextMarkers)
    {
        TitleCandidate? best = null;

        foreach (string key in keys)
        {
            foreach (Match match in JsonStringRegex(key).Matches(normalizedText))
            {
                string value = CleanText(match.Groups[1].Value);
                if (IsRejectedTitle(value))
                {
                    continue;
                }

                int score = GetTitleKeyScore(key) + GetTitleContextScore(normalizedText, match.Index, contextMarkers);
                if (key.Equals("title", StringComparison.OrdinalIgnoreCase) && score < 40)
                {
                    continue;
                }

                TitleCandidate candidate = new(value, score, match.Index);
                if (best == null
                    || candidate.Score > best.Value.Score
                    || (candidate.Score == best.Value.Score && candidate.Index < best.Value.Index))
                {
                    best = candidate;
                }
            }
        }

        return best?.Value;
    }

    private static int GetTitleKeyScore(string key)
    {
        return key switch
        {
            "room_title" or "roomTitle" => 120,
            "liveTitle" or "live_title" => 110,
            _ => 0,
        };
    }

    private static int GetTitleContextScore(string text, int index, string[] markers)
    {
        const int radius = 2500;
        int start = Math.Max(0, index - radius);
        int length = Math.Min(text.Length - start, radius * 2);
        string context = text.Substring(start, length);
        int score = 0;

        foreach (string marker in markers)
        {
            if (context.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                score += marker.Contains("stream", StringComparison.OrdinalIgnoreCase)
                    || marker.Contains("pull", StringComparison.OrdinalIgnoreCase)
                    || marker.Contains("status", StringComparison.OrdinalIgnoreCase)
                    ? 40
                    : 20;
            }
        }

        return score;
    }

    private static string? ExtractScriptContent(string text, string scriptId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Regex regex = new(
            $"<script[^>]*\\bid=[\"']{Regex.Escape(scriptId)}[\"'][^>]*>(.*?)</script>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Match match = regex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string? ExtractJsonStringByPath(string? jsonText, params string[][] paths)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return null;
        }

        try
        {
            JToken root = JToken.Parse(jsonText);
            foreach (string[] path in paths)
            {
                JToken? token = root;
                foreach (string segment in path)
                {
                    token = token?[segment];
                }

                string? value = CleanOptionalText(token?.ToString());
                if (!IsRejectedTitle(value))
                {
                    return value;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ExtractFirstJsonString(string normalizedText, params string[] keys)
    {
        foreach (string key in keys)
        {
            Match match = JsonStringRegex(key).Match(normalizedText);
            if (match.Success)
            {
                string value = CleanText(match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ExtractFirstAvatar(string normalizedText)
    {
        foreach (Regex regex in AvatarRegexes)
        {
            Match match = regex.Match(normalizedText);
            if (match.Success)
            {
                string value = CleanUrl(match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static StreamCandidate ExtractPreferredStreamCandidate(string normalizedText, string streamKind, string extension, string? preferredQuality)
    {
        Dictionary<string, string> urls = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in StreamUrlRegex(extension).Matches(normalizedText))
        {
            string quality = match.Groups["quality"].Success ? match.Groups["quality"].Value : string.Empty;
            string value = CleanUrl(match.Groups["url"].Value);

            if (string.IsNullOrWhiteSpace(value) || !value.Contains(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            urls[quality] = value;
            if (string.IsNullOrWhiteSpace(quality))
            {
                urls[streamKind] = value;
            }
        }

        StreamCandidate highestQuality = SelectHighestObservableQuality(urls, preferredQuality);
        if (!string.IsNullOrWhiteSpace(highestQuality.Url))
        {
            return highestQuality;
        }

        foreach (string key in StreamQualityCatalog.GetStreamKeyOrder(preferredQuality).Concat([streamKind, string.Empty]))
        {
            if (urls.TryGetValue(key, out string? url))
            {
                string? quality = string.IsNullOrWhiteSpace(key) || key.Equals(streamKind, StringComparison.OrdinalIgnoreCase) ? null : key;
                return new StreamCandidate(url, quality);
            }
        }

        KeyValuePair<string, string> first = urls.FirstOrDefault();
        return string.IsNullOrWhiteSpace(first.Value)
            ? default
            : new StreamCandidate(first.Value, string.IsNullOrWhiteSpace(first.Key) ? null : first.Key);
    }

    private static StreamCandidate SelectPreferredStream(JToken? map, string? preferredQuality)
    {
        if (map is not JObject obj)
        {
            return default;
        }

        Dictionary<string, string> urls = new(StringComparer.OrdinalIgnoreCase);
        foreach (JProperty property in obj.Properties())
        {
            string? url = CleanOptionalUrl(property.Value.ToString());
            if (!string.IsNullOrWhiteSpace(url))
            {
                urls[property.Name] = url;
            }
        }


        StreamCandidate highestQuality = SelectHighestObservableQuality(urls, preferredQuality);
        if (!string.IsNullOrWhiteSpace(highestQuality.Url))
        {
            return highestQuality;
        }

        foreach (string key in StreamQualityCatalog.GetStreamKeyOrder(preferredQuality).Append(string.Empty))
        {
            if (urls.TryGetValue(key, out string? url))
            {
                return new StreamCandidate(url, string.IsNullOrWhiteSpace(key) ? null : key);
            }
        }

        KeyValuePair<string, string> first = urls.FirstOrDefault();
        return string.IsNullOrWhiteSpace(first.Value)
            ? default
            : new StreamCandidate(first.Value, first.Key);
    }

    private static StreamCandidate SelectHighestObservableQuality(IReadOnlyDictionary<string, string> urls, string? preferredQuality)
    {
        if (StreamQualityCatalog.NormalizePreference(preferredQuality) != StreamQualityCatalog.Original)
        {
            return default;
        }

        var candidates = urls
            .Select(pair =>
            {
                (int height, double bitrate) = StreamMetadataParser.GetQualityMetrics(pair.Value);
                return new { pair.Key, Url = pair.Value, Height = height, Bitrate = bitrate };
            })
            .Where(candidate => candidate.Height > 0 || candidate.Bitrate > 0)
            .OrderByDescending(candidate => candidate.Height)
            .ThenByDescending(candidate => candidate.Bitrate)
            .ThenBy(candidate => Array.IndexOf(StreamQualityCatalog.GetStreamKeyOrder(StreamQualityCatalog.Original).ToArray(), candidate.Key))
            .FirstOrDefault();

        return candidates == null
            ? default
            : new StreamCandidate(candidates.Url, string.IsNullOrWhiteSpace(candidates.Key) ? null : candidates.Key);
    }

    private static string? ExtractFirstUrlFromList(JToken? token)
    {
        return token is JArray array
            ? array.Select(item => CleanOptionalUrl(item.ToString())).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url))
            : null;
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(value, out int result) ? result : null;
    }

    private static string NormalizeEscapedText(string value)
    {
        string decoded = WebUtility.HtmlDecode(value)
            .Replace("\\u0026", "&", StringComparison.Ordinal)
            .Replace("\\/", "/", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\u0026", "&", StringComparison.Ordinal);
        return UnicodeEscapeRegex.Replace(
            decoded,
            match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
    }

    private static string CleanText(string value)
    {
        return NormalizeEscapedText(value).Trim();
    }

    private static string? CleanOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : CleanText(value);
    }

    private static string CleanUrl(string value)
    {
        return CleanText(value)
            .Replace("\\", string.Empty, StringComparison.Ordinal)
            .Replace("amp;", string.Empty, StringComparison.Ordinal);
    }

    private static string? CleanOptionalUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : CleanUrl(value);
    }

    private static bool IsRejectedTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string normalized = value.Trim();
        return normalized.Length > 120
            || normalized.Contains("://", StringComparison.Ordinal)
            || normalized.Contains('<', StringComparison.Ordinal)
            || normalized.Contains('\n', StringComparison.Ordinal)
            || normalized.Contains("\u7528\u6237\u670d\u52a1\u534f\u8bae", StringComparison.Ordinal)
            || normalized.Contains("\u9690\u79c1\u653f\u7b56", StringComparison.Ordinal)
            || normalized.Contains("\u5e7f\u544a\u6295\u653e", StringComparison.Ordinal)
            || normalized.Contains("\u521b\u4f5c\u8005\u670d\u52a1\u5e73\u53f0", StringComparison.Ordinal)
            || normalized.Contains("\u76f4\u64ad\u4f34\u4fa3", StringComparison.Ordinal)
            || normalized.Contains("\u6296\u97f3\u5f00\u653e\u5e73\u53f0", StringComparison.Ordinal)
            || normalized.Contains("\u9875\u9762\u4e0d\u5b58\u5728", StringComparison.Ordinal)
            || normalized.Contains("\u9a8c\u8bc1\u7801", StringComparison.Ordinal)
            || normalized.Contains("\u5b89\u5168\u9a8c\u8bc1", StringComparison.Ordinal)
            || normalized.Contains("\u670d\u52a1\u534f\u8bae", StringComparison.Ordinal)
            || normalized.Contains("\u670d\u52a1\u6761\u6b3e", StringComparison.Ordinal)
            || normalized.Equals("\u6296\u97f3", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Douyin", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("TikTok", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("\u6296\u97f3\u76f4\u64ad", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\u76f4\u64ad\u95f4 - \u6296\u97f3", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBadTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string normalized = value.Trim();
        return normalized.Length > 120
            || normalized.Contains("广告投放", StringComparison.Ordinal)
            || normalized.Equals("抖音", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("TikTok", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("直播间 - 抖音", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static void SetLastError(string url, string error)
    {
        string key = NormalizeUrl(url, allowNetwork: false) ?? url.Trim();
        LastErrors[key] = error;
    }

    private static Regex JsonStringRegex(string key)
    {
        return new Regex($"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
    }

    private static Regex StreamUrlRegex(string extension)
    {
        string escapedExtension = Regex.Escape(extension);
        return new Regex(
            "\"(?<quality>[A-Z0-9_]{0,16})\"\\s*:\\s*\"(?<url>https?:[^\"\\s]*" + escapedExtension + "[^\"\\s]*)\"|\"(?<url>https?:[^\"\\s]*" + escapedExtension + "[^\"\\s]*)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    [GeneratedRegex("https://www\\.douyin\\.com/user/[A-Za-z0-9_\\-]+")]
    private static partial Regex DouyinProfileUrlRegex { get; }

    [GeneratedRegex("\"(?:sec_uid|secUid|sec_user_id)\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex DouyinSecUidRegex { get; }

    [GeneratedRegex("\"avatar_thumb\"\\s*:\\s*\\{[^\\{\\}]*\"url_list\"\\s*:\\s*\\[\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AvatarThumbRegex { get; }

    [GeneratedRegex("\"avatar_thumb\".*?\"url_list\"\\s*:\\s*\\[\"([^\"]+)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AvatarThumbBroadRegex { get; }

    [GeneratedRegex("\"avatarThumb\"\\s*:\\s*\\{[^\\{\\}]*\"urlList\"\\s*:\\s*\\[\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AvatarThumbCamelRegex { get; }

    [GeneratedRegex("\"avatarThumb\".*?\"urlList\"\\s*:\\s*\\[\"([^\"]+)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AvatarThumbCamelBroadRegex { get; }

    [GeneratedRegex("\"avatarMedium\".*?\"urlList\"\\s*:\\s*\\[\"([^\"]+)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AvatarMediumRegex { get; }

    [GeneratedRegex("\"avatarLarger\".*?\"urlList\"\\s*:\\s*\\[\"([^\"]+)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AvatarLargerRegex { get; }

    [GeneratedRegex("\"(?:avatar|avatar_url|avatarUrl)\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AvatarUrlRegex { get; }

    [GeneratedRegex(@"\\u([0-9a-fA-F]{4})")]
    private static partial Regex UnicodeEscapeRegex { get; }

    private static Regex[] AvatarRegexes { get; } =
    [
        AvatarThumbRegex,
        AvatarThumbBroadRegex,
        AvatarThumbCamelRegex,
        AvatarThumbCamelBroadRegex,
        AvatarMediumRegex,
        AvatarLargerRegex,
        AvatarUrlRegex,
    ];

    private readonly record struct TitleCandidate(string Value, int Score, int Index);

    private readonly record struct StreamCandidate(string? Url, string? Quality);
}

internal readonly record struct HlsVariant(string? Url, int Width, int Height, double Bandwidth);

internal interface IStreamMetadataResult
{
    public string? Title { get; }

    public string? Quality { get; }

    public string? Resolution { get; }

    public string? Bitrate { get; }

    public string? Headers { get; }
}

internal sealed class StreamResolverResult : ISpiderResult, IStreamMetadataResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }

    public string? Title { get; set; }

    public string? Quality { get; set; }

    public string? Resolution { get; set; }

    public string? Bitrate { get; set; }

    public string? Headers { get; set; }
}

internal static class SpiderResultMetadata
{
    public static string? GetTitle(ISpiderResult? result)
    {
        return GetMetadataValue(result, nameof(IStreamMetadataResult.Title));
    }

    public static string? GetQuality(ISpiderResult? result)
    {
        string? value = GetMetadataValue(result, nameof(IStreamMetadataResult.Quality));
        return !string.IsNullOrWhiteSpace(value) ? value : StreamMetadataParser.GetQuality(result?.FlvUrl, result?.HlsUrl);
    }

    public static string? GetResolution(ISpiderResult? result)
    {
        string? value = GetMetadataValue(result, nameof(IStreamMetadataResult.Resolution));
        return !string.IsNullOrWhiteSpace(value) ? value : StreamMetadataParser.GetResolution(result?.FlvUrl, result?.HlsUrl);
    }

    public static string? GetBitrate(ISpiderResult? result)
    {
        string? value = GetMetadataValue(result, nameof(IStreamMetadataResult.Bitrate));
        return !string.IsNullOrWhiteSpace(value) ? value : StreamMetadataParser.GetBitrate(result?.FlvUrl, result?.HlsUrl);
    }

    public static string? GetHeaders(ISpiderResult? result)
    {
        return GetMetadataValue(result, nameof(IStreamMetadataResult.Headers));
    }

    private static string? GetMetadataValue(ISpiderResult? result, string propertyName)
    {
        if (result == null)
        {
            return null;
        }

        if (result is IStreamMetadataResult metadata)
        {
            return propertyName switch
            {
                nameof(IStreamMetadataResult.Title) => metadata.Title,
                nameof(IStreamMetadataResult.Quality) => metadata.Quality,
                nameof(IStreamMetadataResult.Resolution) => metadata.Resolution,
                nameof(IStreamMetadataResult.Bitrate) => metadata.Bitrate,
                nameof(IStreamMetadataResult.Headers) => metadata.Headers,
                _ => null,
            };
        }

        PropertyInfo? property = result.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        return property?.GetValue(result) as string;
    }
}
