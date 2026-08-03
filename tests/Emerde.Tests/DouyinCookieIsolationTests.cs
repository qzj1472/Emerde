using Emerde.Core;

namespace Emerde.Tests;

public sealed class DouyinCookieIsolationTests
{
    [Fact]
    public void AutomaticDouyinResolversStartAnonymousAndDeferRiskCookieFallback()
    {
        string resolver = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "StreamResolver.cs"));
        string legacyResolver = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "SpiderImpl", "DouyinSpider.cs"));

        Assert.DoesNotContain("PlatformCookieStore.GetCookie(\"Douyin\"", legacyResolver, StringComparison.Ordinal);
        Assert.Contains("GetDouyinAnonymousCookie()", resolver, StringComparison.Ordinal);
        Assert.Contains("GetDouyinAnonymousCookie()", legacyResolver, StringComparison.Ordinal);
        Assert.Contains("TryGetDouyinRiskControlFallback(requestCookie", resolver, StringComparison.Ordinal);
        Assert.Contains("IsDouyinRiskControlSignal((int)response.StatusCode, text)", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void RiskControlCookieExcludesAccountIdentity()
    {
        string cookie = StreamResolver.BuildDouyinRiskControlCookie(
            "sessionid=account; sid_tt=sid; uid_tt=user; passport_csrf_token=csrf; ttwid=device; msToken=token; s_v_web_id=verify; __ac_nonce=nonce; __ac_signature=signature; odin_tt=odin");

        Assert.Equal("ttwid=device; msToken=token; s_v_web_id=verify; __ac_nonce=nonce; __ac_signature=signature", cookie);
        Assert.DoesNotContain("sessionid", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sid_tt", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uid_tt", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passport", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("odin_tt", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RiskControlCookieRequiresAnonymousFields()
    {
        Assert.Empty(StreamResolver.BuildDouyinRiskControlCookie("sessionid=account; uid_tt=user"));
        Assert.Empty(StreamResolver.BuildDouyinRiskControlCookie(string.Empty));
    }

    [Theory]
    [InlineData(403, "", true)]
    [InlineData(429, "", true)]
    [InlineData(200, "<html>captcha_verify_container</html>", true)]
    [InlineData(200, "", false)]
    [InlineData(500, "", false)]
    [InlineData(503, "<html>temporarily unavailable</html>", false)]
    public void RiskControlFallbackRequiresExplicitSignal(int statusCode, string content, bool expected)
    {
        Assert.Equal(expected, StreamResolver.IsDouyinRiskControlSignal(statusCode, content));
    }

    [Fact]
    public void AnonymousDouyinIdentityContainsOnlyTtwid()
    {
        string cookie = StreamResolver.GetDouyinAnonymousCookie();

        Assert.StartsWith("ttwid=", cookie, StringComparison.Ordinal);
        Assert.Single(cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.DoesNotContain("sessionid", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passport", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uid_tt", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DouyinWebViewClearsPersistedCookiesBeforeNavigation()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "DouyinWebViewResolver.cs"));
        int applyIndex = source.IndexOf("await ApplyCookiesAsync(webView.CoreWebView2.CookieManager", StringComparison.Ordinal);
        int navigationIndex = source.IndexOf("NavigateAndCaptureAsync(webView, roomUrl", StringComparison.Ordinal);

        Assert.Contains("cookieManager.DeleteAllCookies();", source, StringComparison.Ordinal);
        Assert.True(applyIndex >= 0);
        Assert.True(navigationIndex > applyIndex);
    }

    [Fact]
    public void BilibiliResolverStillReadsConfiguredCookie()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Core", "SpiderImpl", "BilibiliSpider.cs"));

        Assert.Contains("PlatformCookieStore.GetCookie(\"Bilibili\"", source, StringComparison.Ordinal);
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
