using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json.Linq;

namespace Emerde.Core;

internal static partial class StreamResolver
{
    private const int RequestTimeoutSeconds = 5;
    private const int RedirectTimeoutSeconds = 3;
    private const int PlaylistTimeoutSeconds = 2;
    private const int HlsVariantCacheLimit = 256;
    internal const int DouyinResolverConcurrency = 6;
    internal const int DouyinResolverQueueTimeoutMilliseconds = 5000;
    internal const int DouyinRequestSpacingMilliseconds = 200;
    internal const string DouyinTransientBlockError = "Douyin room data was empty or blocked.";
    internal const string DouyinResolverBusyError = "Douyin resolver queue was busy.";
    internal const string DouyinGlobalCircuitOpenError = "Douyin background requests were paused after repeated blocking responses.";
    internal const string DouyinInconclusiveError = "Douyin room state was inconclusive.";
    private const string DouyinDefaultCookie = "ttwid=1%7C2iDIYVmjzMcpZ20fcaFde0VghXAA3NaNXE_SLR68IyE%7C1761045455%7Cab35197d5cfb21df6cbb2fa7ef1c9262206b062c315b9d04da746d0b37dfbc7d";
    private const string DouyinWebUserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.5845.97 Safari/537.36 Core/1.116.567.400 QQBrowser/19.7.6764.400";
    private static readonly TimeSpan HlsVariantPositiveCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HlsVariantNegativeCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<string, string> LastErrors = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, HlsVariantCacheEntry> HlsVariantCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DouyinThrottleState> DouyinThrottleStates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DouyinRoomSession> DouyinRoomSessions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim DouyinResolverSemaphore = new(DouyinResolverConcurrency, DouyinResolverConcurrency);
    private static readonly object DouyinThrottleSync = new();
    private static readonly Queue<long> DouyinBlockingResponses = new();
    private static long douyinNextRequestAt;
    private static long douyinBackgroundBlockedUntil;
    private static long douyinNextWebViewAt;

    public static string GetLastError(string url)
    {
        string key = NormalizeUrl(url, allowNetwork: false) ?? url.Trim();
        return LastErrors.TryGetValue(key, out string? error) ? error : string.Empty;
    }

    internal static void ClearLastError(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string key = NormalizeUrl(url, allowNetwork: false) ?? url.Trim();
        _ = LastErrors.TryRemove(key, out _);
    }

    internal static void ClearDouyinThrottle(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string key = NormalizeUrl(url, allowNetwork: false) ?? url.Trim();
        _ = DouyinThrottleStates.TryRemove(key, out _);
        _ = DouyinRoomSessions.TryRemove(key, out _);
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
        if (HostMatchesDomain(host, "douyin.com") || HostMatchesDomain(host, "iesdouyin.com"))
        {
            return "Douyin";
        }

        if (HostMatchesDomain(host, "tiktok.com"))
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
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

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
                : null;
        }

        if (HostMatchesDomain(host, "douyin.com"))
        {
            string? roomId = ExtractDouyinRoomId(uri);
            return string.IsNullOrWhiteSpace(roomId) ? null : $"https://live.douyin.com/{roomId}";
        }

        if (host is "vm.tiktok.com" or "vt.tiktok.com")
        {
            return allowNetwork && TryResolveRedirect(trimmed, out string? redirected)
                ? NormalizeUrl(redirected ?? string.Empty, allowNetwork: false) ?? redirected
                : null;
        }

        if (HostMatchesDomain(host, "tiktok.com"))
        {
            string? userId = uri.Segments
                .Select(segment => segment.Trim('/'))
                .FirstOrDefault(segment => segment.StartsWith("@", StringComparison.Ordinal));

            return string.IsNullOrWhiteSpace(userId) ? null : $"https://www.tiktok.com/{userId}/live";
        }

        return null;
    }

    public static ISpiderResult? GetResult(string url, string? preferredQuality = null, bool bypassDouyinThrottle = false, bool prioritizeDouyin = false)
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
            if (HostMatchesDomain(host, "douyin.com"))
            {
                if (!DouyinResolverSemaphore.Wait(DouyinResolverQueueTimeoutMilliseconds))
                {
                    SetLastError(resolverUrl, DouyinResolverBusyError);
                    return null;
                }

                try
                {
                    if (!TryWaitForDouyinRequestWindow(resolverUrl, bypassDouyinThrottle, prioritizeDouyin))
                    {
                        if (string.IsNullOrWhiteSpace(GetLastError(resolverUrl)))
                        {
                            SetLastError(resolverUrl, DouyinTransientBlockError);
                        }
                        return null;
                    }

                    StreamResolverResult? result = ResolveDouyin(resolverUrl, preferredQuality, bypassDouyinThrottle, prioritizeDouyin);
                    UpdateDouyinThrottle(resolverUrl, result, GetLastError(resolverUrl));
                    return result;
                }
                finally
                {
                    DouyinResolverSemaphore.Release();
                }
            }

            if (HostMatchesDomain(host, "tiktok.com"))
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
        return HasConclusiveData(result);
    }

    public static bool HasConclusiveData(ISpiderResult? result)
    {
        return result != null
            && (result.IsLiveStreaming.HasValue
                || !string.IsNullOrWhiteSpace(result.FlvUrl)
                || !string.IsNullOrWhiteSpace(result.HlsUrl)
                || !string.IsNullOrWhiteSpace(result.RecordUrl));
    }

    public static bool HasRoomData(ISpiderResult? result)
    {
        if (result == null)
        {
            return false;
        }

        return HasConclusiveData(result)
            || !string.IsNullOrWhiteSpace(result.Nickname)
            || !string.IsNullOrWhiteSpace(result.AvatarThumbUrl)
            || !string.IsNullOrWhiteSpace(result.Uid)
            || !string.IsNullOrWhiteSpace(SpiderResultMetadata.GetTitle(result));
    }

    internal static bool NeedsSupplementalData(ISpiderResult? result)
    {
        if (!HasConclusiveData(result))
        {
            return true;
        }

        if (result!.IsLiveStreaming == true)
        {
            return string.IsNullOrWhiteSpace(result.FlvUrl)
                && string.IsNullOrWhiteSpace(result.HlsUrl)
                && string.IsNullOrWhiteSpace(result.RecordUrl);
        }

        return result.IsLiveStreaming == false
            && string.IsNullOrWhiteSpace(result.Nickname)
            && string.IsNullOrWhiteSpace(result.Uid);
    }

    internal static StreamResolverResult MergeResults(string roomUrl, params ISpiderResult?[] results)
    {
        StreamResolverResult merged = new()
        {
            RoomUrl = roomUrl,
        };

        foreach (ISpiderResult result in results.OfType<ISpiderResult>())
        {
            merged.RoomUrl = FirstNonEmpty(merged.RoomUrl, result.RoomUrl);
            merged.PlatformName = FirstNonEmpty(merged.PlatformName, result.PlatformName);
            merged.IsLiveStreaming ??= result.IsLiveStreaming;
            merged.Nickname = FirstNonEmpty(merged.Nickname, result.Nickname);
            merged.AvatarThumbUrl = FirstNonEmpty(merged.AvatarThumbUrl, result.AvatarThumbUrl);
            merged.FlvUrl = FirstNonEmpty(merged.FlvUrl, result.FlvUrl);
            merged.HlsUrl = FirstNonEmpty(merged.HlsUrl, result.HlsUrl);
            merged.RecordUrl = FirstNonEmpty(merged.RecordUrl, result.RecordUrl);
            merged.Title = FirstNonEmpty(merged.Title, SpiderResultMetadata.GetTitle(result));
            merged.Quality = FirstNonEmpty(merged.Quality, SpiderResultMetadata.GetQuality(result));
            merged.Uid = FirstNonEmpty(merged.Uid, result.Uid);
            merged.Resolution = FirstNonEmpty(merged.Resolution, SpiderResultMetadata.GetResolution(result));
            merged.Bitrate = FirstNonEmpty(merged.Bitrate, SpiderResultMetadata.GetBitrate(result));
            merged.Headers = FirstNonEmpty(merged.Headers, SpiderResultMetadata.GetHeaders(result));
        }

        if (string.Equals(merged.PlatformName, "Douyin", StringComparison.OrdinalIgnoreCase)
            && merged.IsLiveStreaming == false)
        {
            merged.FlvUrl = null;
            merged.HlsUrl = null;
            merged.RecordUrl = null;
            merged.Quality = null;
            merged.Resolution = null;
            merged.Bitrate = null;
            merged.Headers = null;
        }

        return merged;
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
                4 => false,
                _ => null,
            };
            result.Uid = FirstNonEmpty(
                CleanOptionalText(user?["id_str"]?.ToString()),
                CleanOptionalText(user?["sec_uid"]?.ToString()),
                CleanOptionalText(room["owner"]?["id_str"]?.ToString()),
                CleanOptionalText(room["owner"]?["sec_uid"]?.ToString()));

            JToken? streamUrl = room["stream_url"];
            StreamCandidate hls = SelectPreferredStream(streamUrl?["hls_pull_url_map"], preferredQuality);
            StreamCandidate flv = SelectPreferredStream(streamUrl?["flv_pull_url"], preferredQuality);
            DouyinOriginStream originStream = SelectDouyinOriginStream(streamUrl, preferredQuality);
            if (!originStream.HasStream && string.IsNullOrWhiteSpace(hls.Url) && string.IsNullOrWhiteSpace(flv.Url))
            {
                originStream = SelectDouyinOriginStream(streamUrl, StreamQualityCatalog.Original);
            }
            result.HlsUrl = originStream.HasStream ? originStream.HlsUrl : hls.Url;
            result.FlvUrl = originStream.HasStream ? originStream.FlvUrl : flv.Url;
            result.RecordUrl = SelectDouyinRecordUrl(result.FlvUrl, result.HlsUrl);
            result.Quality = originStream.HasStream ? "ORIGIN" : FirstNonEmpty(hls.Quality, flv.Quality);
            result.Resolution = FirstNonEmpty(
                originStream.Resolution,
                StreamMetadataParser.GetResolution(result.FlvUrl, result.HlsUrl));
            result.Bitrate = originStream.Bitrate > 0
                ? StreamQualityCatalog.FormatBitrate(originStream.Bitrate)
                : StreamMetadataParser.GetBitrate(result.FlvUrl, result.HlsUrl);

            if (result.IsLiveStreaming == false)
            {
                result.HlsUrl = null;
                result.FlvUrl = null;
                result.RecordUrl = null;
                result.Quality = null;
                result.Resolution = null;
                result.Bitrate = null;
            }
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

    internal static bool IsTransientDouyinFailure(string? error)
    {
        return string.Equals(error, DouyinTransientBlockError, StringComparison.Ordinal)
            || string.Equals(error, DouyinResolverBusyError, StringComparison.Ordinal)
            || string.Equals(error, DouyinGlobalCircuitOpenError, StringComparison.Ordinal)
            || string.Equals(error, DouyinInconclusiveError, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(error)
                && error.StartsWith("Douyin request ", StringComparison.Ordinal));
    }

    internal static int GetDouyinBackoffMilliseconds(int failureCount)
    {
        int exponent = Math.Clamp(failureCount - 1, 0, 1);
        return Math.Min(MonitorTiming.LiveRoutineIntervalMilliseconds, 5000 << exponent);
    }

    internal static bool IsDouyinRequestBlocked(long currentTimestamp, long blockedUntil)
    {
        return blockedUntil > currentTimestamp;
    }

    private static bool TryWaitForDouyinRequestWindow(string resolverUrl, bool bypassDouyinThrottle, bool prioritizeDouyin)
    {
        long now = Environment.TickCount64;
        if (!bypassDouyinThrottle
            && DouyinThrottleStates.TryGetValue(resolverUrl, out DouyinThrottleState state)
            && IsDouyinRequestBlocked(now, state.BlockedUntil))
        {
            return false;
        }

        if (!bypassDouyinThrottle
            && !prioritizeDouyin
            && IsDouyinRequestBlocked(now, Volatile.Read(ref douyinBackgroundBlockedUntil)))
        {
            SetLastError(resolverUrl, DouyinGlobalCircuitOpenError);
            return false;
        }

        return true;
    }

    private static void UpdateDouyinThrottle(string resolverUrl, ISpiderResult? result, string error)
    {
        if (HasConclusiveData(result))
        {
            _ = DouyinThrottleStates.TryRemove(resolverUrl, out _);
            return;
        }

        if (!IsTransientDouyinFailure(error))
        {
            return;
        }

        long now = Environment.TickCount64;
        _ = DouyinThrottleStates.AddOrUpdate(
            resolverUrl,
            new DouyinThrottleState(now + GetDouyinBackoffMilliseconds(1), 1),
            (_, current) => CreateNextDouyinThrottleState(current, now));
    }

    private static DouyinThrottleState CreateNextDouyinThrottleState(DouyinThrottleState current, long now)
    {
        if (IsDouyinRequestBlocked(now, current.BlockedUntil))
        {
            return current;
        }

        int failureCount = Math.Min(current.FailureCount + 1, 4);
        return new DouyinThrottleState(now + GetDouyinBackoffMilliseconds(failureCount), failureCount);
    }

    internal static StreamResolverResult ExtractDouyinReflowData(string roomUrl, string? json, string? preferredQuality = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StreamResolverResult
            {
                RoomUrl = roomUrl,
                PlatformName = "Douyin",
            };
        }

        try
        {
            JObject root = JObject.Parse(json);
            JToken? room = root["data"]?["room"];
            if (room == null)
            {
                return new StreamResolverResult
                {
                    RoomUrl = roomUrl,
                    PlatformName = "Douyin",
                };
            }

            JObject compatible = new()
            {
                ["data"] = new JObject
                {
                    ["data"] = new JArray(room.DeepClone()),
                    ["user"] = room["owner"]?.DeepClone(),
                },
            };
            return ExtractDouyinWebEnterData(roomUrl, compatible.ToString(Newtonsoft.Json.Formatting.None), preferredQuality);
        }
        catch
        {
            return new StreamResolverResult
            {
                RoomUrl = roomUrl,
                PlatformName = "Douyin",
            };
        }
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

    private static StreamResolverResult? ResolveDouyin(string roomUrl, string? preferredQuality, bool tryAllRoutes, bool prioritizeDouyin)
    {
        DouyinRoomSession session = DouyinRoomSessions.GetOrAdd(roomUrl, static _ => new DouyinRoomSession());
        DouyinResolveRoute firstRoute = tryAllRoutes ? DouyinResolveRoute.WebEnter : session.TakeNextRoute();
        DouyinResolveRoute[] routes = GetDouyinRouteOrder(firstRoute, tryAllRoutes);
        StreamResolverResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = "Douyin",
        };

        foreach (DouyinResolveRoute route in routes)
        {
            StreamResolverResult? routeResult = ResolveDouyinRoute(roomUrl, preferredQuality, session, route);
            result = MergeResults(roomUrl, result, routeResult);
            if (IsCompleteDouyinResult(result))
            {
                break;
            }
        }

        int failureCount = IsCompleteDouyinResult(result) ? 0 : session.RegisterFailure();
        if (failureCount == 0)
        {
            session.ResetFailures();
        }
        else if (TryReserveDouyinWebViewFallback(tryAllRoutes, prioritizeDouyin, failureCount))
        {
            DouyinWebViewSnapshot snapshot = DouyinWebViewResolver.Resolve(roomUrl, GetDouyinCookie(), tryAllRoutes);
            StreamResolverResult browserResult = ExtractDouyinWebViewSnapshot(roomUrl, snapshot, preferredQuality, session);
            result = MergeResults(roomUrl, result, browserResult);
            if (!IsCompleteDouyinResult(result)
                && session.TryGetIdentity(out string roomId, out string secUid))
            {
                StreamResolverResult? reflowResult = ResolveDouyinAppReflow(
                    roomUrl,
                    roomId,
                    secUid,
                    GetDouyinCookie(),
                    preferredQuality);
                result = MergeResults(roomUrl, result, reflowResult);
            }
            if (IsCompleteDouyinResult(result))
            {
                session.ResetFailures();
                AppSessionLogger.Event("info", "resolver", "douyin_webview_resolved", "Douyin room was resolved through the browser fallback", new
                {
                    roomUrl,
                    result.IsLiveStreaming,
                    hasStream = HasRecordableStream(result),
                });
            }
        }

        if (IsCompleteDouyinResult(result))
        {
            EnrichHighestHlsVariant(result, preferredQuality, roomUrl, GetDouyinCookie(), DouyinWebUserAgent);
            LastErrors.TryRemove(roomUrl, out _);
            return result;
        }

        if (string.IsNullOrWhiteSpace(GetLastError(roomUrl)))
        {
            SetLastError(roomUrl, HasRoomData(result) ? DouyinInconclusiveError : DouyinTransientBlockError);
        }
        if (!HasRoomData(result) && IsDouyinBlockingFailure(GetLastError(roomUrl)))
        {
            RegisterDouyinBlockingResponse();
        }
        return HasRoomData(result) ? result : null;
    }

    internal static DouyinResolveRoute[] GetDouyinRouteOrder(DouyinResolveRoute firstRoute, bool tryAllRoutes)
    {
        if (tryAllRoutes || firstRoute == DouyinResolveRoute.WebEnter)
        {
            return [DouyinResolveRoute.WebEnter, DouyinResolveRoute.RoomPage, DouyinResolveRoute.AppReflow];
        }

        return firstRoute == DouyinResolveRoute.RoomPage
            ? [DouyinResolveRoute.RoomPage, DouyinResolveRoute.WebEnter, DouyinResolveRoute.AppReflow]
            : [DouyinResolveRoute.AppReflow, DouyinResolveRoute.WebEnter, DouyinResolveRoute.RoomPage];
    }

    private static StreamResolverResult? ResolveDouyinRoute(
        string roomUrl,
        string? preferredQuality,
        DouyinRoomSession session,
        DouyinResolveRoute route)
    {
        return route switch
        {
            DouyinResolveRoute.WebEnter => ResolveDouyinWebEnter(roomUrl, preferredQuality, session),
            DouyinResolveRoute.RoomPage => ResolveDouyinRoomPage(roomUrl, preferredQuality, session),
            DouyinResolveRoute.AppReflow when session.TryGetIdentity(out string roomId, out string secUid) =>
                ResolveDouyinAppReflow(roomUrl, roomId, secUid, GetDouyinCookie(), preferredQuality),
            _ => null,
        };
    }

    private static StreamResolverResult? ResolveDouyinWebEnter(string roomUrl, string? preferredQuality, DouyinRoomSession session)
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
        string? json = RequestDouyinText(
            roomUrl,
            api,
            roomUrl,
            GetDouyinCookie(),
            DouyinWebUserAgent,
            "application/json,text/plain,*/*");

        StreamResolverResult result = ExtractDouyinWebEnterData(roomUrl, json, preferredQuality);
        if (TryExtractDouyinReflowIdentity(json, out string roomId, out string secUid))
        {
            session.UpdateIdentity(roomId, secUid);
            if (NeedsDouyinAppFallback(result))
            {
                session.SetNextRoute(DouyinResolveRoute.AppReflow);
            }
        }
        return HasRoomData(result) ? result : null;
    }

    private static StreamResolverResult? ResolveDouyinRoomPage(string roomUrl, string? preferredQuality, DouyinRoomSession session)
    {
        string? html = RequestDouyinText(
            roomUrl,
            roomUrl,
            "https://live.douyin.com/",
            GetDouyinCookie(),
            DouyinWebUserAgent);
        if (TryExtractDouyinPageIdentity(html, out string roomId, out string secUid))
        {
            session.UpdateIdentity(roomId, secUid);
        }
        StreamResolverResult result = ExtractDouyinData(roomUrl, html, preferredQuality);
        return HasRoomData(result) ? result : null;
    }

    private static StreamResolverResult ExtractDouyinWebViewSnapshot(
        string roomUrl,
        DouyinWebViewSnapshot snapshot,
        string? preferredQuality,
        DouyinRoomSession session)
    {
        StreamResolverResult apiResult = ExtractDouyinWebEnterData(roomUrl, snapshot.WebEnterJson, preferredQuality);
        StreamResolverResult reflowResult = ExtractDouyinReflowData(roomUrl, snapshot.ReflowJson, preferredQuality);
        StreamResolverResult pageResult = ExtractDouyinData(roomUrl, snapshot.Html, preferredQuality);
        if (TryExtractDouyinReflowIdentity(snapshot.WebEnterJson, out string roomId, out string secUid)
            || TryExtractDouyinReflowIdentity(snapshot.ReflowJson, out roomId, out secUid)
            || TryExtractDouyinPageIdentity(snapshot.Html, out roomId, out secUid))
        {
            session.UpdateIdentity(roomId, secUid);
        }
        return MergeResults(roomUrl, apiResult, reflowResult, pageResult);
    }

    private static bool IsCompleteDouyinResult(ISpiderResult? result)
    {
        return result?.IsLiveStreaming == false
            || (result?.IsLiveStreaming == true && HasRecordableStream(result));
    }

    internal static bool ShouldUseDouyinWebViewFallback(bool tryAllRoutes, bool prioritizeDouyin, int failureCount)
    {
        return tryAllRoutes || prioritizeDouyin && failureCount >= 2 || failureCount >= 3;
    }

    internal static bool IsDouyinBlockingFailure(string? error)
    {
        return string.Equals(error, DouyinTransientBlockError, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(error)
                && (error.Contains("HTTP 403", StringComparison.Ordinal)
                    || error.Contains("HTTP 429", StringComparison.Ordinal)));
    }

    private static bool TryReserveDouyinWebViewFallback(bool tryAllRoutes, bool prioritizeDouyin, int failureCount)
    {
        if (!ShouldUseDouyinWebViewFallback(tryAllRoutes, prioritizeDouyin, failureCount))
        {
            return false;
        }
        if (tryAllRoutes)
        {
            return true;
        }

        long now = Environment.TickCount64;
        while (true)
        {
            long next = Volatile.Read(ref douyinNextWebViewAt);
            if (next > now)
            {
                return false;
            }
            if (Interlocked.CompareExchange(ref douyinNextWebViewAt, now + 60000, next) == next)
            {
                return true;
            }
        }
    }

    private static string GetDouyinCookie()
    {
        string configuredCookie = PlatformCookieStore.GetCookie("Douyin", SecretProtector.GetChinaCookie());
        if (string.IsNullOrWhiteSpace(configuredCookie))
        {
            return DouyinDefaultCookie;
        }
        if (configuredCookie.Contains("ttwid=", StringComparison.OrdinalIgnoreCase))
        {
            return configuredCookie;
        }
        return configuredCookie.Trim().TrimEnd(';') + "; " + DouyinDefaultCookie;
    }

    internal static bool NeedsDouyinAppFallback(ISpiderResult? result)
    {
        return result?.IsLiveStreaming == true && !HasRecordableStream(result);
    }

    internal static bool NeedsDouyinMetadataSupplement(ISpiderResult? result)
    {
        return NeedsSupplementalData(result)
            || (result?.IsLiveStreaming == false
                && string.IsNullOrWhiteSpace(result.Nickname)
                && string.IsNullOrWhiteSpace(result.Uid)
                && string.IsNullOrWhiteSpace(result.AvatarThumbUrl));
    }

    internal static bool ShouldUseDouyinCookieFallback(ISpiderResult? primary, ISpiderResult? fallback)
    {
        return !NeedsDouyinMetadataSupplement(fallback)
            || (!HasRoomData(primary) && HasRoomData(fallback));
    }

    internal static bool TryExtractDouyinReflowIdentity(string? json, out string roomId, out string secUid)
    {
        roomId = string.Empty;
        secUid = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JToken? room = root["data"]?["data"]?.FirstOrDefault();
            JToken? user = root["data"]?["user"] ?? room?["owner"];
            roomId = FirstNonEmpty(
                CleanOptionalText(room?["id_str"]?.ToString()),
                CleanOptionalText(room?["id"]?.ToString())) ?? string.Empty;
            secUid = FirstNonEmpty(
                CleanOptionalText(user?["sec_uid"]?.ToString()),
                CleanOptionalText(room?["owner"]?["sec_uid"]?.ToString())) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(roomId) && !string.IsNullOrWhiteSpace(secUid);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryExtractDouyinPageIdentity(string? html, out string roomId, out string secUid)
    {
        roomId = string.Empty;
        secUid = string.Empty;
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        string normalized = NormalizeEscapedText(html);
        Match roomIdMatch = DouyinInternalRoomIdRegex.Match(normalized);
        Match secUidMatch = DouyinSecUidRegex.Match(normalized);
        if (roomIdMatch.Success)
        {
            roomId = HttpUtility.HtmlDecode(FirstNonEmpty(
                roomIdMatch.Groups[1].Value,
                roomIdMatch.Groups[2].Value) ?? string.Empty).Trim();
        }
        if (secUidMatch.Success)
        {
            secUid = HttpUtility.HtmlDecode(secUidMatch.Groups[1].Value).Trim();
        }
        return !string.IsNullOrWhiteSpace(roomId) && !string.IsNullOrWhiteSpace(secUid);
    }

    private static bool HasRecordableStream(ISpiderResult result)
    {
        return !string.IsNullOrWhiteSpace(result.RecordUrl)
            || !string.IsNullOrWhiteSpace(result.HlsUrl)
            || !string.IsNullOrWhiteSpace(result.FlvUrl);
    }

    private static StreamResolverResult? ResolveDouyinAppReflow(
        string roomUrl,
        string roomId,
        string secUid,
        string cookie,
        string? preferredQuality)
    {
        string query = string.Join("&",
        [
            "verifyFp=verify_hwj52020_7szNlAB7_pxNY_48Vh_ALKF_GA1Uf3yteoOY",
            "type_id=0",
            "live_id=1",
            $"room_id={Uri.EscapeDataString(roomId)}",
            $"sec_user_id={Uri.EscapeDataString(secUid)}",
            "version_code=99.99.99",
            "app_id=1128",
        ]);
        string aBogus = DouyinWebSignature.CreateABogus(query, DouyinWebUserAgent);
        string api = $"https://webcast.amemv.com/webcast/room/reflow/info/?{query}&a_bogus={Uri.EscapeDataString(aBogus)}";
        string? json = RequestDouyinText(
            roomUrl,
            api,
            roomUrl,
            cookie,
            DouyinWebUserAgent,
            "application/json,text/plain,*/*");
        StreamResolverResult result = ExtractDouyinReflowData(roomUrl, json, preferredQuality);
        return HasRoomData(result) ? result : null;
    }

    private static string? RequestDouyinText(
        string roomUrl,
        string url,
        string referer,
        string? cookie,
        string userAgent,
        string accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
    {
        WaitForDouyinHttpRequestSlot();
        using HttpRequestMessage request = CreateRequest(url, referer, cookie, userAgent, accept);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(RequestTimeoutSeconds));
        try
        {
            using HttpResponseMessage response = ProxyHttpClientPool.GetCurrent().Send(
                request,
                HttpCompletionOption.ResponseContentRead,
                timeout.Token);
            string text = response.Content.ReadAsStringAsync(timeout.Token).GetAwaiter().GetResult();
            if ((int)response.StatusCode is 403 or 429)
            {
                SetLastError(roomUrl, $"Douyin request blocked (HTTP {(int)response.StatusCode}).");
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                SetLastError(roomUrl, $"Douyin request failed (HTTP {(int)response.StatusCode}).");
                return null;
            }
            if (IsDouyinBlockedContent(text))
            {
                SetLastError(roomUrl, DouyinTransientBlockError);
                return null;
            }
            return text;
        }
        catch (OperationCanceledException)
        {
            SetLastError(roomUrl, "Douyin request timed out.");
            return null;
        }
        catch (HttpRequestException e)
        {
            SetLastError(roomUrl, $"Douyin request failed: {e.Message}");
            return null;
        }
    }

    private static HttpRequestMessage CreateRequest(
        string url,
        string referer,
        string? cookie,
        string userAgent,
        string accept)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", string.IsNullOrWhiteSpace(Configurations.UserAgent.Get()) ? userAgent : Configurations.UserAgent.Get());
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.7");
        request.Headers.TryAddWithoutValidation("Referer", referer);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
        return request;
    }

    private static void WaitForDouyinHttpRequestSlot()
    {
        long now = Environment.TickCount64;
        long requestAt;
        lock (DouyinThrottleSync)
        {
            requestAt = Math.Max(now, douyinNextRequestAt);
            douyinNextRequestAt = requestAt + DouyinRequestSpacingMilliseconds;
        }
        long delay = requestAt - now;
        if (delay > 0)
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(delay));
        }
    }

    private static void RegisterDouyinBlockingResponse()
    {
        const int threshold = 5;
        const int windowMilliseconds = 10000;
        const int pauseMilliseconds = 30000;
        long now = Environment.TickCount64;
        lock (DouyinThrottleSync)
        {
            while (DouyinBlockingResponses.TryPeek(out long timestamp) && now - timestamp > windowMilliseconds)
            {
                _ = DouyinBlockingResponses.Dequeue();
            }
            DouyinBlockingResponses.Enqueue(now);
            if (DouyinBlockingResponses.Count >= threshold)
            {
                bool wasOpen = IsDouyinRequestBlocked(now, Volatile.Read(ref douyinBackgroundBlockedUntil));
                Volatile.Write(ref douyinBackgroundBlockedUntil, now + pauseMilliseconds);
                DouyinBlockingResponses.Clear();
                if (!wasOpen)
                {
                    AppSessionLogger.Event(
                        "warn",
                        "resolver",
                        "douyin_background_paused",
                        "Douyin background requests were paused after repeated blocking responses",
                        new { pauseMilliseconds });
                }
            }
        }
    }

    internal static bool IsDouyinBlockedContent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }
        return ContainsDouyinChallenge(text);
    }

    internal static bool ContainsDouyinChallenge(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        return text.Contains("captcha_verify_container", StringComparison.OrdinalIgnoreCase)
            || text.Contains("secsdk-captcha-drag-wrapper", StringComparison.OrdinalIgnoreCase)
            || text.Contains("verifycenter/captcha", StringComparison.OrdinalIgnoreCase)
            || (text.Length < 4096 && text.Contains("captcha", StringComparison.OrdinalIgnoreCase));
    }

    private static StreamResolverResult? ResolveTiktok(string roomUrl, string? preferredQuality)
    {
        string? html = RequestText(
            roomUrl,
            "https://www.tiktok.com/",
            PlatformCookieStore.GetCookie("TikTok", SecretProtector.GetOverseaCookie()),
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");

        StreamResolverResult result = ExtractTiktokData(roomUrl, html, preferredQuality);

        if (HasUsableData(result))
        {
            EnrichHighestHlsVariant(result, preferredQuality, roomUrl, PlatformCookieStore.GetCookie("TikTok", SecretProtector.GetOverseaCookie()), "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");
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
        using HttpRequestMessage request = CreateRequest(url, referer, cookie, userAgent, accept);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(RequestTimeoutSeconds));
        using HttpResponseMessage response = ProxyHttpClientPool.GetCurrent().Send(
            request,
            HttpCompletionOption.ResponseContentRead,
            timeout.Token);
        return response.IsSuccessStatusCode
            ? response.Content.ReadAsStringAsync(timeout.Token).GetAwaiter().GetResult()
            : null;
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

    internal static void EnrichHighestHlsVariant(StreamResolverResult result, string? preferredQuality, string referer, string? cookie, string userAgent)
    {
        if (StreamQualityCatalog.NormalizePreference(preferredQuality) != StreamQualityCatalog.Original
            || string.IsNullOrWhiteSpace(result.HlsUrl))
        {
            return;
        }

        if (!TryGetCachedHlsVariant(result.HlsUrl, out HlsVariant variant))
        {
            variant = ProbeHighestHlsVariant(result.HlsUrl, referer, cookie, userAgent);
            TimeSpan cacheDuration = string.IsNullOrWhiteSpace(variant.Url)
                ? HlsVariantNegativeCacheDuration
                : HlsVariantPositiveCacheDuration;
            CacheHlsVariant(result.HlsUrl, variant, cacheDuration);
        }
        if (string.IsNullOrWhiteSpace(variant.Url))
        {
            return;
        }

        string originalHlsUrl = result.HlsUrl;
        result.HlsUrl = variant.Url;
        if (string.Equals(result.RecordUrl, originalHlsUrl, StringComparison.Ordinal))
        {
            result.RecordUrl = variant.Url;
        }
        if (variant.Height > 0)
        {
            result.Resolution = variant.Width > 0 ? $"{variant.Width}x{variant.Height}" : $"{variant.Height}p";
        }
        if (variant.Bandwidth > 0)
        {
            result.Bitrate = StreamQualityCatalog.FormatBitrate(variant.Bandwidth);
        }
    }

    private static bool TryGetCachedHlsVariant(string playlistUrl, out HlsVariant variant)
    {
        variant = default;
        if (!HlsVariantCache.TryGetValue(playlistUrl, out HlsVariantCacheEntry entry))
        {
            return false;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            HlsVariantCache.TryRemove(playlistUrl, out _);
            return false;
        }

        variant = entry.Variant;
        return true;
    }

    private static void CacheHlsVariant(string playlistUrl, HlsVariant variant, TimeSpan duration)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HlsVariantCache[playlistUrl] = new HlsVariantCacheEntry(variant, now.Add(duration));

        foreach (KeyValuePair<string, HlsVariantCacheEntry> expired in HlsVariantCache.Where(pair => pair.Value.ExpiresAt <= now))
        {
            HlsVariantCache.TryRemove(expired.Key, out _);
        }

        int excess = HlsVariantCache.Count - HlsVariantCacheLimit;
        if (excess <= 0)
        {
            return;
        }

        foreach (KeyValuePair<string, HlsVariantCacheEntry> oldest in HlsVariantCache.OrderBy(pair => pair.Value.ExpiresAt).Take(excess))
        {
            HlsVariantCache.TryRemove(oldest.Key, out _);
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
        string host = uri.Host.ToLowerInvariant();
        string[] segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length == 0)
        {
            return null;
        }

        if (host.Equals("live.douyin.com", StringComparison.Ordinal))
        {
            return IsValidDouyinRoomToken(segments[0]) ? segments[0] : null;
        }

        if (!host.Equals("www.douyin.com", StringComparison.Ordinal)
            && !host.Equals("douyin.com", StringComparison.Ordinal))
        {
            return null;
        }

        int liveIndex = Array.FindIndex(segments, segment => segment.Equals("live", StringComparison.OrdinalIgnoreCase));
        if (liveIndex >= 0 && liveIndex < segments.Length - 1)
        {
            string roomId = segments[liveIndex + 1];
            return IsValidDouyinRoomToken(roomId) ? roomId : null;
        }

        return null;
    }

    private static bool HostMatchesDomain(string host, string domain)
    {
        return host.Equals(domain, StringComparison.Ordinal)
            || host.EndsWith('.' + domain, StringComparison.Ordinal);
    }

    private static bool IsValidDouyinRoomToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DouyinRoomTokenRegex.IsMatch(value);
    }

    private static string? TryFetchDouyinProfileAvatar(string roomUrl, string? liveHtml)
    {
        foreach (string profileUrl in GetDouyinProfileCandidates(roomUrl, liveHtml))
        {
            string? profileHtml = RequestText(
                profileUrl,
                "https://www.douyin.com/",
                PlatformCookieStore.GetCookie("Douyin", SecretProtector.GetChinaCookie()),
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

    private static DouyinOriginStream SelectDouyinOriginStream(JToken? streamUrl, string? preferredQuality)
    {
        if (StreamQualityCatalog.NormalizePreference(preferredQuality) != StreamQualityCatalog.Original)
        {
            return default;
        }

        if (streamUrl is not JObject streamUrlObject)
        {
            return default;
        }

        JToken? liveCoreSdkData = GetJsonProperty(streamUrlObject, "live_core_sdk_data", "liveCoreSdkData");
        JToken? primaryPullData = GetJsonProperty(liveCoreSdkData, "pull_data", "pullData");
        JObject? primaryStreamData = ParseJsonObject(GetJsonProperty(primaryPullData, "stream_data", "streamData"));
        List<JObject> streamDataCandidates = [];
        JToken? pullDatas = GetJsonProperty(streamUrlObject, "pull_datas", "pullDatas");
        bool hasDeclaredPullData = false;
        if (pullDatas is JObject pullDataMap)
        {
            hasDeclaredPullData = pullDataMap.Properties().Any();
            streamDataCandidates.AddRange(pullDataMap.Properties()
                .Select(property => ParseDouyinStreamData(property.Value))
                .Where(candidate => candidate != null)
                .Select(candidate => candidate!));
        }
        if (!hasDeclaredPullData && primaryStreamData != null)
        {
            streamDataCandidates.Add(primaryStreamData);
        }

        JObject? selectedStreamData = streamDataCandidates.FirstOrDefault(HasDouyinOriginStream);
        if (selectedStreamData?["data"]?["origin"]?["main"] is not JObject origin)
        {
            return default;
        }

        JObject? primaryOrigin = primaryStreamData?["data"]?["origin"]?["main"] as JObject;
        JObject? primarySdkParams = ParseJsonObject(primaryOrigin?["sdk_params"] ?? primaryOrigin?["sdkParams"]);
        JObject? selectedSdkParams = ParseJsonObject(origin["sdk_params"] ?? origin["sdkParams"]);
        string? codec = FirstNonEmpty(
            CleanOptionalText(primarySdkParams?["VCodec"]?.ToString()),
            CleanOptionalText(selectedSdkParams?["VCodec"]?.ToString()));
        string? resolution = CleanOptionalText(selectedSdkParams?["resolution"]?.ToString());
        double bitrate = ParsePositiveDouble(selectedSdkParams?["vbitrate"] ?? selectedSdkParams?["v_bit_rate"]);
        if (bitrate <= 0)
        {
            bitrate = ParsePositiveDouble(origin["templateRealTimeInfo"]?["bitrateKbps"]) * 1000d;
        }

        string? flvUrl = AppendQueryParameter(CleanOptionalUrl(origin["flv"]?.ToString()), "codec", codec);
        string? hlsUrl = AppendQueryParameter(CleanOptionalUrl(origin["hls"]?.ToString()), "codec", codec);
        if (string.IsNullOrWhiteSpace(flvUrl) && string.IsNullOrWhiteSpace(hlsUrl))
        {
            return default;
        }

        return new DouyinOriginStream(flvUrl, hlsUrl, resolution, bitrate);
    }

    private static JObject? ParseDouyinStreamData(JToken? pullData)
    {
        JObject? direct = ParseJsonObject(GetJsonProperty(pullData, "stream_data", "streamData"));
        if (direct != null)
        {
            return direct;
        }

        JToken? nestedPullData = GetJsonProperty(pullData, "pull_data", "pullData");
        JObject? nested = ParseJsonObject(GetJsonProperty(nestedPullData, "stream_data", "streamData"));
        if (nested != null)
        {
            return nested;
        }

        JToken? liveCoreSdkData = GetJsonProperty(pullData, "live_core_sdk_data", "liveCoreSdkData");
        nestedPullData = GetJsonProperty(liveCoreSdkData, "pull_data", "pullData");
        return ParseJsonObject(GetJsonProperty(nestedPullData, "stream_data", "streamData"))
            ?? ParseJsonObject(pullData);
    }

    private static bool HasDouyinOriginStream(JObject streamData)
    {
        JToken? origin = streamData["data"]?["origin"]?["main"];
        return !string.IsNullOrWhiteSpace(CleanOptionalUrl(origin?["flv"]?.ToString()))
            || !string.IsNullOrWhiteSpace(CleanOptionalUrl(origin?["hls"]?.ToString()));
    }

    private static string? SelectDouyinRecordUrl(string? flvUrl, string? hlsUrl)
    {
        if (!string.IsNullOrWhiteSpace(flvUrl))
        {
            string? codec = Uri.TryCreate(flvUrl, UriKind.Absolute, out Uri? flvUri)
                ? HttpUtility.ParseQueryString(flvUri.Query)["codec"]
                : null;
            if (!string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase))
            {
                return flvUrl;
            }
        }

        return FirstNonEmpty(hlsUrl, flvUrl);
    }

    private static JToken? GetJsonProperty(JToken? token, string primaryName, string alternateName)
    {
        return token is JObject obj ? obj[primaryName] ?? obj[alternateName] : null;
    }

    private static JObject? ParseJsonObject(JToken? token)
    {
        if (token is JObject obj)
        {
            return obj;
        }

        string? value = token?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JObject.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private static double ParsePositiveDouble(JToken? token)
    {
        string? value = token?.ToString();
        return double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double parsed)
            && parsed > 0
            ? parsed
            : 0;
    }

    private static string? AppendQueryParameter(string? url, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(value))
        {
            return url;
        }

        string parameterPrefix = $"{name}=";
        if (url.Split('?', '&').Any(part => part.StartsWith(parameterPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            return url;
        }

        char separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}{name}={Uri.EscapeDataString(value)}";
    }

    private static StreamCandidate SelectHighestObservableQuality(IReadOnlyDictionary<string, string> urls, string? preferredQuality)
    {
        if (StreamQualityCatalog.NormalizePreference(preferredQuality) != StreamQualityCatalog.Original)
        {
            return default;
        }

        string[] qualityOrder = StreamQualityCatalog.GetStreamKeyOrder(StreamQualityCatalog.Original).ToArray();
        var candidates = urls
            .Select(pair =>
            {
                (int height, double bitrate) = StreamMetadataParser.GetQualityMetrics(pair.Value);
                int qualityRank = Array.FindIndex(qualityOrder, key => key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    pair.Key,
                    Url = pair.Value,
                    Height = height,
                    Bitrate = bitrate,
                    QualityRank = qualityRank < 0 ? int.MaxValue : qualityRank,
                };
            })
            .ToArray();
        if (candidates.Length == 0 || candidates.Any(candidate => candidate.Height <= 0 && candidate.Bitrate <= 0))
        {
            return default;
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.Height)
            .ThenByDescending(candidate => candidate.Bitrate)
            .ThenBy(candidate => candidate.QualityRank)
            .FirstOrDefault();

        return selected == null
            ? default
            : new StreamCandidate(selected.Url, string.IsNullOrWhiteSpace(selected.Key) ? null : selected.Key);
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

    [GeneratedRegex("\"(?:room_id|roomId)\"\\s*:\\s*(?:\"([^\"]+)\"|([0-9]+))")]
    private static partial Regex DouyinInternalRoomIdRegex { get; }

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

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{1,63}$")]
    private static partial Regex DouyinRoomTokenRegex { get; }

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

    private readonly record struct DouyinOriginStream(
        string? FlvUrl,
        string? HlsUrl,
        string? Resolution,
        double Bitrate)
    {
        public bool HasStream => !string.IsNullOrWhiteSpace(FlvUrl) || !string.IsNullOrWhiteSpace(HlsUrl);
    }
}

internal readonly record struct HlsVariant(string? Url, int Width, int Height, double Bandwidth);

internal readonly record struct HlsVariantCacheEntry(HlsVariant Variant, DateTimeOffset ExpiresAt);

internal readonly record struct DouyinThrottleState(long BlockedUntil, int FailureCount);

internal enum DouyinResolveRoute
{
    WebEnter,
    RoomPage,
    AppReflow,
}

internal sealed class DouyinRoomSession
{
    private readonly object identitySync = new();
    private int nextRoute;
    private int failureCount;
    private string roomId = string.Empty;
    private string secUid = string.Empty;

    public DouyinResolveRoute TakeNextRoute()
    {
        int value = Interlocked.Increment(ref nextRoute) - 1;
        return (DouyinResolveRoute)((uint)value % 3);
    }

    public void SetNextRoute(DouyinResolveRoute route)
    {
        Interlocked.Exchange(ref nextRoute, (int)route);
    }

    public int RegisterFailure()
    {
        return Interlocked.Increment(ref failureCount);
    }

    public void ResetFailures()
    {
        Interlocked.Exchange(ref failureCount, 0);
    }

    public void UpdateIdentity(string valueRoomId, string valueSecUid)
    {
        if (string.IsNullOrWhiteSpace(valueRoomId) || string.IsNullOrWhiteSpace(valueSecUid))
        {
            return;
        }
        lock (identitySync)
        {
            roomId = valueRoomId;
            secUid = valueSecUid;
        }
    }

    public bool TryGetIdentity(out string valueRoomId, out string valueSecUid)
    {
        lock (identitySync)
        {
            valueRoomId = roomId;
            valueSecUid = secUid;
            return !string.IsNullOrWhiteSpace(valueRoomId) && !string.IsNullOrWhiteSpace(valueSecUid);
        }
    }
}

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

    public string? RecordUrl { get; set; }

    public string? Title { get; set; }

    public string? Quality { get; set; }

    public string? Uid { get; set; }

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
