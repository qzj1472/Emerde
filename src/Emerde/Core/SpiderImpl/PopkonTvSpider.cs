using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed partial class PopkonTvSpider : ISpider
{
    public static Lazy<PopkonTvSpider> Instance { get; } = new(() => new PopkonTvSpider());

    public string PlatformName => "PopkonTV";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host != "www.popkontv.com")
        {
            return null;
        }

        string? castId = GetQueryValue(uri.Query, "castId") ?? GetQueryValue(uri.Query, "mcid");

        if (string.IsNullOrWhiteSpace(castId))
        {
            return null;
        }

        string? partnerCode = GetQueryValue(uri.Query, "partnerCode") ?? GetQueryValue(uri.Query, "mcPartnerCode") ?? "P-00001";

        return $"https://www.popkontv.com/live/view?castId={Uri.EscapeDataString(castId)}&partnerCode={Uri.EscapeDataString(partnerCode)}";
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        PopkonTvSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null || !Uri.TryCreate(roomUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string? castId = GetQueryValue(uri.Query, "castId");
        string? partnerCode = GetQueryValue(uri.Query, "partnerCode") ?? "P-00001";

        if (string.IsNullOrWhiteSpace(castId))
        {
            return result;
        }

        string? html = SpiderRequest.Get(roomUrl, Headers(), PlatformCookieStore.GetCookie("PopkonTV", Configurations.CookieOversea.Get()));
        ExtractNextData(html, result);

        if (result.IsLiveStreaming == true &&
            !string.IsNullOrWhiteSpace(result.CastStartDate) &&
            !string.IsNullOrWhiteSpace(result.CastSignId) &&
            !string.IsNullOrWhiteSpace(result.CastType))
        {
            string? watchJson = SpiderRequest.PostJson(
                "https://www.popkontv.com/api/proxy/broadcast/v1/castwatchonoffguest",
                BuildWatchBody(result, partnerCode, GetQueryValue(uri.Query, "pwd")),
                Headers(),
                PlatformCookieStore.GetCookie("PopkonTV", Configurations.CookieOversea.Get()));
            ExtractWatchInfo(watchJson, result);
        }

        return result;
    }

    internal static void ExtractNextData(string? html, PopkonTvSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        try
        {
            Match match = NextDataRegex.Match(html);

            if (!match.Success)
            {
                return;
            }

            JObject root = JObject.Parse(match.Groups[1].Value);
            JObject? roomData = root["props"]?["pageProps"]?["mcData"]?["data"] as JObject;

            if (roomData == null)
            {
                return;
            }

            result.Nickname = roomData["mc_nickName"]?.ToString() ?? roomData["mcNickName"]?.ToString() ?? roomData["mc_signId"]?.ToString();
            result.CastStartDate = roomData["mc_castStartDate"]?.ToString();
            result.CastSignId = roomData["mc_signId"]?.ToString();
            result.CastType = roomData["castType"]?.ToString();
            result.IsPrivate = roomData["mc_isPrivate"]?.ToString();
            result.IsLiveStreaming = true;
        }
        catch
        {
        }
    }

    internal static void ExtractWatchInfo(string? json, PopkonTvSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            string? statusCode = root["statusCd"]?.ToString();

            if (statusCode is "L0000" or "L0001")
            {
                result.HlsUrl = root["data"]?["castHlsUrl"]?.ToString();
            }
        }
        catch
        {
        }
    }

    private static string BuildWatchBody(PopkonTvSpiderResult result, string partnerCode, string? roomPassword)
    {
        return JsonConvert.SerializeObject(new
        {
            androidStore = 0,
            castCode = $"{result.CastSignId}-{result.CastStartDate}",
            castPartnerCode = partnerCode,
            castSignId = result.CastSignId,
            castType = result.CastType,
            commandType = 0,
            exePath = 5,
            isSecret = result.IsPrivate ?? "0",
            partnerCode,
            password = roomPassword ?? string.Empty,
            signId = string.Empty,
            version = "4.6.2",
        });
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

    private static IReadOnlyDictionary<string, string> Headers()
    {
        return new Dictionary<string, string>
        {
            ["Accept"] = "application/json, text/plain, */*",
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["ClientKey"] = "Client FpAhe6mh8Qtz116OENBmRddbYVirNKasktdXQiuHfm88zRaFydTsFy63tzkdZY0u",
            ["Content-Type"] = "application/json",
            ["Origin"] = "https://www.popkontv.com",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0",
        };
    }

    [GeneratedRegex("<script id=\"__NEXT_DATA__\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex NextDataRegex { get; }
}

public sealed class PopkonTvSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public string? CastStartDate { get; set; }

    public string? CastSignId { get; set; }

    public string? CastType { get; set; }

    public string? IsPrivate { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

