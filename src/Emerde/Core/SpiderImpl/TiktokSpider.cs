using Newtonsoft.Json;
using RestSharp;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Emerde.Core;

[SuppressMessage("Performance", "CA1822:Mark members as static")]
public sealed partial class TiktokSpider : ISpider
{
    public static Lazy<TiktokSpider> Instance { get; } = new(() => new TiktokSpider());

    public string PlatformName => "TikTok";

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        string? htmlStr = RequestUrl(roomUrl);
        TiktokSpiderResult result = ExtractData(htmlStr);

        result.RoomUrl = roomUrl;
        result.PlatformName = PlatformName;
        return result;
    }

    public string? ParseUrl(string url)
    {
        // Supported two case URLs:
        // https://www.tiktok.com/@xxx/live
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.tiktok.com")
        {
            return null;
        }

        string userId = uri.Segments.Last();

        if (!userId.StartsWith('@'))
        {
            if (uri.Segments.Length >= 2)
            {
                userId = uri.Segments[^2].Trim('/');

                if (userId.StartsWith('@'))
                {
                    string roomUrl = $"https://www.tiktok.com/{userId}/live";
                    return roomUrl;
                }
            }
        }

        return null;
    }

    private string? RequestUrl(string? url)
    {
        if (url == null)
        {
            return null;
        }

        RestClientOptions options = new()
        {
            BaseUrl = new Uri(url),
        };

        using RestClient client = new(options);

        RestRequest request = new()
        {
            Method = Method.Get,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = PlatformCookieStore.GetCookie("TikTok", SecretProtector.GetOverseaCookie());

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2");
        request.AddHeader("Referer", "https://www.tiktok.com/");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        var response = client.Execute(request);

        if (response.IsSuccessful)
        {
            string? htmlStr = response.Content;

            return htmlStr;
        }
        else
        {
            Console.WriteLine($"{response.ErrorMessage}");
            return null!;
        }
    }

    public static TiktokSpiderResult ExtractData(string? htmlStr)
    {
        TiktokSpiderResult result = new();

        if (string.IsNullOrWhiteSpace(htmlStr))
        {
            return result;
        }

        if (htmlStr.Contains("We regret to inform you that we have discontinued operating TikTok"))
        {
            // Your proxy node's regional network is blocked from accessing TikTok;
            // please switch to a node in another region to access.
            return result;
        }

        if (htmlStr.Contains("UNEXPECTED_EOF_WHILE_READING"))
        {
            // UNEXPECTED_EOF_WHILE_READING
            return result;
        }

        Match match = JsonRegex.Match(htmlStr);

        if (match.Success)
        {
            string jsonStr = match.Groups[1].Value;

            try
            {
                dynamic? json = JsonConvert.DeserializeObject(jsonStr);
                dynamic? liveRoom = json!["LiveRoom"]["liveRoomUserInfo"];
                dynamic? user = liveRoom["user"];

                result.UniqueId = user["uniqueId"];
                result.Nickname = user["nickname"];
                result.AvatarThumbUrl = user["avatarThumb"];
                result.IsLiveStreaming = user["status"] == "2";

                if (result.IsLiveStreaming == false)
                {
                    return result;
                }

                dynamic? streamData = liveRoom["liveRoom"]["streamData"]["pull_data"]["stream_data"];
                streamData = JsonConvert.DeserializeObject(streamData.ToString())["data"];

                result.FlvUrl = streamData["origin"]["main"]["flv"];
                result.HlsUrl = streamData["origin"]["main"]["hls"];
            }
            catch
            {
                ///
            }
        }

        return result;
    }

    [GeneratedRegex("<script id=\"SIGI_STATE\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonRegex { get; }
}

public sealed class TiktokSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? UniqueId { get; set; }

    public string AnchorName => $"{Nickname}-{UniqueId}";

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
