using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Emerde.Core;

internal static class ExternalStreamResolver
{
    private static readonly ConcurrentDictionary<string, string> LastErrorsByUrl = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex UrlCandidateRegex = new(
        "(?:https?|rtmps?)://[^\\s<>\"'\\u2018\\u2019\\u201c\\u201d\\u3001\\u3002\\u300a\\u300b\\u3010\\u3011\\uff08\\uff09\\uff0c\\uff1a\\uff1b\\uff01\\uff1f]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> ShortLinkPlatforms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["v.douyin.com"] = "Douyin",
        ["www.iesdouyin.com"] = "Douyin",
        ["iesdouyin.com"] = "Douyin",
        ["vm.tiktok.com"] = "TikTok",
        ["vt.tiktok.com"] = "TikTok",
        ["b23.tv"] = "Bilibili",
        ["v.kuaishou.com"] = "Kuaishou",
        ["s.kuaishou.com"] = "Kuaishou",
        ["xhslink.com"] = "Xiaohongshu",
        ["slink.bigovideo.tv"] = "Bigo",
        ["3.cn"] = "JD",
        ["t.cn"] = "Weibo",
    };

    public static string LastError { get; private set; } = string.Empty;

    public static Task WarmUpAsync()
    {
        return Task.Run(() =>
        {
            _ = Spider.SupportedPlatformNames.Count;
        });
    }

    public static string GetLastError(string? url)
    {
        foreach (string key in GetErrorKeys(url))
        {
            if (LastErrorsByUrl.TryGetValue(key, out string? error))
            {
                return error ?? string.Empty;
            }
        }

        return string.IsNullOrWhiteSpace(url) ? LastError : string.Empty;
    }

    public static string GetPlatformName(string? url)
    {
        foreach (string candidate in GetUrlCandidates(url))
        {
            string candidateWithScheme = EnsureScheme(candidate);
            if (Uri.TryCreate(candidateWithScheme, UriKind.Absolute, out Uri? uri)
                && GetShortLinkPlatform(uri) is string shortLinkPlatform)
            {
                return shortLinkPlatform;
            }

            string value = NormalizeCandidate(candidate) ?? candidate;
            string platformName = StreamResolver.GetPlatformName(value);
            platformName = string.IsNullOrWhiteSpace(platformName) ? Spider.GetLegacyPlatformName(value) : platformName;
            if (!string.IsNullOrWhiteSpace(platformName))
            {
                return platformName;
            }
        }

        return string.Empty;
    }

    public static bool HasRoomData(ISpiderResult? result)
    {
        return StreamResolver.HasRoomData(result);
    }

    public static bool HasConclusiveData(ISpiderResult? result)
    {
        return StreamResolver.HasConclusiveData(result);
    }

    internal static bool IsSameRoom(
        string? firstUrl,
        string? firstPlatformName,
        string? firstUid,
        string? secondUrl,
        string? secondPlatformName,
        string? secondUid)
    {
        string firstNormalizedUrl = NormalizeUrl(firstUrl) ?? firstUrl?.Trim() ?? string.Empty;
        string secondNormalizedUrl = NormalizeUrl(secondUrl) ?? secondUrl?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(firstNormalizedUrl)
            && string.Equals(firstNormalizedUrl, secondNormalizedUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string firstPlatform = string.IsNullOrWhiteSpace(firstPlatformName)
            ? GetPlatformName(firstNormalizedUrl)
            : firstPlatformName.Trim();
        string secondPlatform = string.IsNullOrWhiteSpace(secondPlatformName)
            ? GetPlatformName(secondNormalizedUrl)
            : secondPlatformName.Trim();
        return !string.IsNullOrWhiteSpace(firstPlatform)
            && string.Equals(firstPlatform, secondPlatform, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(firstUid)
            && !string.IsNullOrWhiteSpace(secondUid)
            && string.Equals(firstUid.Trim(), secondUid.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string? NormalizeUrl(string? url, bool allowNetwork = false, CancellationToken cancellationToken = default)
    {
        string[] candidates = GetUrlCandidates(url).ToArray();
        foreach (string candidate in candidates)
        {
            string? normalizedUrl = NormalizeCandidate(candidate);
            if (!string.IsNullOrWhiteSpace(normalizedUrl))
            {
                return normalizedUrl;
            }
        }

        if (!allowNetwork)
        {
            return null;
        }

        foreach (string candidate in candidates)
        {
            string? normalizedUrl = NormalizeRedirectCandidate(candidate, cancellationToken);
            if (!string.IsNullOrWhiteSpace(normalizedUrl))
            {
                return normalizedUrl;
            }
        }

        return null;
    }

    internal static bool IsPersistableRoomUrl(string? url)
    {
        string? normalizedUrl = NormalizeUrl(url);
        return !string.IsNullOrWhiteSpace(normalizedUrl)
            && Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri)
            && GetShortLinkPlatform(uri) == null
            && !StreamResolver.TryExtractDouyinReflowIdentity(uri, out _, out _);
    }

    internal static IEnumerable<string> GetUrlCandidates(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            yield break;
        }

        string value = input.Trim();
        MatchCollection matches = UrlCandidateRegex.Matches(value);
        if (matches.Count == 0)
        {
            yield return value;
            yield break;
        }

        foreach (Match match in matches)
        {
            string candidate = match.Value.TrimEnd('.', ',', ';', '!', ')', ']', '}');
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string? NormalizeCandidate(string candidate)
    {
        string value = EnsureScheme(candidate);
        string? resolverNormalized = StreamResolver.NormalizeUrl(value);
        if (!string.IsNullOrWhiteSpace(resolverNormalized))
        {
            return resolverNormalized;
        }

        return NormalizeKnownPlatformUrl(value);
    }

    private static string? NormalizeRedirectCandidate(string candidate, CancellationToken cancellationToken)
    {
        string value = EnsureScheme(candidate);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || GetShortLinkPlatform(uri) == null
            || !StreamResolver.CanResolveRedirect(uri)
            || !StreamResolver.TryResolveRedirect(value, cancellationToken, out string? redirected)
            || string.IsNullOrWhiteSpace(redirected)
            || string.Equals(value, redirected, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NormalizeCandidate(redirected);
    }

    private static string EnsureScheme(string value)
    {
        return value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
    }

    private static string? GetShortLinkPlatform(Uri uri)
    {
        string host = uri.Host;
        if (ShortLinkPlatforms.TryGetValue(host, out string? platformName))
        {
            return platformName;
        }

        if (host.Equals("tb.cn", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".tb.cn", StringComparison.OrdinalIgnoreCase))
        {
            return "Taobao";
        }

        if (host.Equals("shp.ee", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".shp.ee", StringComparison.OrdinalIgnoreCase))
        {
            return "Shopee";
        }

        return null;
    }

    public static ISpiderResult? GetResult(
        string url,
        string? streamQuality = null,
        bool bypassDouyinThrottle = false,
        bool prioritizeDouyin = false,
        bool allowDouyinWebViewFallback = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? normalizedUrl = NormalizeUrl(url, allowNetwork: true, cancellationToken) ?? Spider.ParseLegacyUrl(url);
        ClearLastError(url, normalizedUrl);
        string lastError;

        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            SetLastError(url, normalizedUrl, "empty or invalid url");
            return null;
        }

        ISpiderResult? resolverResult = StreamResolver.GetResult(normalizedUrl, streamQuality, bypassDouyinThrottle, prioritizeDouyin, allowDouyinWebViewFallback, cancellationToken);
        if (!StreamResolver.NeedsSupplementalData(resolverResult))
        {
            SetLastError(url, normalizedUrl, string.Empty);
            return StreamResolver.MergeResults(normalizedUrl, resolverResult);
        }

        lastError = StreamResolver.GetLastError(normalizedUrl);
        if (StreamResolver.IsTransientDouyinFailure(lastError))
        {
            SetLastError(url, normalizedUrl, lastError);
            return resolverResult;
        }

        cancellationToken.ThrowIfCancellationRequested();
        ISpiderResult? legacyResult = Spider.GetLegacyResult(normalizedUrl, streamQuality);
        StreamResolverResult result = StreamResolver.MergeResults(normalizedUrl, resolverResult, legacyResult);
        if (ShouldResolveHlsVariant(result.PlatformName, result.HlsUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StreamResolver.EnrichHighestHlsVariant(
                result,
                StreamQualityCatalog.Original,
                normalizedUrl,
                PlatformCookieStore.GetCookie("Twitch", SecretProtector.GetOverseaCookie()),
                TwitchSpider.WebUserAgent);
        }

        if (!HasRoomData(result))
        {
            lastError = StreamResolver.GetLastError(normalizedUrl);
            SetLastError(url, normalizedUrl, string.IsNullOrWhiteSpace(lastError) ? "stream resolver returned no room data" : lastError);
            return null;
        }

        if (!StreamResolver.NeedsSupplementalData(result))
        {
            SetLastError(url, normalizedUrl, string.Empty);
        }
        else
        {
            lastError = StreamResolver.GetLastError(normalizedUrl);
            SetLastError(url, normalizedUrl, string.IsNullOrWhiteSpace(lastError) ? "room state was inconclusive" : lastError);
        }

        return result;
    }

    internal static bool ShouldResolveHlsVariant(string? platformName, string? hlsUrl)
    {
        return platformName?.Equals("Twitch", StringComparison.OrdinalIgnoreCase) == true
            && !string.IsNullOrWhiteSpace(hlsUrl);
    }

    private static string? NormalizeKnownPlatformUrl(string value)
    {
        return Spider.ParseLegacyUrl(value);
    }

    private static string SetLastError(string? originalUrl, string? normalizedUrl, string error)
    {
        LastError = error;

        foreach (string key in GetErrorKeys(originalUrl).Concat(GetErrorKeys(normalizedUrl)))
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                _ = LastErrorsByUrl.TryRemove(key, out _);
            }
            else
            {
                LastErrorsByUrl[key] = error;
            }
        }

        return error;
    }

    internal static void ClearLastError(string? originalUrl, string? normalizedUrl = null)
    {
        foreach (string key in GetErrorKeys(originalUrl).Concat(GetErrorKeys(normalizedUrl)))
        {
            _ = LastErrorsByUrl.TryRemove(key, out _);
        }

        StreamResolver.ClearLastError(originalUrl);
        StreamResolver.ClearLastError(normalizedUrl);
    }

    internal static void ClearDouyinThrottle(string? originalUrl, string? normalizedUrl = null)
    {
        StreamResolver.ClearDouyinThrottle(originalUrl);
        StreamResolver.ClearDouyinThrottle(normalizedUrl);
    }

    internal static void ClearRoomState(string? originalUrl, string? normalizedUrl = null)
    {
        ClearLastError(originalUrl, normalizedUrl);
        ClearDouyinThrottle(originalUrl, normalizedUrl);
    }

    private static IEnumerable<string> GetErrorKeys(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            yield break;
        }

        string key = url.Trim();
        yield return key;

        string? normalizedUrl = NormalizeUrl(key);
        if (!string.IsNullOrWhiteSpace(normalizedUrl) && !normalizedUrl.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            yield return normalizedUrl;
        }
    }
}
