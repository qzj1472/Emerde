using System.Collections.Concurrent;

namespace Emerde.Core;

internal static class ExternalStreamResolver
{
    private static readonly ConcurrentDictionary<string, string> LastErrorsByUrl = new(StringComparer.OrdinalIgnoreCase);

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
        string? normalizedUrl = NormalizeUrl(url);
        string candidate = normalizedUrl ?? url?.Trim() ?? string.Empty;
        string platformName = StreamResolver.GetPlatformName(candidate);
        return string.IsNullOrWhiteSpace(platformName) ? Spider.GetLegacyPlatformName(candidate) : platformName;
    }

    public static bool HasRoomData(ISpiderResult? result)
    {
        return StreamResolver.HasRoomData(result);
    }

    public static bool HasConclusiveData(ISpiderResult? result)
    {
        return StreamResolver.HasConclusiveData(result);
    }

    public static string? NormalizeUrl(string? url, bool allowNetwork = false)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string value = url.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        string? resolverNormalized = StreamResolver.NormalizeUrl(value, allowNetwork);
        if (!string.IsNullOrWhiteSpace(resolverNormalized))
        {
            return resolverNormalized;
        }

        return NormalizeKnownPlatformUrl(value);
    }

    public static ISpiderResult? GetResult(string url, string? streamQuality = null, bool bypassDouyinThrottle = false, bool prioritizeDouyin = false)
    {
        string? normalizedUrl = NormalizeUrl(url, allowNetwork: true) ?? Spider.ParseLegacyUrl(url);
        ClearLastError(url, normalizedUrl);
        string lastError;

        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            SetLastError(url, normalizedUrl, "empty or invalid url");
            return null;
        }

        ISpiderResult? resolverResult = StreamResolver.GetResult(normalizedUrl, streamQuality, bypassDouyinThrottle, prioritizeDouyin);
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

        ISpiderResult? legacyResult = Spider.GetLegacyResult(normalizedUrl, streamQuality);
        StreamResolverResult result = StreamResolver.MergeResults(normalizedUrl, resolverResult, legacyResult);

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

    private static string? NormalizeKnownPlatformUrl(string value)
    {
        return DouyuSpider.Instance.Value.ParseUrl(value)
            ?? TwitchSpider.Instance.Value.ParseUrl(value);
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
