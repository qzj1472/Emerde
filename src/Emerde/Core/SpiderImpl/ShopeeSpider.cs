using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using System.Text.RegularExpressions;

namespace Emerde.Core;

public sealed class ShopeeSpider : ISpider
{
    public static Lazy<ShopeeSpider> Instance { get; } = new(() => new ShopeeSpider());

    public string PlatformName => "Shopee";

    public string? ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (!uri.Host.StartsWith("live.shopee.", StringComparison.OrdinalIgnoreCase) && uri.Host != "shp.ee")
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + uri.Query;
    }

    public ISpiderResult GetResult(string url)
    {
        string? roomUrl = ParseUrl(url);
        ShopeeSpiderResult result = new()
        {
            RoomUrl = roomUrl,
            PlatformName = PlatformName,
        };

        if (roomUrl == null)
        {
            return result;
        }

        string resolvedUrl = roomUrl.Contains("live.shopee.", StringComparison.OrdinalIgnoreCase)
            ? roomUrl
            : ResolveRedirectUrl(roomUrl) ?? roomUrl;

        if (!Uri.TryCreate(resolvedUrl, UriKind.Absolute, out Uri? uri))
        {
            return result;
        }

        string hostSuffix = uri.Host.StartsWith("live.shopee.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host["live.shopee.".Length..]
            : uri.Host.Split('.')[0];
        string apiHost = $"https://live.shopee.{hostSuffix}";
        string? uid = GetQueryValue(uri.Query, "uid");
        string? sessionId = GetQueryValue(uri.Query, "session");
        bool isLiving = uri.Host.StartsWith("live.shopee.", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(uid);

        if (!string.IsNullOrWhiteSpace(uid))
        {
            string? ongoingJson = SpiderRequest.Get($"{apiHost}/api/v1/shop_page/live/ongoing?uid={Uri.EscapeDataString(uid)}", Headers(), PlatformCookieStore.GetCookie("Shopee", Configurations.CookieOversea.Get()));
            sessionId = ExtractOngoingSessionId(ongoingJson);
            isLiving = !string.IsNullOrWhiteSpace(sessionId);

            if (!isLiving)
            {
                string? replayJson = SpiderRequest.Get($"{apiHost}/api/v1/shop_page/live/replay_list?offset=0&limit=1&uid={Uri.EscapeDataString(uid)}", Headers(), PlatformCookieStore.GetCookie("Shopee", Configurations.CookieOversea.Get()));
                ExtractReplayInfo(replayJson, result);
                return result;
            }
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return result;
        }

        string? sessionJson = SpiderRequest.Get($"{apiHost}/api/v1/session/{Uri.EscapeDataString(sessionId)}", Headers(), PlatformCookieStore.GetCookie("Shopee", Configurations.CookieOversea.Get()));
        ExtractSession(sessionJson, isLiving, result);

        return result;
    }

    internal static string? ExtractOngoingSessionId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(json);
            return root["data"]?["ongoing_live"]?["session_id"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static void ExtractReplayInfo(string? json, ShopeeSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? replay = root["data"]?["replay"]?.FirstOrDefault() as JObject;
            result.Nickname = replay?["nick_name"]?.ToString();
            result.IsLiveStreaming = false;
        }
        catch
        {
        }
    }

    internal static void ExtractSession(string? json, bool isLiving, ShopeeSpiderResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JObject root = JObject.Parse(json);
            JObject? session = root["data"]?["session"] as JObject;

            if (session == null)
            {
                return;
            }

            result.Nickname = session["nickname"]?.ToString();
            result.IsLiveStreaming = session["status"]?.Value<int>() == 1 && isLiving;

            if (result.IsLiveStreaming == true)
            {
                result.FlvUrl = session["play_url"]?.ToString();
            }
        }
        catch
        {
        }
    }

    private static string? ResolveRedirectUrl(string url)
    {
        try
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

            RestClient client = new(options);
            RestRequest request = new()
            {
                Method = Method.Get,
                Timeout = TimeSpan.FromSeconds(5),
            };

            foreach ((string key, string value) in Headers())
            {
                request.AddHeader(key, value);
            }

            RestResponse response = client.Execute(request);

            return response.ResponseUri?.ToString();
        }
        catch
        {
            return null;
        }
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
            ["accept"] = "application/json, text/plain, */*",
            ["accept-language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
            ["referer"] = "https://live.shopee.sg/share?from=live&session=802458&share_user_id=",
            ["user-agent"] = "ios/7.830 (ios 17.0; ; iPhone 15 (A2846/A3089/A3090/A3092))",
        };
    }
}

public sealed class ShopeeSpiderResult : ISpiderResult
{
    public string? RoomUrl { get; set; }

    public string? PlatformName { get; set; }

    public bool? IsLiveStreaming { get; set; }

    public string? Nickname { get; set; }

    public string? AvatarThumbUrl { get; set; }

    public string? FlvUrl { get; set; }

    public string? HlsUrl { get; set; }
}

