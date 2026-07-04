using Newtonsoft.Json.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed class LaixiuSpider : ISpider
{
    public static Lazy<LaixiuSpider> Instance { get; } = new(() => new LaixiuSpider());

    public string PlatformName => "Laixiu";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.imkktv.com")
        {
            return null;
        }

        string? roomId = GetQueryValue(uri.Query, "roomId") ?? GetQueryValue(uri.Query, "anchorId");

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return null;
        }

        return $"https://www.imkktv.com/h5/share/video.html?roomId={Uri.EscapeDataString(roomId)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        LaixiuSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string? roomId = GetQueryValue(uri.Query, "roomId");

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return result;
        }

        LaixiuSignData sign = CreateSignData();
        string? json = SpiderRequest.Get(
            $"https://api.imkktv.com/liveroom/getShareLiveVideo?roomId={Uri.EscapeDataString(roomId)}",
            Headers(sign),
            Configurations.CookieChina.Get());
        ExtractShareLiveVideo(json, result);

        return result;
    }

    internal static void ExtractShareLiveVideo(string? json, LaixiuSpiderResult result)
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

            result.Nickname = data["nickname"]?.ToString();
            result.AvatarThumbUrl = data["avatar"]?.ToString();
            result.IsLiveStreaming = data["playStatus"]?.Value<int>() == 0;

            if (result.IsLiveStreaming == true)
            {
                result.FlvUrl = data["playUrl"]?.ToString();
            }
        }
        catch
        {
        }
    }

    private static LaixiuSignData CreateSignData()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string imei = Guid.NewGuid().ToString("N");
        string input = $"web{imei}{timestamp}kk792f28d6ff1f34ec702c08626d454b39pro";
        string requestId = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

        return new LaixiuSignData(timestamp, imei, requestId);
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

    private static IReadOnlyDictionary<string, string> Headers(LaixiuSignData sign)
    {
        return new Dictionary<string, string>
        {
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["channel"] = "9",
            ["imei"] = sign.Imei,
            ["loginType"] = "2",
            ["mobileModel"] = "web",
            ["os"] = "web",
            ["Origin"] = "https://www.imkktv.com",
            ["platform"] = "WEB",
            ["Referer"] = "https://www.imkktv.com/",
            ["requestId"] = sign.RequestId,
            ["timestamp"] = sign.Timestamp.ToString(),
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36 Edg/138.0.0.0",
            ["version"] = "1.0.0",
            ["versionCode"] = "10003",
        };
    }
}

internal sealed record LaixiuSignData(long Timestamp, string Imei, string RequestId);

public sealed class LaixiuSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

