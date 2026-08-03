using System.Diagnostics.CodeAnalysis;
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
        string value = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url.Trim();
        string? normalizedUrl = StreamResolver.NormalizeUrl(value, allowNetwork: false);
        if (string.IsNullOrWhiteSpace(normalizedUrl)
            || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        return uri.Host.Equals("live.douyin.com", StringComparison.OrdinalIgnoreCase)
            ? normalizedUrl
            : null;
    }

    private string? RequestUrl(string? url)
    {
        if (url == null)
        {
            return null;
        }

        return StreamResolver.RequestDouyinText(
            url,
            url,
            "https://live.douyin.com/",
            StreamResolver.GetDouyinAnonymousCookie(),
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0");
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
