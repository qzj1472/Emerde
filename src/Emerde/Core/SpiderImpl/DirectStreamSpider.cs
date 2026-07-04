namespace Emerde.Core;

public sealed class DirectStreamSpider : ISpider
{
    public static Lazy<DirectStreamSpider> Instance { get; } = new(() => new DirectStreamSpider());

    public string PlatformName => "Direct";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        string path = uri.AbsolutePath;

        if (!path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
         && !path.EndsWith(".flv", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        DirectStreamSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
            IsLiveStreaming = roomUrl != null,
            Nickname = BuildNickname(roomUrl),
        };

        if (roomUrl?.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) == true)
        {
            result.HlsUrl = roomUrl;
        }
        else if (roomUrl?.Contains(".flv", StringComparison.OrdinalIgnoreCase) == true)
        {
            result.FlvUrl = roomUrl;
        }

        return result;
    }

    private static string BuildNickname(string? roomUrl)
    {
        if (!Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return "Direct Stream";
        }

        string fileName = Path.GetFileName(uri.AbsolutePath);

        return string.IsNullOrWhiteSpace(fileName) ? "Direct Stream" : fileName;
    }
}

public sealed class DirectStreamSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}
