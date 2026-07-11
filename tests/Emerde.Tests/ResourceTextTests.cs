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
    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void PlatformUiKeys_ArePresent(string cultureName)
    {
        CultureInfo? culture = string.IsNullOrEmpty(cultureName) ? null : new CultureInfo(cultureName);
        string[] keys =
        [
            "Platform",
            "Supported",
            "PlatformAccess",
            "NoCookie",
            "ProxyAppliesToPlatformRequests",
        ];

        foreach (string key in keys)
        {
            string? value = Resources.ResourceManager.GetString(key, culture);

            Assert.False(string.IsNullOrWhiteSpace(value));
        }

        CultureInfo? previousCulture = Resources.Culture;

        try
        {
            Resources.Culture = culture;

            Assert.False(string.IsNullOrWhiteSpace(Resources.Platform));
            Assert.False(string.IsNullOrWhiteSpace(Resources.Supported));
            Assert.False(string.IsNullOrWhiteSpace(Resources.PlatformAccess));
            Assert.False(string.IsNullOrWhiteSpace(Resources.NoCookie));
            Assert.False(string.IsNullOrWhiteSpace(Resources.ProxyAppliesToPlatformRequests));
        }
        finally
        {
            Resources.Culture = previousCulture;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void VideoLibraryUiKeys_ArePresent(string cultureName)
    {
        CultureInfo? culture = string.IsNullOrEmpty(cultureName) ? null : new CultureInfo(cultureName);
        string[] keys =
        [
            "VideoListTitle", "RefreshButton", "ImportFolder", "MergeVideos", "DeleteButton",
            "MoveButton", "CopyButton", "SelectAll", "InvertSelection", "MultiSelect",
            "StreamerLabel", "TimeRangeLabel", "OpenVideo", "TranscodeVideo", "SplitButton",
            "SplitVideo", "SplitInterval", "Minutes", "Seconds", "Hours", "StartButton",
            "VideoAllStreamers", "CommonUnknown", "TimeRangeAll", "TimeRangeLast24Hours",
            "TimeRangeLastWeek", "TimeRangeLastMonth", "TimeRangeLastThreeMonths", "TimeRangeLastYear",
            "SortDescending", "SortAscending", "VideoSelectedCount", "OpenVideoFailed", "TranscodingVideo",
            "TranscodeComplete", "TranscodeFailed", "SplitDurationInvalid", "SplittingVideo", "SplitComplete",
            "SplitFailed", "SelectAtLeastTwoVideos", "MergeFormatsMustMatch", "MergingVideos", "MergeComplete",
            "MergeFailed", "ConfirmDeleteVideos", "MovingVideos", "CopyingVideos", "StreamerChip",
            "ResolutionChip", "BitrateChip", "QualityLabel", "QualitySelectionHint",
        ];

        foreach (string key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(Resources.ResourceManager.GetString(key, culture)), key);
        }
    }

    [Fact]
    public void XamlResourceProperties_ArePresent()
    {
        string[] keys =
        [
            "VideoListTitle", "RefreshButton", "ImportFolder", "MergeVideos", "DeleteButton",
            "MoveButton", "CopyButton", "SelectAll", "InvertSelection", "MultiSelect",
            "StreamerLabel", "TimeRangeLabel", "OpenVideo", "TranscodeVideo", "SplitButton",
            "SplitVideo", "SplitInterval", "Minutes", "Seconds", "Hours", "StartButton",
            "QualityLabel", "QualitySelectionHint",
        ];

        foreach (string key in keys)
        {
            Assert.NotNull(typeof(Resources).GetProperty(key));
        }
    }
}
