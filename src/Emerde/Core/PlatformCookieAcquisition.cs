namespace Emerde.Core;

internal sealed record PlatformCookieAcquisitionProfile(
    string PlatformName,
    Uri LoginUri,
    IReadOnlyList<Uri> CookieOrigins,
    IReadOnlySet<string> AllowedDomains);

internal readonly record struct PlatformBrowserCookie(string Name, string Value, string Domain, string Path);

internal static class PlatformCookieAcquisition
{
    private static readonly IReadOnlyDictionary<string, PlatformCookieAcquisitionProfile> Profiles = CreateProfiles();
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RequiredAuthenticationCookies =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Douyin"] = new HashSet<string>(["sessionid", "sessionid_ss", "sid_guard"], StringComparer.Ordinal),
            ["Bilibili"] = new HashSet<string>(["SESSDATA", "DedeUserID"], StringComparer.Ordinal),
        };

    public static IReadOnlyCollection<string> SupportedPlatformNames => Profiles.Keys.ToArray();

    public static bool TryGetProfile(string platformName, out PlatformCookieAcquisitionProfile? profile)
    {
        return Profiles.TryGetValue(platformName, out profile);
    }

    public static string BuildCookieHeader(
        PlatformCookieAcquisitionProfile profile,
        IEnumerable<PlatformBrowserCookie> cookies)
    {
        return string.Join("; ", cookies
            .Where(cookie => !string.IsNullOrWhiteSpace(cookie.Name)
                && profile.AllowedDomains.Any(domain => DomainMatches(cookie.Domain, domain)))
            .GroupBy(cookie => cookie.Name.Trim(), StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(cookie => string.Equals(cookie.Path, "/", StringComparison.Ordinal))
                .ThenBy(cookie => NormalizeDomain(cookie.Domain).Length)
                .ThenBy(cookie => cookie.Path?.Length ?? 0)
                .First())
            .OrderBy(cookie => cookie.Name, StringComparer.Ordinal)
            .Select(cookie => $"{cookie.Name.Trim()}={cookie.Value}"));
    }

    public static bool HasAuthenticatedSession(
        PlatformCookieAcquisitionProfile profile,
        IEnumerable<PlatformBrowserCookie> cookies)
    {
        PlatformBrowserCookie[] relevantCookies = cookies
            .Where(cookie => profile.AllowedDomains.Any(domain => DomainMatches(cookie.Domain, domain)))
            .ToArray();
        if (!RequiredAuthenticationCookies.TryGetValue(profile.PlatformName, out IReadOnlySet<string>? requiredNames))
        {
            return relevantCookies.Any(cookie => !string.IsNullOrWhiteSpace(cookie.Name));
        }

        return relevantCookies.Any(cookie => requiredNames.Contains(cookie.Name));
    }

    internal static bool DomainMatches(string cookieDomain, string allowedDomain)
    {
        string normalizedCookieDomain = NormalizeDomain(cookieDomain);
        string normalizedAllowedDomain = NormalizeDomain(allowedDomain);
        return normalizedCookieDomain.Equals(normalizedAllowedDomain, StringComparison.OrdinalIgnoreCase)
            || normalizedCookieDomain.EndsWith($".{normalizedAllowedDomain}", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDomain(string domain)
    {
        return domain.Trim().TrimStart('.');
    }

    private static IReadOnlyDictionary<string, PlatformCookieAcquisitionProfile> CreateProfiles()
    {
        PlatformCookieAcquisitionProfile[] profiles =
        [
            Create("Douyin", "https://www.douyin.com/", ["douyin.com"], "https://www.douyin.com/", "https://live.douyin.com/"),
            Create("Bilibili", "https://passport.bilibili.com/login", ["bilibili.com"], "https://www.bilibili.com/", "https://live.bilibili.com/"),
            Create("Kuaishou", "https://live.kuaishou.com/", ["kuaishou.com"], "https://www.kuaishou.com/", "https://live.kuaishou.com/"),
            Create("Huya", "https://www.huya.com/", ["huya.com"], "https://www.huya.com/", "https://mp.huya.com/"),
            Create("Douyu", "https://www.douyu.com/", ["douyu.com"], "https://www.douyu.com/", "https://m.douyu.com/"),
            Create("Baidu", "https://passport.baidu.com/v2/?login", ["baidu.com"], "https://www.baidu.com/", "https://live.baidu.com/"),
            Create("MaoerFM", "https://fm.missevan.com/", ["missevan.com"], "https://fm.missevan.com/"),
            Create("Lianjie", "https://show.lailianjie.com/", ["lailianjie.com"], "https://show.lailianjie.com/"),
            Create("6Rooms", "https://v.6.cn/", ["6.cn", "6rooms.com"], "https://v.6.cn/"),
            Create("VVXqiu", "https://h5p.vvxqiu.com/", ["vvxqiu.com"], "https://h5p.vvxqiu.com/"),
            Create("Blued", "https://app.blued.cn/", ["blued.cn"], "https://app.blued.cn/"),
            Create("Liuxing", "https://www.7u66.com/", ["7u66.com"], "https://www.7u66.com/", "https://wap.7u66.com/"),
            Create("Changliao", "https://live.tlclw.com/", ["tlclw.com"], "https://live.tlclw.com/", "https://wap.tlclw.com/"),
            Create("Yinbo", "https://live.ybw1666.com/", ["ybw1666.com"], "https://live.ybw1666.com/", "https://wap.ybw1666.com/"),
            Create("Zhihu", "https://www.zhihu.com/signin", ["zhihu.com"], "https://www.zhihu.com/"),
            Create("PPLive", "https://m.pp.weimipopo.com/", ["weimipopo.com"], "https://m.pp.weimipopo.com/"),
            Create("CatShow", "https://h.catshow168.com/", ["catshow168.com"], "https://h.catshow168.com/"),
            Create("Laixiu", "https://www.imkktv.com/", ["imkktv.com"], "https://www.imkktv.com/"),
            Create("JD", "https://passport.jd.com/new/login.aspx", ["jd.com"], "https://www.jd.com/", "https://lives.jd.com/"),
            Create("Weibo", "https://weibo.com/", ["weibo.com"], "https://weibo.com/"),
            Create("Huajiao", "https://www.huajiao.com/", ["huajiao.com"], "https://www.huajiao.com/", "https://live.huajiao.com/"),
            Create("Look", "https://look.163.com/", ["163.com"], "https://look.163.com/"),
            Create("Taobao", "https://login.taobao.com/", ["taobao.com"], "https://www.taobao.com/", "https://tbzb.taobao.com/"),
            Create("Xiaohongshu", "https://www.xiaohongshu.com/", ["xiaohongshu.com", "xhs.cn"], "https://www.xiaohongshu.com/"),
            Create("Kugou", "https://fanxing.kugou.com/", ["kugou.com"], "https://fanxing.kugou.com/"),
            Create("Yingke", "https://www.inke.cn/", ["inke.cn"], "https://www.inke.cn/"),
            Create("AcFun", "https://www.acfun.cn/login/", ["acfun.cn"], "https://www.acfun.cn/", "https://live.acfun.cn/"),
            Create("YY", "https://www.yy.com/", ["yy.com"], "https://www.yy.com/"),
            Create("NeteaseCC", "https://cc.163.com/", ["163.com"], "https://cc.163.com/"),
            Create("QianduRebo", "https://qiandurebo.com/", ["qiandurebo.com"], "https://qiandurebo.com/"),
            Create("TikTok", "https://www.tiktok.com/login", ["tiktok.com"], "https://www.tiktok.com/"),
            Create("Bigo", "https://www.bigo.tv/", ["bigo.tv"], "https://www.bigo.tv/"),
            Create("ShowRoom", "https://www.showroom-live.com/", ["showroom-live.com"], "https://www.showroom-live.com/"),
            Create("17Live", "https://17.live/", ["17.live", "17app.co"], "https://17.live/"),
            Create("CHZZK", "https://chzzk.naver.com/", ["naver.com"], "https://chzzk.naver.com/"),
            Create("Picarto", "https://www.picarto.tv/", ["picarto.tv"], "https://www.picarto.tv/"),
            Create("LangLive", "https://www.lang.live/", ["lang.live"], "https://www.lang.live/"),
            Create("PandaTV", "https://www.pandalive.co.kr/", ["pandalive.co.kr"], "https://www.pandalive.co.kr/"),
            Create("WinkTV", "https://www.winktv.co.kr/", ["winktv.co.kr"], "https://www.winktv.co.kr/"),
            Create("Twitch", "https://www.twitch.tv/login", ["twitch.tv"], "https://www.twitch.tv/"),
            Create("YouTube", "https://accounts.google.com/ServiceLogin?service=youtube", ["youtube.com"], "https://www.youtube.com/"),
            Create("Shopee", "https://live.shopee.sg/", ["shopee.sg"], "https://live.shopee.sg/"),
            Create("TwitCasting", "https://twitcasting.tv/indexlogin.php", ["twitcasting.tv"], "https://twitcasting.tv/"),
            Create("Faceit", "https://www.faceit.com/", ["faceit.com"], "https://www.faceit.com/"),
            Create("SOOP", "https://www.sooplive.com/", ["sooplive.com", "sooplive.co.kr"], "https://www.sooplive.com/", "https://play.sooplive.co.kr/"),
            Create("FlexTV", "https://www.flextv.co.kr/", ["flextv.co.kr", "ttinglive.com"], "https://www.flextv.co.kr/", "https://www.ttinglive.com/"),
            Create("PopkonTV", "https://www.popkontv.com/", ["popkontv.com"], "https://www.popkontv.com/"),
            Create("LiveMe", "https://www.liveme.com/", ["liveme.com"], "https://www.liveme.com/", "https://live.liveme.com/"),
        ];

        return profiles.ToDictionary(profile => profile.PlatformName, StringComparer.OrdinalIgnoreCase);
    }

    private static PlatformCookieAcquisitionProfile Create(
        string platformName,
        string loginUri,
        string[] allowedDomains,
        params string[] cookieOrigins)
    {
        return new PlatformCookieAcquisitionProfile(
            platformName,
            new Uri(loginUri),
            cookieOrigins.Select(origin => new Uri(origin)).Distinct().ToArray(),
            new HashSet<string>(allowedDomains.Select(NormalizeDomain), StringComparer.OrdinalIgnoreCase));
    }
}
