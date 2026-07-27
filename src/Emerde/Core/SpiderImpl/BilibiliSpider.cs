using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;

namespace Emerde.Core;

public sealed class BilibiliSpider : ISpider, IQualitySelectableSpider
{
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:127.0) Gecko/20100101 Firefox/127.0";

    public static Lazy<BilibiliSpider> Instance { get; } = new(() => new BilibiliSpider());

    public string PlatformName => "Bilibili";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "live.bilibili.com")
        {
            return null;
        }

        string roomId = uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomId) || !roomId.All(char.IsDigit))
        {
            return null;
        }

        return $"https://live.bilibili.com/{roomId}";
    }

    public ISpiderResult GetResult(string url)
    {
        return GetResult(url, StreamQualityCatalog.Original);
    }

    public ISpiderResult GetResult(string url, string? preferredQuality)
    {
        string? roomUrl = ParseUrl(url);
        BilibiliSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        result.Headers = BuildPlaybackHeaders(
            roomUrl,
            PlatformCookieStore.GetCookie("Bilibili", SecretProtector.GetChinaCookie()));

        string roomId = roomUrl.Split('/').Last();
        string? roomInfoJson = RequestUrl($"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId}", roomUrl);
        ExtractRoomInfo(roomInfoJson, result);

        if (string.IsNullOrWhiteSpace(result.Nickname) && !string.IsNullOrWhiteSpace(result.Uid))
        {
            string? masterJson = RequestUrl($"https://api.live.bilibili.com/live_user/v1/Master/info?uid={result.Uid}", roomUrl);
            ExtractMasterInfo(masterJson, result);
        }

        if (result.IsLiveStreaming == true && !string.IsNullOrWhiteSpace(result.RoomId))
        {
            int qualityNumber = StreamQualityCatalog.GetBilibiliQualityNumber(preferredQuality);
            string? playInfoJson = RequestPlayInfo(result.RoomId, qualityNumber, roomUrl);
            BilibiliPlayInfo playInfo = ExtractPlayInfo(playInfoJson, result);
            if (StreamQualityCatalog.NormalizePreference(preferredQuality) == StreamQualityCatalog.Original
                && playInfo.HighestAvailableQuality > qualityNumber)
            {
                string? highestPlayInfoJson = RequestPlayInfo(result.RoomId, playInfo.HighestAvailableQuality, roomUrl);
                ExtractPlayInfo(highestPlayInfoJson, result);
            }

            if (string.IsNullOrWhiteSpace(result.FlvUrl) && string.IsNullOrWhiteSpace(result.HlsUrl))
            {
                string? playJson = RequestUrl($"https://api.live.bilibili.com/room/v1/Room/playUrl?cid={result.RoomId}&qn={qualityNumber}&platform=web", roomUrl);
                ExtractPlayUrl(playJson, result, qualityNumber.ToString());
            }
        }

        return result;
    }

    internal static void ExtractRoomInfo(string? json, BilibiliSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? data = root["data"] as JObject;

            if (data == null)
            {
                return;
            }

            result.RoomId = data["room_id"]?.ToString();
            result.Uid = data["uid"]?.ToString();
            result.IsLiveStreaming = data["live_status"]?.Value<int>() == 1;

            if (!string.IsNullOrWhiteSpace(result.RoomId))
            {
                result.RoomUrl = $"https://live.bilibili.com/{result.RoomId}";
            }
        }
        catch
        {
        }
    }

    internal static void ExtractMasterInfo(string? json, BilibiliSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? data = root["data"] as JObject;
            JObject? info = data?["info"] as JObject;

            if (info == null)
            {
                return;
            }

            result.Nickname = info["uname"]?.ToString();
            result.AvatarThumbUrl = info["face"]?.ToString();
        }
        catch
        {
        }
    }

    internal static void ExtractPlayUrl(string? json, BilibiliSpiderResult result, string? quality = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JToken? data = root["data"];
            JArray? durl = data?["durl"] as JArray;

            if (durl == null || durl.Count == 0)
            {
                return;
            }

            string? url = durl
                .Select(item => item["url"]?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .LastOrDefault();

            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                result.HlsUrl = url;
            }
            else
            {
                result.FlvUrl = url;
            }
            result.Quality = data?["current_qn"]?.ToString() ?? quality;
        }
        catch
        {
        }
    }

    internal static BilibiliPlayInfo ExtractPlayInfo(string? json, BilibiliSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JToken? playUrl = root["data"]?["playurl_info"]?["playurl"];
            JArray? streams = playUrl?["stream"] as JArray;
            if (streams == null)
            {
                return default;
            }

            List<BilibiliStreamCandidate> candidates = [];
            HashSet<int> availableQualities = [];
            foreach (JToken description in playUrl?["g_qn_desc"] as JArray ?? [])
            {
                if (description["qn"]?.Value<int>() is int quality && quality > 0)
                {
                    availableQualities.Add(quality);
                }
            }

            foreach (JToken stream in streams)
            {
                string protocolName = stream["protocol_name"]?.ToString() ?? string.Empty;
                foreach (JToken format in stream["format"] as JArray ?? [])
                {
                    string formatName = format["format_name"]?.ToString() ?? string.Empty;
                    foreach (JToken codec in format["codec"] as JArray ?? [])
                    {
                        int currentQuality = codec["current_qn"]?.Value<int>() ?? 0;
                        if (currentQuality > 0)
                        {
                            availableQualities.Add(currentQuality);
                        }
                        foreach (JToken acceptedQuality in codec["accept_qn"] as JArray ?? [])
                        {
                            if (acceptedQuality.Value<int>() is int quality && quality > 0)
                            {
                                availableQualities.Add(quality);
                            }
                        }

                        string baseUrl = codec["base_url"]?.ToString() ?? string.Empty;
                        string codecName = codec["codec_name"]?.ToString() ?? string.Empty;
                        foreach (JToken urlInfo in codec["url_info"] as JArray ?? [])
                        {
                            string? url = BuildPlayUrl(urlInfo["host"]?.ToString(), baseUrl, urlInfo["extra"]?.ToString());
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                candidates.Add(new BilibiliStreamCandidate(url, protocolName, formatName, codecName, currentQuality));
                            }
                        }
                    }
                }
            }

            BilibiliStreamCandidate? flv = SelectCandidate(candidates, true);
            BilibiliStreamCandidate? hls = SelectCandidate(candidates, false);
            if (flv != null)
            {
                result.FlvUrl = flv.Url;
            }
            if (hls != null)
            {
                result.HlsUrl = hls.Url;
            }

            int current = Math.Max(flv?.Quality ?? 0, hls?.Quality ?? 0);
            if (current > 0)
            {
                result.Quality = current.ToString();
            }

            return new BilibiliPlayInfo(current, availableQualities.DefaultIfEmpty(current).Max());
        }
        catch
        {
            return default;
        }
    }

    private static string? RequestPlayInfo(string roomId, int qualityNumber, string roomUrl)
    {
        string url = "https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo"
            + $"?room_id={roomId}&protocol=0,1&format=0,1,2&codec=0,1,2&qn={qualityNumber}&platform=web&ptype=8&dolby=5&panorama=1";
        return RequestUrl(url, roomUrl);
    }

    private static BilibiliStreamCandidate? SelectCandidate(IEnumerable<BilibiliStreamCandidate> candidates, bool flv)
    {
        return candidates
            .Where(candidate => flv ? candidate.IsFlv : candidate.IsHls)
            .OrderByDescending(candidate => candidate.Quality)
            .ThenBy(candidate => GetCodecPriority(candidate.CodecName))
            .FirstOrDefault();
    }

    private static int GetCodecPriority(string codecName)
    {
        return codecName.ToLowerInvariant() switch
        {
            "avc" or "h264" => 0,
            "hevc" or "h265" => 1,
            "av1" => 2,
            _ => 3,
        };
    }

    private static string? BuildPlayUrl(string? host, string? baseUrl, string? extra)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        string url;
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            url = baseUrl;
        }
        else if (!string.IsNullOrWhiteSpace(host))
        {
            url = host.TrimEnd('/') + "/" + baseUrl.TrimStart('/');
        }
        else
        {
            return null;
        }

        return url + (extra ?? string.Empty);
    }

    private static string? RequestUrl(string url, string referer)
    {
        RestClientOptions options = new()
        {
            BaseUrl = new Uri(url),
        };

        if (Configurations.IsUseProxy.Get())
        {
            string proxyUrl = Configurations.ProxyUrl.Get();

            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                options.Proxy = ProxyAddress.Create(proxyUrl);
            }
        }

        using RestClient client = new(options);
        RestRequest request = new()
        {
            Method = Method.Get,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = PlatformCookieStore.GetCookie("Bilibili", SecretProtector.GetChinaCookie());

        request.AddHeader("User-Agent", BrowserUserAgent);
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2");
        request.AddHeader("Origin", "https://live.bilibili.com");
        request.AddHeader("Referer", referer);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    internal static string BuildPlaybackHeaders(string roomUrl, string? cookie)
    {
        string headers = $"User-Agent: {BrowserUserAgent}\r\nReferer: {roomUrl}\r\nOrigin: https://live.bilibili.com";
        return string.IsNullOrWhiteSpace(cookie) ? headers : $"{headers}\r\nCookie: {cookie}";
    }
}

internal readonly record struct BilibiliPlayInfo(int CurrentQuality, int HighestAvailableQuality);

internal sealed record BilibiliStreamCandidate(
    string Url,
    string ProtocolName,
    string FormatName,
    string CodecName,
    int Quality)
{
    public bool IsFlv => FormatName.Contains("flv", StringComparison.OrdinalIgnoreCase)
        || Url.Contains(".flv", StringComparison.OrdinalIgnoreCase);

    public bool IsHls => !IsFlv
        && (ProtocolName.Contains("hls", StringComparison.OrdinalIgnoreCase)
            || FormatName.Contains("ts", StringComparison.OrdinalIgnoreCase)
            || FormatName.Contains("fmp4", StringComparison.OrdinalIgnoreCase)
            || Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase));
}

public sealed class BilibiliSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? RoomId { get; set; }

    public string? Uid { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }

    public string? Quality { get; set; }

    public string? Headers { get; set; }
}
