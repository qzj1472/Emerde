using RestSharp;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

[SuppressMessage("Performance", "CA1822:Mark members as static")]
public sealed partial class DouyinSpider : ISpider
{
    public static Lazy<DouyinSpider> Instance { get; } = new(() => new DouyinSpider());

    public string PlatformName => "Douyin";

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        string? htmlStr = RequestUrl(roomUrl);
        DouyinSpiderResult result = ExtractData(htmlStr);

        result.RoomUrl = roomUrl;
        result.PlatformName = PlatformName;
        return result;
    }

    public string? ParseUrl(string url)
    {
        // Supported two case URLs:
        // https://live.douyin.com/xxx?x=x
        // https://www.douyin.com/root/live/xxx?x=x
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "live.douyin.com" && uri.Host != "www.douyin.com")
        {
            return null;
        }

        string roomId = uri.Segments.Last();
        string roomUrl = $"https://live.douyin.com/{roomId}";

        return roomUrl;
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

        if (Configurations.IsUseProxy.Get())
        {
            string proxyUrl = Configurations.ProxyUrl.Get();

            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                options.Proxy = new WebProxy($"http://{proxyUrl}")
                {
                };
            }
        }

        RestClient client = new(options);

        RestRequest request = new()
        {
            Method = Method.Get,
            Timeout = TimeSpan.FromSeconds(5),
        };

        string cookie = PlatformCookieStore.GetCookie("Douyin", Configurations.CookieChina.Get());

        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.8,zh-TW;q=0.7,zh-HK;q=0.5,en-US;q=0.3,en;q=0.2");
        request.AddHeader("Referer", "https://live.douyin.com/");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

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

    public static DouyinSpiderResult ExtractData(string? htmlStr)
    {
        DouyinSpiderResult result = new();

        if (string.IsNullOrWhiteSpace(htmlStr))
        {
            return result;
        }

        if (htmlStr.Contains("\\\"status_str\\\":\\\"2\\\""))
        {
            result.IsLiveStreaming = true;
        }
        else if (htmlStr.Contains("\\\"status_str\\\":\\\"4\\\""))
        {
            result.IsLiveStreaming = false;
        }

        Match match = NickNameRegex.Match(htmlStr.Replace("\\\"nickname\\\":\\\"$undefined\\\",", string.Empty));
        if (match.Success)
        {
            result.Nickname = match.Groups[1].Value;
        }

        match = AvatarThumbUrlRegex.Match(htmlStr);
        if (match.Success)
        {
            result.AvatarThumbUrl = match.Groups[1].Value
                .Replace("\\u0026", "&");
        }

        if (result.IsLiveStreaming == false)
        {
            return result;
        }

        match = HlsPullUrlMapRegex.Match(htmlStr);
        if (match.Success)
        {
            result.HlsUrl = match.Groups[1].Value
                .Replace("\\u0026", "&");
        }

        return result;
    }

    [GeneratedRegex("\\\\\"nickname\\\\\":\\\\\"([^\\\"]+)\\\\\",\\\\\"avatar_thumb")]
    private static partial Regex NickNameRegex { get; }

    [GeneratedRegex("avatar_thumb\\\\\":\\{\\\\\"url_list\\\\\":\\[\\\\\"(.*?)\\\\\"")]
    private static partial Regex AvatarThumbUrlRegex { get; }

    [GeneratedRegex("\\\\\"hls_pull_url_map\\\\\":{\\\\\"FULL_HD1\\\\\":\\\\\"(.*?)\\\\\"")]
    private static partial Regex HlsPullUrlMapRegex { get; }
}

public sealed class DouyinSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    /// <summary>
    /// \"status_str\":\"2\" -> true
    /// \"status_str\":\"4\" -> false
    /// </summary>
    public bool? IsLiveStreaming { get; set; } = null;

    /// <summary>
    /// Remove "\"nickname\":\"$undefined\","
    /// "\"nickname\":\"(.*?)\",\"avatar_thumb"
    /// </summary>
    public string? Nickname { get; set; }

    /// <summary>
    /// \"url_list\":[\"(.*?)\"
    /// </summary>
    public string? AvatarThumbUrl { get; set; }

    /// <summary>
    /// TODO
    /// </summary>
    public string? FlvUrl { get; set; }

    /// <summary>
    /// "\"hls_pull_url_map\":{\"FULL_HD1\":\"(.*?)\""
    /// </summary>
    public string? HlsUrl { get; set; }
}
