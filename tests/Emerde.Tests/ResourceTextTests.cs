using Emerde.Properties;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Emerde.Tests;

public sealed class ResourceTextTests
{
    [Fact]
    public void Translation_FallsBackToEmbeddedResourceForNewKeys()
    {
        CultureInfo previousCulture = Locale.Culture;
        try
        {
            Locale.Culture = CultureInfo.GetCultureInfo("zh-Hans");

            Assert.Equal("检测失败", "StreamStatusOfCheckFailed".Tr());
        }
        finally
        {
            Locale.Culture = previousCulture;
        }
    }

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
            "TargetFormat", "CreateOptimizedAudioTrack", "OptimizedAudioTrackDescription",
            "SplitVideo", "SplitInterval", "Minutes", "Seconds", "Hours", "StartButton",
            "VideoAllStreamers", "CommonUnknown", "TimeRangeAll", "TimeRangeLast24Hours",
            "TimeRangeLastWeek", "TimeRangeLastMonth", "TimeRangeLastThreeMonths", "TimeRangeLastYear",
            "SortDescending", "SortAscending", "VideoSelectedCount", "OpenVideoFailed", "TranscodingVideo", "TranscodingChip",
            "TranscodeComplete", "TranscodeFailed", "SplitDurationInvalid", "SplittingVideo", "SplitComplete",
            "SplitFailed", "SelectAtLeastTwoVideos", "MergeFormatsMustMatch", "MergingVideos", "MergeComplete",
            "MergeFailed", "ConfirmDeleteVideos", "MovingVideos", "CopyingVideos", "StreamerChip",
            "ResolutionChip", "BitrateChip", "QualityLabel", "QualitySelectionHint",
            "PreviewPause", "PreviewMute", "PreviewUnmute", "PreviewFullScreen", "PreviewRestore",
        ];

        foreach (string key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(Resources.ResourceManager.GetString(key, culture)), key);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void AutoShutdownUiKeys_ArePresent(string cultureName)
    {
        CultureInfo? culture = string.IsNullOrEmpty(cultureName) ? null : new CultureInfo(cultureName);
        string[] keys =
        [
            "AutoShutdownAfterTranscode", "AutoShutdownComputer", "AutoShutdownCancel", "AutoShutdownNow",
            "AutoShutdownAfterTranscodeNow", "ButtonOfAcknowledge", "AutoShutdownComputerDescription",
            "AutoShutdownApplicationDescription", "AutoShutdownComputerFailed",
        ];

        foreach (string key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(Resources.ResourceManager.GetString(key, culture)), key);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void StartupNoticeUiKeys_ArePresent(string cultureName)
    {
        CultureInfo? culture = string.IsNullOrEmpty(cultureName) ? null : new CultureInfo(cultureName);
        string[] keys =
        [
            "StartupAboutNoticeTitle",
            "StartupAboutNoticeDescription",
            "ButtonOfAcknowledge",
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
            "StreamerLabel", "TimeRangeLabel", "OpenVideo", "TranscodeVideo", "TranscodingChip", "SplitButton",
            "SplitVideo", "SplitInterval", "Minutes", "Seconds", "Hours", "StartButton",
            "QualityLabel", "QualitySelectionHint", "PreviewPause", "PreviewMute", "PreviewUnmute",
            "PreviewFullScreen", "PreviewRestore", "AutoShutdownAfterTranscode", "AutoShutdownComputer",
            "AutoShutdownCancel", "AutoShutdownNow", "AutoShutdownAfterTranscodeNow", "ButtonOfAcknowledge",
            "AutoShutdownComputerDescription", "AutoShutdownApplicationDescription", "AutoShutdownComputerFailed",
            "StartupAboutNoticeTitle", "StartupAboutNoticeDescription",
        ];

        foreach (string key in keys)
        {
            Assert.NotNull(typeof(Resources).GetProperty(key));
        }
    }

    [Fact]
    public void XamlI18nKeys_HaveStronglyTypedResourceProperties()
    {
        string sourceDirectory = FindRepositoryDirectory("src", "Emerde");
        string[] missingKeys = Directory
            .EnumerateFiles(sourceDirectory, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => Regex
                .Matches(File.ReadAllText(path), @"\{I18N\s+([A-Za-z_][A-Za-z0-9_]*)")
                .Cast<Match>()
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .Where(key => typeof(Resources).GetProperty(key) == null)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingKeys);
    }

    private static string FindRepositoryDirectory(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
