using Emerde.Properties;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class ResourceTextTests
{
    [Fact]
    public void LocalizedResourceFiles_HaveIdenticalNonEmptyKeySets()
    {
        string resourceDirectory = FindRepositoryDirectory("src", "Emerde", "Properties");
        string[] fileNames =
        [
            "Resources.resx",
            "Resources.zh-Hans.resx",
            "Resources.zh-Hant.resx",
            "Resources.ja.resx",
        ];
        Dictionary<string, HashSet<string>> resourceKeys = fileNames.ToDictionary(
            fileName => fileName,
            fileName => XDocument.Load(Path.Combine(resourceDirectory, fileName))
                .Descendants("data")
                .Where(element => !string.IsNullOrWhiteSpace(element.Element("value")?.Value))
                .Select(element => (string?)element.Attribute("name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        HashSet<string> expected = resourceKeys["Resources.resx"];

        foreach ((string fileName, HashSet<string> keys) in resourceKeys)
        {
            Assert.Empty(expected.Except(keys));
            Assert.Empty(keys.Except(expected));
            Assert.Equal(keys.Count, XDocument.Load(Path.Combine(resourceDirectory, fileName)).Descendants("data").Count());
        }
    }

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
            "UpdateReleaseNotesDialogTitle",
            "UpdateReleaseNotesCurrentVersionFormat",
            "UpdateReleaseNotesUpgradeFromFormat",
            "UpdateReleaseNotesUpgradeCurrentFormat",
            "UpdateReleaseNotesHistoryLabel",
            "ReleaseNotes1670Title",
            "ReleaseNotes1670Date",
            "ReleaseNotes1670Items",
            "ReleaseNotesUnknownTitle",
            "ReleaseNotesUnknownItem",
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
    public void SharedStatusAndUnitKeys_ArePresent(string cultureName)
    {
        CultureInfo? culture = string.IsNullOrEmpty(cultureName) ? null : new CultureInfo(cultureName);
        string[] keys =
        [
            "Unsupported", "SkipValidation", "FollowGlobalSettings", "VolumeFormat",
            "PreviewVolumeToolTipFormat", "ExitFullScreenHint", "SelectedRoomsFormat", "AllPlatforms",
            "RecordingEngineActive", "RecordingEngineStarting", "WaitingForData", "LiveStream",
            "AutoShutdownComputerCountdown", "AutoShutdownApplicationCountdown", "Milliseconds",
            "Days", "Weeks", "Months", "Years",
            "HomePage", "Extensions", "Monitor", "NavigationHomeToolTip", "NavigationVideosToolTip",
            "NavigationSettingsToolTip", "NavigationExtensionsToolTip", "NavigationAboutToolTip",
            "ExitApplicationToolTip", "AddRoomToolTip", "ToggleAllMonitorToolTip", "ToggleAllRecordingToolTip",
            "CardSize", "SizeLarge", "SizeMedium", "SizeSmall", "Sort", "SortByName", "SortByAddedOrder",
            "PlatformFilter", "LoadPlatforms", "RoomInformation", "LiveTitle", "ResolutionLabel", "BitrateLabel",
            "RoomAddress", "RefreshCurrentRoomToolTip", "CopyRoomAddressToolTip", "CopyLiveStreamToolTip",
            "PreviewCurrentRoomToolTip", "OpenCurrentRoomToolTip", "ToggleCurrentMonitorToolTip",
            "ToggleCurrentRecordingToolTip", "RemoveRoomShortcutHint", "OpenCurrentRoomSettingsToolTip",
            "UIX", "UIXHint",
        ];

        foreach (string key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(Resources.ResourceManager.GetString(key, culture)), key);
        }
    }

    [Fact]
    public void MainWindowPrimaryWorkflow_DoesNotContainHardcodedChineseLabels()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryDirectory("src", "Emerde", "Views"), "MainWindow.xaml"));
        string[] retiredLabels =
        [
            "Text=\"首页\"", "Text=\"视频列表\"", "Text=\"直播间信息\"", "Text=\"房间地址\"",
            "Text=\"直播流\"", "Text=\"操作\"", "Header=\"卡片大小\"", "Header=\"平台筛选\"",
        ];

        foreach (string label in retiredLabels)
        {
            Assert.DoesNotContain(label, source, StringComparison.Ordinal);
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
