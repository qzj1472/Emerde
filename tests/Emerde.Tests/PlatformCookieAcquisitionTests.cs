using System.Xml.Linq;
using Emerde.Core;
using Emerde.Properties;
using System.Globalization;

namespace Emerde.Tests;

public sealed class PlatformCookieAcquisitionTests
{
    private static readonly string[] ConfiguredPlatformNames =
    [
        "Douyin", "Bilibili", "Kuaishou", "Huya", "Douyu", "Baidu", "MaoerFM", "Lianjie",
        "6Rooms", "VVXqiu", "Blued", "Liuxing", "Changliao", "Yinbo", "Zhihu", "PPLive",
        "CatShow", "Laixiu", "JD", "Weibo", "Huajiao", "Look", "Taobao", "Xiaohongshu",
        "Kugou", "Yingke", "AcFun", "YY", "NeteaseCC", "QianduRebo", "TikTok", "Bigo",
        "ShowRoom", "17Live", "CHZZK", "Picarto", "LangLive", "PandaTV", "WinkTV", "Twitch",
        "YouTube", "Shopee", "TwitCasting", "Faceit", "SOOP", "FlexTV", "PopkonTV", "LiveMe",
    ];

    [Fact]
    public void Profiles_CoverEveryConfiguredCookiePlatform()
    {
        Assert.Equal(
            ConfiguredPlatformNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            PlatformCookieAcquisition.SupportedPlatformNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildCookieHeader_FiltersForeignDomainsAndPrefersRootCookies()
    {
        Assert.True(PlatformCookieAcquisition.TryGetProfile("Douyin", out PlatformCookieAcquisitionProfile? profile));
        PlatformBrowserCookie[] cookies =
        [
            new("sessionid", "nested", ".live.douyin.com", "/room"),
            new("sessionid", "root", ".douyin.com", "/"),
            new("theme", "dark", "www.douyin.com", "/"),
            new("foreign", "secret", ".example.com", "/"),
        ];

        string header = PlatformCookieAcquisition.BuildCookieHeader(profile!, cookies);

        Assert.Equal("sessionid=root; theme=dark", header);
    }

    [Fact]
    public void SettingsCookieRows_ExposeAcquisitionCommands()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XElement[] buttons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button"
                && ((string?)element.Attribute("Command"))?.Contains("AcquirePlatformCookieCommand", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(2, buttons.Length);
        Assert.All(buttons, button => Assert.Contains("AcquireCookie", (string?)button.Attribute("Content")));
    }

    [Fact]
    public void SettingsCookieHeaders_DoNotExposeLegacyInstructions()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "SettingsWindow.xaml"));
        XElement[] buttons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button"
                && ((string?)element.Attribute("Command"))?.Contains("OpenHowToGetCookie", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(buttons);
    }

    [Theory]
    [InlineData(".douyin.com", "douyin.com", true)]
    [InlineData("live.douyin.com", "douyin.com", true)]
    [InlineData("evil-douyin.com", "douyin.com", false)]
    [InlineData("douyin.com.example.com", "douyin.com", false)]
    public void DomainMatches_RequiresARealDomainBoundary(string cookieDomain, string allowedDomain, bool expected)
    {
        Assert.Equal(expected, PlatformCookieAcquisition.DomainMatches(cookieDomain, allowedDomain));
    }

    [Fact]
    public void HasAuthenticatedSession_RequiresDouyinLoginCookie()
    {
        Assert.True(PlatformCookieAcquisition.TryGetProfile("Douyin", out PlatformCookieAcquisitionProfile? profile));

        Assert.False(PlatformCookieAcquisition.HasAuthenticatedSession(profile!,
        [
            new PlatformBrowserCookie("ttwid", "anonymous", ".douyin.com", "/"),
        ]));
        Assert.True(PlatformCookieAcquisition.HasAuthenticatedSession(profile!,
        [
            new PlatformBrowserCookie("sessionid", "authenticated", ".douyin.com", "/"),
        ]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void CookieAcquisitionResources_ArePresent(string cultureName)
    {
        CultureInfo? culture = string.IsNullOrEmpty(cultureName) ? null : new CultureInfo(cultureName);
        string[] keys =
        [
            "AcquireCookie", "CookieLoginInitializing", "CookieLoginInstruction", "CookieLoginFinish",
            "CookieLoginRuntimeMissing", "CookieLoginOpenFailed", "CookieLoginReading", "CookieLoginEmpty",
            "CookieLoginReadFailed", "CookieLoginLoading", "CookieLoginNavigationFailed",
            "CookieLoginUnsupported", "CookieLoginSaved",
        ];

        foreach (string key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(Resources.ResourceManager.GetString(key, culture)), key);
            Assert.NotNull(typeof(Resources).GetProperty(key));
        }
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
