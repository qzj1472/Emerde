using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class LiveMeSpider : ISpider
{
    private const string AppId = "LM6000101139961122666757";
    private const string SignKey = "dd46dbb442b6e4ba817d6347d2ddf493";

    public static Lazy<LiveMeSpider> Instance { get; } = new(() => new LiveMeSpider());

    public string PlatformName => "LiveMe";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.liveme.com")
        {
            return null;
        }

        string[] segments = uri.Segments.Select(segment => segment.Trim('/')).Where(segment => !string.IsNullOrWhiteSpace(segment)).ToArray();
        int videoIndex = Array.IndexOf(segments, "v");

        if (videoIndex < 0 || videoIndex + 1 >= segments.Length)
        {
            return null;
        }

        return $"https://www.liveme.com/zh/v/{segments[videoIndex + 1]}/index.html";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        LiveMeSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string[] segments = roomUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string videoId = segments[^2];
        LiveMeSignData signData = CreateSignData(videoId);
        string api = "https://live.liveme.com/live/queryinfosimple?alias=liveme&tongdun_black_box=&os=web";
        string? json = PostSignedForm(api, signData, PlatformCookieStore.GetCookie("LiveMe", SecretProtector.GetOverseaCookie()));
        ExtractVideoInfo(json, result);

        return result;
    }

    internal static LiveMeSignData CreateSignData(string videoId)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string lmTimestamp = $"{timestamp}1";
        string lmString = Md5(lmTimestamp);
        Dictionary<string, string> fields = new()
        {
            ["lm_s_id"] = AppId,
            ["lm_s_ts"] = lmTimestamp,
            ["lm_s_str"] = lmString,
            ["lm_s_ver"] = "1",
            ["h5"] = "1",
            ["_time"] = timestamp.ToString(),
            ["thirdchannel"] = "6",
            ["videoid"] = videoId,
            ["area"] = "zh",
            ["vali"] = CreateSignature(),
        };
        Dictionary<string, string> signFields = new(fields)
        {
            ["alias"] = "liveme",
            ["tongdun_black_box"] = string.Empty,
            ["os"] = "web",
        };
        string sign = Md5(string.Concat(signFields.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + item.Value)) + fields["lm_s_id"] + fields["lm_s_ts"] + SignKey);

        return new LiveMeSignData(fields, sign);
    }

    internal static void ExtractVideoInfo(string? json, LiveMeSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? videoInfo = root["data"]?["video_info"] as JObject;

            if (videoInfo == null)
            {
                return;
            }

            result.Nickname = videoInfo["uname"]?.ToString();
            result.IsLiveStreaming = videoInfo["status"]?.ToString() == "0";

            if (result.IsLiveStreaming == true)
            {
                result.HlsUrl = videoInfo["hlsvideosource"]?.ToString();
                result.FlvUrl = videoInfo["videosource"]?.ToString();
            }
        }
        catch
        {
        }
    }

    private static string? PostSignedForm(string url, LiveMeSignData signData, string cookie)
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
            Method = Method.Post,
            Timeout = TimeSpan.FromSeconds(5),
        };

        request.AddHeader("origin", "https://www.liveme.com");
        request.AddHeader("referer", "https://www.liveme.com");
        request.AddHeader("user-agent", "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))");
        request.AddHeader("lm-s-sign", signData.Sign);

        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        foreach ((string key, string value) in signData.Fields)
        {
            request.AddParameter(key, value);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    private static string CreateSignature()
    {
        const string pattern = "4l4m5";
        const string chars = "ABCDEFGHJKMNPQRSTWXYZabcdefhijkmnprstwxyz2345678";
        StringBuilder builder = new();
        int count = 0;

        foreach (char c in pattern)
        {
            if (char.IsDigit(c))
            {
                count = count * 10 + (c - '0');
                continue;
            }

            AppendRandom(builder, chars, count);
            count = 0;
            builder.Append(c);
        }

        AppendRandom(builder, chars, count);

        return builder.ToString();
    }

    private static void AppendRandom(StringBuilder builder, string chars, int count)
    {
        for (int i = 0; i < count; i++)
        {
            builder.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
        }
    }

    private static string Md5(string value)
    {
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

internal sealed record LiveMeSignData(IReadOnlyDictionary<string, string> Fields, string Sign);

public sealed class LiveMeSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
