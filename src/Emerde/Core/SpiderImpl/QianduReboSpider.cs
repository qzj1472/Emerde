using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class QianduReboSpider : ISpider
{
    public static Lazy<QianduReboSpider> Instance { get; } = new(() => new QianduReboSpider());

    public string PlatformName => "QianduRebo";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "qiandurebo.com" && uri.Host != "www.qiandurebo.com")
        {
            return null;
        }

        string[] segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length == 0)
        {
            return null;
        }

        return $"https://qiandurebo.com/{string.Join('/', segments)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        QianduReboSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string? html = RequestUrl(roomUrl);
        ExtractData(html, result);

        return result;
    }

    internal static void ExtractData(string? html, QianduReboSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        Match dataMatch = UserDataRegex.Match(html);

        if (!dataMatch.Success)
        {
            return;
        }

        string data = dataMatch.Groups[1].Value;
        Match nicknameMatch = NicknameRegex.Match(data);

        if (nicknameMatch.Success)
        {
            result.Nickname = WebUtility.HtmlDecode(nicknameMatch.Groups[1].Value);
        }

        Match playUrlMatch = PlayUrlRegex.Match(data);

        if (!playUrlMatch.Success || html.Contains("common-text-center\" style=\"display:block", StringComparison.OrdinalIgnoreCase))
        {
            result.IsLiveStreaming = false;
            return;
        }

        string flvUrl = WebUtility.HtmlDecode(playUrlMatch.Groups[1].Value)
            .Replace("\\/", "/");

        result.IsLiveStreaming = true;
        result.FlvUrl = flvUrl;
    }

    private static string? RequestUrl(string url)
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

        string cookie = PlatformCookieStore.GetCookie("QianduRebo", SecretProtector.GetChinaCookie());

        request.AddHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        request.AddHeader("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6");
        request.AddHeader("Referer", "https://qiandurebo.com/web/index.php");
        request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.AddHeader("Cookie", cookie);
        }

        RestResponse response = client.Execute(request);

        return response.IsSuccessful ? response.Content : null;
    }

    [GeneratedRegex("var user = (.*?)\\r?\\n\\s+user\\.play_url", RegexOptions.Singleline)]
    private static partial Regex UserDataRegex { get; }

    [GeneratedRegex("\"zb_nickname\"\\s*:\\s*\"(.*?)\"\\s*,", RegexOptions.Singleline)]
    private static partial Regex NicknameRegex { get; }

    [GeneratedRegex("\"play_url\"\\s*:\\s*\"(.*?)\"\\s*,", RegexOptions.Singleline)]
    private static partial Regex PlayUrlRegex { get; }
}

public sealed class QianduReboSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
