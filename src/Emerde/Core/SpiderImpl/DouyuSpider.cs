using Jint;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class DouyuSpider : ISpider
{
    private const string DeviceId = "10000000000000000000000000003306";

    public static Lazy<DouyuSpider> Instance { get; } = new(() => new DouyuSpider());

    public string PlatformName => "Douyu";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.douyu.com" && uri.Host != "douyu.com" && uri.Host != "m.douyu.com")
        {
            return null;
        }

        string? rid = GetQueryValue(uri.Query, "rid");

        if (!string.IsNullOrWhiteSpace(rid))
        {
            return $"https://www.douyu.com/{Uri.EscapeDataString(rid)}";
        }

        string roomPath = string.Join(
            '/',
            uri.Segments.Select(segment => segment.Trim('/')).Where(segment => !string.IsNullOrWhiteSpace(segment)));

        if (string.IsNullOrWhiteSpace(roomPath))
        {
            return null;
        }

        return $"https://www.douyu.com/{roomPath}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        DouyuSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string roomPath = uri.AbsolutePath.Trim('/');
        string? roomId = roomPath;

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return result;
        }

        if (!roomId.All(char.IsDigit))
        {
            string? html = SpiderRequest.Get($"https://m.douyu.com/{roomPath}", MobileHeaders(), PlatformCookieStore.GetCookie("Douyu", Configurations.CookieChina.Get()));
            roomId = ExtractMobileRoomId(html);
        }

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return result;
        }

        result.RoomId = roomId;
        result.RoomUrl = $"https://www.douyu.com/{roomId}";

        string cookie = PlatformCookieStore.GetCookie("Douyu", Configurations.CookieChina.Get());
        string? roomInfoJson = SpiderRequest.Get($"https://www.douyu.com/betard/{roomId}", DesktopHeaders(), cookie);
        ExtractBetardRoomInfo(roomInfoJson, result);

        if (result.IsLiveStreaming == true)
        {
            string? html = SpiderRequest.Get($"https://m.douyu.com/{roomId}", MobileHeaders(), cookie);
            DouyuSignData? signData = CreateSignData(html, roomId);

            if (signData != null)
            {
                string? streamJson = RequestStreamData(roomId, signData, cookie);
                ExtractStreamData(streamJson, result);
            }

            if (string.IsNullOrWhiteSpace(result.FlvUrl) && string.IsNullOrWhiteSpace(result.HlsUrl))
            {
                result.IsLiveStreaming = false;
            }
        }

        return result;
    }

    internal static string? ExtractMobileRoomId(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        Match match = MobileContextRegex.Match(html);

        if (!match.Success)
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));

            return root["pageProps"]?["room"]?["roomInfo"]?["roomInfo"]?["rid"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static string? ExtractCrpText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        Match match = MobileContextRegex.Match(html);

        if (!match.Success)
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));

            return root["crptext"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static void ExtractBetardRoomInfo(string? json, DouyuSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? room = root["room"] as JObject;

            if (room == null)
            {
                return;
            }

            result.Nickname = room["nickname"]?.ToString();
            result.AvatarThumbUrl = room["avatar_mid"]?.ToString()
                ?? room["avatar"]?["middle"]?.ToString()
                ?? room["avatar"]?["big"]?.ToString()
                ?? room["room_pic"]?.ToString();
            result.Title = room["room_name"]?.ToString()?.Replace("&nbsp;", string.Empty, StringComparison.Ordinal);
            result.RoomId = room["room_id"]?.ToString() ?? result.RoomId;
            result.IsLiveStreaming = room["videoLoop"]?.Value<int>() == 0 && room["show_status"]?.Value<int>() == 1;
        }
        catch
        {
        }
    }

    internal static void ExtractStreamData(string? json, DouyuSpiderResult result)
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

            string? rtmpUrl = data["rtmp_url"]?.ToString();
            string? rtmpLive = data["rtmp_live"]?.ToString();
            string? streamUrl = data["url"]?.ToString();

            if (!string.IsNullOrWhiteSpace(streamUrl))
            {
                if (streamUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                {
                    result.HlsUrl = streamUrl;
                }
                else
                {
                    result.FlvUrl = streamUrl;
                }

                result.IsLiveStreaming = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(rtmpUrl) || string.IsNullOrWhiteSpace(rtmpLive))
            {
                return;
            }

            result.FlvUrl = $"{rtmpUrl.TrimEnd('/')}/{rtmpLive}";
            result.IsLiveStreaming = true;
        }
        catch
        {
        }
    }

    internal static DouyuSignData? CreateSignData(string? html, string roomId)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return CreateSignData(html, roomId, DeviceId, timestamp);
    }

    internal static DouyuSignData? CreateSignData(string? html, string roomId, string deviceId, long timestamp)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        try
        {
            string script = ExtractCrpText(html) ?? html;
            Match tokenMatch = TokenScriptRegex.Match(script);

            if (!tokenMatch.Success)
            {
                return null;
            }

            string tokenSource = tokenMatch.Groups[1].Value;
            string tokenScript = UbEvalRegex.Replace(tokenSource, "$1strc;}");

            if (tokenScript == tokenSource)
            {
                tokenScript = EvalRegex.Replace(tokenSource, "strc;}", 1);
            }
            Engine tokenEngine = CreateEngine();
            tokenEngine.Execute(tokenScript);
            string signScript = tokenEngine.Invoke("ub98484234").AsString();
            Match versionMatch = VersionRegex.Match(signScript);

            if (!versionMatch.Success)
            {
                return null;
            }

            string v = versionMatch.Groups[1].Value;
            string tt = timestamp.ToString();
            string rb = Md5(roomId + deviceId + tt + v);
            string signFunction = ReturnRegex.Replace(signScript, "return rt;}");
            signFunction = SignFunctionRegex.Replace(signFunction, "function sign(");
            signFunction = CryptoJsRegex.Replace(signFunction, $"\"{rb}\"");

            Engine signEngine = CreateEngine();
            signEngine.Execute(signFunction);
            string parameters = signEngine.Invoke("sign", roomId, deviceId, tt).AsString();

            return ParseSignParameters(parameters);
        }
        catch
        {
            return null;
        }
    }

    internal static string Md5(string value)
    {
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static DouyuSignData? ParseSignParameters(string parameters)
    {
        string? v = null;
        string? did = null;
        string? tt = null;
        string? sign = null;

        foreach (string part in parameters.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);

            if (pair.Length != 2)
            {
                continue;
            }

            string value = WebUtility.UrlDecode(pair[1]);

            switch (pair[0])
            {
                case "v":
                    v = value;
                    break;
                case "did":
                    did = value;
                    break;
                case "tt":
                    tt = value;
                    break;
                case "sign":
                    sign = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(did) || string.IsNullOrWhiteSpace(tt) || string.IsNullOrWhiteSpace(sign))
        {
            return null;
        }

        return new DouyuSignData(v, did, tt, sign);
    }

    private static string? RequestStreamData(string roomId, DouyuSignData signData, string cookie)
    {
        Dictionary<string, string> form = new()
        {
            ["v"] = signData.V,
            ["did"] = signData.Did,
            ["tt"] = signData.Timestamp,
            ["sign"] = signData.Sign,
            ["ver"] = "22011191",
            ["rid"] = roomId,
            ["rate"] = "0",
        };

        string? json = SpiderRequest.PostForm(
            "https://m.douyu.com/hgapi/livenc/room/getStreamUrl",
            form,
            MobileHeaders(),
            cookie);

        if (HasStreamUrl(json))
        {
            return json;
        }

        return SpiderRequest.PostForm(
            $"https://www.douyu.com/lapi/live/getH5Play/{roomId}",
            form,
            MobileHeaders(),
            cookie);
    }

    private static bool HasStreamUrl(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            JObject root = JObject.Parse(json);

            return !string.IsNullOrWhiteSpace(root["data"]?["url"]?.ToString())
                || (!string.IsNullOrWhiteSpace(root["data"]?["rtmp_url"]?.ToString())
                 && !string.IsNullOrWhiteSpace(root["data"]?["rtmp_live"]?.ToString()));
        }
        catch
        {
            return false;
        }
    }

    private static Engine CreateEngine()
    {
        return new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(2)));
    }

    private static IReadOnlyDictionary<string, string> DesktopHeaders()
    {
        return new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0",
            ["Accept-Language"] = "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2",
            ["Referer"] = "https://www.douyu.com/",
        };
    }

    private static IReadOnlyDictionary<string, string> MobileHeaders()
    {
        return new Dictionary<string, string>
        {
            ["User-Agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
            ["Accept-Language"] = "zh-CN,zh;q=0.9",
            ["Referer"] = "https://m.douyu.com/",
        };
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

    [GeneratedRegex("<script\\s+id=\"vike_pageContext\"\\s+type=\"application/json\">(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex MobileContextRegex { get; }

    [GeneratedRegex("(vdwdae325w_64we[\\s\\S]*function ub98484234[\\s\\S]*?)function")]
    private static partial Regex TokenScriptRegex { get; }

    [GeneratedRegex("eval\\(strc\\)(?:\\([^;]*\\))?;\\s*}", RegexOptions.Singleline)]
    private static partial Regex EvalRegex { get; }

    [GeneratedRegex("(function\\s+ub98484234[\\s\\S]*?return\\s+)eval\\(strc\\)(?:\\([^;]*\\))?;\\s*}", RegexOptions.Singleline)]
    private static partial Regex UbEvalRegex { get; }

    [GeneratedRegex("v=(\\d+)")]
    private static partial Regex VersionRegex { get; }

    [GeneratedRegex("return rt;}\\);?")]
    private static partial Regex ReturnRegex { get; }

    [GeneratedRegex("\\(function\\s*\\(")]
    private static partial Regex SignFunctionRegex { get; }

    [GeneratedRegex("CryptoJS\\.MD5\\(cb\\)\\.toString\\(\\)")]
    private static partial Regex CryptoJsRegex { get; }
}

internal sealed record DouyuSignData(string V, string Did, string Timestamp, string Sign);

public sealed class DouyuSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? RoomId { get; set; }

    public string? Title { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
