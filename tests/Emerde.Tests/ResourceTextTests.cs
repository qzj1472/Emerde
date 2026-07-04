using Emerde.Properties;
using System.Globalization;

namespace Emerde.Tests;

public sealed class ResourceTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void Title_IsEmerde(string cultureName)
    {
        CultureInfo? culture = string.IsNullOrEmpty(cultureName) ? null : new CultureInfo(cultureName);
        string? value = Resources.ResourceManager.GetString(nameof(Resources.Title), culture);

        Assert.Equal("Emerde", value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void UseCookieHint_DoesNotMentionSinglePlatform(string cultureName)
    {
        CultureInfo? culture = string.IsNullOrEmpty(cultureName) ? null : new CultureInfo(cultureName);
        string? value = Resources.ResourceManager.GetString(nameof(Resources.UseCookieHint), culture);

        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.DoesNotContain("Douyin", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TikTok", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tiktok", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("抖音", value, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void UseCookieEnterHint_DoesNotMentionBuiltInCookies(string cultureName)
    {
        CultureInfo? culture = string.IsNullOrEmpty(cultureName) ? null : new CultureInfo(cultureName);
        string? value = Resources.ResourceManager.GetString(nameof(Resources.UseCookieEnterHint), culture);

        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.DoesNotContain("built-in cookies", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("内置 Cookie", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("內置 Cookie", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("内蔵 Cookie", value, StringComparison.OrdinalIgnoreCase);
    }
}
