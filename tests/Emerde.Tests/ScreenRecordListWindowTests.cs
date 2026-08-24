using Emerde.Core;
using Emerde.Plugins;
using Emerde.ViewModels;
using Emerde.Views;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Controls;
using System.Xml.Linq;

namespace Emerde.Tests;

public sealed class ScreenRecordListWindowTests
{
    [Fact]
    public void LocalizationSubscription_FollowsVideoListMonitoringLifecycle()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        string constructor = ExtractMethod(source, "public ScreenRecordListViewModel()", "internal void StartMonitoring()");
        string start = ExtractMethod(source, "internal void StartMonitoring()", "internal void StopMonitoring()");
        string stop = ExtractMethod(source, "internal void StopMonitoring()", "private void ConfigureDirectoryWatchers(");

        Assert.DoesNotContain("Locale.CultureChanged += OnCultureChanged", constructor, StringComparison.Ordinal);
        Assert.Contains("Locale.CultureChanged += OnCultureChanged", start, StringComparison.Ordinal);
        Assert.Contains("Locale.CultureChanged -= OnCultureChanged", stop, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoList_ContextMenuLoadsRegisteredExtensionActions()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));

        Assert.Contains("ContextMenuOpening=\"VideoCardContextMenuOpening\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{I18N RepairVideo}", xaml, StringComparison.Ordinal);
        Assert.Contains("GetOverrides<IExtensionVideoAction>(ExtensionContractNames.VideoListActions)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoList_RepairFallbackUsesTranscodeSuccessFeedback()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        string method = ExtractMethod(source, "private async Task TranscodeVideoAsync", "private async Task RepairVideoAsync");

        Assert.Contains("new VideoRepairService().RepairAsync", method, StringComparison.Ordinal);
        Assert.Contains("GetResourceText(\"TranscodeComplete\"", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetResourceText(\"RepairingVideo\"", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetResourceText(\"RepairVideoComplete\"", method, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, true, ".mp4", true)]
    [InlineData(0, false, ".mp4", false)]
    [InlineData(1, true, ".mkv", false)]
    public void CreateTranscodeOptions_MapsDialogSelection(int selectedIndex, bool optimizeAudio, string targetFormat, bool expectedOptimization)
    {
        ConverterOptions options = ScreenRecordListViewModel.CreateTranscodeOptions(selectedIndex, optimizeAudio);
        Assert.Equal(targetFormat, options.TargetFormat);
        Assert.Equal(expectedOptimization, options.OptimizeAudio);
    }

    [Fact]
    public void CreateTranscodeOptions_RejectsMissingSelection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenRecordListViewModel.CreateTranscodeOptions(-1, true));
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, false, true)]
    public void CreateTranscodeOptions_PreservesSourceDeletionChoice(
        int selectedIndex,
        bool expectedOptimization,
        bool removeSource)
    {
        ConverterOptions options = ScreenRecordListViewModel.CreateTranscodeOptions(
            selectedIndex,
            optimizeAudio: expectedOptimization,
            removeSource);

        Assert.True(options.RemoveSource);
    }

    [Fact]
    public void IsIdle_AllowsUserTakeoverWhileListTranscodeIsRunning()
    {
        ScreenRecordListViewModel viewModel = new()
        {
            IsOperating = true,
        };

        Assert.False(viewModel.IsIdle);

        viewModel.IsUserTranscoding = true;

        Assert.True(viewModel.IsIdle);
    }

    [Fact]
    public void EnumerateVideoFiles_StopsWhenCancellationIsRequested()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"emerde-video-enumeration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "video.ts"), [1]);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        try
        {
            Assert.Throws<OperationCanceledException>(() => ScreenRecordListViewModel.EnumerateVideoFiles(directory, cancellation.Token).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.Up, true)]
    [InlineData(System.Windows.Input.Key.Down, true)]
    [InlineData(System.Windows.Input.Key.Left, true)]
    [InlineData(System.Windows.Input.Key.Right, true)]
    [InlineData(System.Windows.Input.Key.W, true)]
    [InlineData(System.Windows.Input.Key.A, true)]
    [InlineData(System.Windows.Input.Key.S, true)]
    [InlineData(System.Windows.Input.Key.D, true)]
    [InlineData(System.Windows.Input.Key.Home, false)]
    public void VideoListKeyboardNavigation_UsesArrowAndWasdKeys(System.Windows.Input.Key key, bool expected)
    {
        Assert.Equal(expected, ScreenRecordListWindow.IsVideoListKeyboardNavigationKey(key));
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.Up, -1)]
    [InlineData(System.Windows.Input.Key.Left, -1)]
    [InlineData(System.Windows.Input.Key.W, -1)]
    [InlineData(System.Windows.Input.Key.A, -1)]
    [InlineData(System.Windows.Input.Key.Down, 1)]
    [InlineData(System.Windows.Input.Key.Right, 1)]
    [InlineData(System.Windows.Input.Key.S, 1)]
    [InlineData(System.Windows.Input.Key.D, 1)]
    public void VideoListKeyboardNavigationOffset_MapsDirections(System.Windows.Input.Key key, int expected)
    {
        Assert.Equal(expected, ScreenRecordListWindow.GetVideoListKeyboardNavigationOffset(key));
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.Up, 3, -3)]
    [InlineData(System.Windows.Input.Key.Down, 3, 3)]
    [InlineData(System.Windows.Input.Key.Left, 3, -1)]
    [InlineData(System.Windows.Input.Key.Right, 3, 1)]
    [InlineData(System.Windows.Input.Key.W, 4, -4)]
    [InlineData(System.Windows.Input.Key.S, 4, 4)]
    [InlineData(System.Windows.Input.Key.A, 4, -1)]
    [InlineData(System.Windows.Input.Key.D, 4, 1)]
    public void UiXVideoListKeyboardNavigationOffset_MovesByRowAndAdjacentCard(System.Windows.Input.Key key, int columnCount, int expected)
    {
        Assert.Equal(expected, ScreenRecordListWindow.GetVideoListKeyboardNavigationOffset(key, columnCount));
    }

    [Theory]
    [InlineData(true, false, 2, true)]
    [InlineData(false, false, 2, false)]
    [InlineData(true, true, 2, false)]
    [InlineData(true, false, 1, false)]
    public void VideoListBlankDoubleClick_RefreshesBlankArea(bool isBlank, bool isScrollBar, int clickCount, bool expected)
    {
        Assert.Equal(expected, ScreenRecordListWindow.ShouldRefreshVideoListFromDoubleClick(isBlank, isScrollBar, clickCount));
    }

    [Fact]
    public void VideoListBlankRefreshSurface_DistinguishesCardFromVisibleGap()
    {
        RunOnStaThread(() =>
        {
            Grid host = new();
            Grid itemSlot = new();
            Border card = new() { Name = "VideoCardShell" };
            Border cardContent = new();
            Border visibleGap = new();
            card.Child = cardContent;
            itemSlot.Children.Add(card);
            itemSlot.Children.Add(visibleGap);
            host.Children.Add(itemSlot);

            Assert.False(ScreenRecordListWindow.IsVideoListBlankRefreshSurface(cardContent, host));
            Assert.True(ScreenRecordListWindow.IsVideoListBlankRefreshSurface(visibleGap, host));
        });
    }

    [Fact]
    public void VideoListBlankRefreshSurface_ExcludesTextAndInteractiveControls()
    {
        RunOnStaThread(() =>
        {
            Grid host = new();
            TextBlock text = new();
            Button button = new();
            ComboBox selector = new();
            host.Children.Add(text);
            host.Children.Add(button);
            host.Children.Add(selector);

            Assert.False(ScreenRecordListWindow.IsVideoListBlankRefreshSurface(text, host));
            Assert.False(ScreenRecordListWindow.IsVideoListBlankRefreshSurface(button, host));
            Assert.False(ScreenRecordListWindow.IsVideoListBlankRefreshSurface(selector, host));
            Assert.False(ScreenRecordListWindow.IsVideoListBlankRefreshSurface(new Border(), host));
        });
    }

    [Fact]
    public void ManualVideoRefresh_ReportsResultAndFlashesUiXCards()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("ManualRefreshCompleted?.Invoke(this, new VideoListRefreshCompletedEventArgs(true))", source, StringComparison.Ordinal);
        Assert.Contains("ManualRefreshCompleted?.Invoke(this, new VideoListRefreshCompletedEventArgs(false))", source, StringComparison.Ordinal);
        Assert.Contains("AppFeedback.Success(\"VideoListRefreshComplete\".Tr(), key: \"video-list-refresh\")", source, StringComparison.Ordinal);
        Assert.Contains("AppFeedback.Warning(\"VideoListRefreshFailed\".Tr(), key: \"video-list-refresh\")", source, StringComparison.Ordinal);
        Assert.Contains("AppFeedback.Warning(\"VideoListRefreshInProgress\".Tr(), key: \"video-list-refresh\")", source, StringComparison.Ordinal);
        Assert.Contains("\"VideoListRefreshTooFrequently\".Tr(MainViewModel.GetRefreshRemainingSeconds(remainingMilliseconds))", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VideoCardRefreshLayer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVideoRefreshFlashActive", xaml, StringComparison.Ordinal);

        foreach (string resourceName in new[] { "Resources.resx", "Resources.zh-Hans.resx", "Resources.zh-Hant.resx", "Resources.ja.resx" })
        {
            string resources = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Properties", resourceName));
            Assert.Contains("name=\"VideoListRefreshComplete\"", resources, StringComparison.Ordinal);
            Assert.Contains("name=\"VideoListRefreshFailed\"", resources, StringComparison.Ordinal);
            Assert.Contains("name=\"VideoListRefreshTooFrequently\"", resources, StringComparison.Ordinal);
            Assert.Contains("name=\"VideoListRefreshInProgress\"", resources, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AdjacentVisibleVideo_MovesWithinFilteredOrderAndStopsAtBounds()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        videos.Add(first);
        videos.Add(second);

        Assert.Same(first, viewModel.GetAdjacentVisibleVideo(1));
        Assert.Same(second, viewModel.GetAdjacentVisibleVideo(-1));

        viewModel.SelectRegularItem(first);
        Assert.Same(second, viewModel.GetAdjacentVisibleVideo(1));
        Assert.Same(first, viewModel.GetAdjacentVisibleVideo(-1));

        viewModel.SelectRegularItem(second);
        Assert.Same(second, viewModel.GetAdjacentVisibleVideo(1));
    }

    [Fact]
    public void AdjacentVisibleVideo_PreservesRowOffsetAndStopsAtBounds()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem[] items = Enumerable.Range(0, 7)
            .Select(index => new RecordedVideoItem { FullPath = $@"C:\videos\{index}.ts" })
            .ToArray();
        foreach (RecordedVideoItem item in items)
        {
            videos.Add(item);
        }

        viewModel.SelectRegularItem(items[1]);
        Assert.Same(items[4], viewModel.GetAdjacentVisibleVideo(3));
        Assert.Same(items[0], viewModel.GetAdjacentVisibleVideo(-3));

        viewModel.SelectRegularItem(items[5]);
        Assert.Same(items[6], viewModel.GetAdjacentVisibleVideo(3));
    }

    [Fact]
    public void AdjacentVisibleVideo_SkipsRecordingFiles()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem recording = new() { FullPath = @"C:\videos\recording.ts", IsRecordingFile = true };
        RecordedVideoItem completed = new() { FullPath = @"C:\videos\completed.ts" };
        videos.Add(recording);
        videos.Add(completed);

        Assert.Same(completed, viewModel.GetAdjacentVisibleVideo(1));

        viewModel.SelectRegularItem(recording);

        Assert.Null(viewModel.RegularSelectedItem);
    }

    [Fact]
    public void ReuseExistingVideoItems_PreservesUnchangedObjectsAndAddsNewFiles()
    {
        DateTime lastWrite = new(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
        RecordedVideoItem existing = new() { FullPath = @"C:\videos\first.ts", IsEnriched = true, SourceLength = 100, SourceLastWriteTimeUtc = lastWrite };
        RecordedVideoItem reloaded = new() { FullPath = @"C:\videos\first.ts", SourceLength = 100, SourceLastWriteTimeUtc = lastWrite };
        RecordedVideoItem added = new() { FullPath = @"C:\videos\second.ts" };

        RecordedVideoItem[] result = ScreenRecordListViewModel.ReuseExistingVideoItems([existing], [reloaded, added]);

        Assert.Same(existing, result[0]);
        Assert.Same(added, result[1]);
        Assert.True(result[0].IsEnriched);
    }

    [Fact]
    public void ReuseExistingVideoItems_ReplacesFilesWhoseVersionChanged()
    {
        RecordedVideoItem existing = new() { FullPath = @"C:\videos\first.ts", SourceLength = 100 };
        RecordedVideoItem changed = new() { FullPath = @"C:\videos\first.ts", SourceLength = 200 };

        RecordedVideoItem[] result = ScreenRecordListViewModel.ReuseExistingVideoItems([existing], [changed]);

        Assert.Same(changed, result[0]);
    }

    [Theory]
    [InlineData(false, 0, 1000, (int)VideoListRefreshStartResult.Started, 0)]
    [InlineData(false, 1000, 2999, (int)VideoListRefreshStartResult.Cooldown, 1)]
    [InlineData(false, 1000, 3000, (int)VideoListRefreshStartResult.Started, 0)]
    [InlineData(true, 0, 1000, (int)VideoListRefreshStartResult.InProgress, 0)]
    public void ManualVideoRefresh_DistinguishesCooldownAndOverlappingRefreshes(
        bool isRefreshRunning,
        long lastRefreshTimestamp,
        long currentTimestamp,
        int expected,
        long expectedRemainingMilliseconds)
    {
        Assert.Equal((VideoListRefreshStartResult)expected, ScreenRecordListViewModel.GetManualRefreshStartResult(
            isRefreshRunning,
            lastRefreshTimestamp,
            currentTimestamp,
            ScreenRecordListViewModel.ManualRefreshCooldownMilliseconds,
            out long remainingMilliseconds));
        Assert.Equal(expectedRemainingMilliseconds, remainingMilliseconds);
        Assert.Equal(MainViewModel.PreviewRefreshCooldownMilliseconds, ScreenRecordListViewModel.ManualRefreshCooldownMilliseconds);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1000, 1)]
    [InlineData(1001, 2)]
    [InlineData(2000, 2)]
    public void ManualVideoRefresh_RoundsRemainingTimeUp(long remainingMilliseconds, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, MainViewModel.GetRefreshRemainingSeconds(remainingMilliseconds));
    }

    [Fact]
    public void ManualVideoRefresh_RescansWithoutReplacingUnchangedCards()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));

        Assert.Contains("if (!forceRefresh && !rootsChanged", source, StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, RecordedVideoItem> existingByPath = videos", source, StringComparison.Ordinal);
        Assert.Contains("items = ReuseExistingVideoItems(videos, items);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("reuseExistingItems: !forceRefresh", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\videos\record.ts", WatcherChangeTypes.Deleted, false, true)]
    [InlineData(@"C:\videos\record.ts", WatcherChangeTypes.Created, true, false)]
    [InlineData(@"C:\videos\record.ts", WatcherChangeTypes.Changed, false, false)]
    [InlineData(@"C:\videos\record.txt", WatcherChangeTypes.Created, false, false)]
    public void DirectoryRefresh_SkipsProtectedMediaChanges(
        string path,
        WatcherChangeTypes changeType,
        bool isProtected,
        bool expected)
    {
        Assert.Equal(expected, ScreenRecordListViewModel.ShouldQueueDirectoryRefresh(path, changeType, isProtected));
    }

    [Fact]
    public void ReconcileVideoItems_PreservesExistingReferencesWithoutClearingCollection()
    {
        ObservableCollection<RecordedVideoItem> current =
        [
            new RecordedVideoItem { FullPath = @"C:\videos\first.ts" },
            new RecordedVideoItem { FullPath = @"C:\videos\second.ts" },
        ];
        RecordedVideoItem first = current[0];
        RecordedVideoItem second = current[1];
        RecordedVideoItem third = new() { FullPath = @"C:\videos\third.ts" };

        ScreenRecordListViewModel.ReconcileVideoItems(current, [second, third, first]);

        Assert.Equal([second, third, first], current);
    }

    [Fact]
    public void ReconcileVideoItems_ReplacesTheOnlyItemWithoutEmptyingTheCollection()
    {
        ObservableCollection<RecordedVideoItem> current =
        [
            new RecordedVideoItem { FullPath = @"C:\videos\source.ts" },
        ];
        RecordedVideoItem converted = new() { FullPath = @"C:\videos\source.mp4" };
        List<int> observedCounts = [];
        List<System.Collections.Specialized.NotifyCollectionChangedAction> observedActions = [];
        current.CollectionChanged += (_, _) => observedCounts.Add(current.Count);
        current.CollectionChanged += (_, e) => observedActions.Add(e.Action);

        ScreenRecordListViewModel.ReconcileVideoItems(current, [converted]);

        Assert.Equal([converted], current);
        Assert.DoesNotContain(0, observedCounts);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Replace], observedActions);
    }

    [Fact]
    public void InternalVideoOperations_UseIncrementalRefresh()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));

        Assert.Contains("await RefreshForDisplayAsync();", source);
        Assert.DoesNotContain("await RefreshAsync();", source);
        Assert.Contains("BeginAutomaticRefreshSuppression();", source);
        Assert.Contains("EndAutomaticRefreshSuppression();", source);
        Assert.DoesNotContain("RefreshAfterOperationAsync", source);
    }

    [Fact]
    public void TryGetExistingFile_ReturnsFalseAfterFileIsRemoved()
    {
        string path = Path.Combine(Path.GetTempPath(), $"emerde-list-file-{Guid.NewGuid():N}.ts");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            Assert.True(ScreenRecordListViewModel.TryGetExistingFile(path, out _, out long length));
            Assert.Equal(3, length);

            File.Delete(path);

            Assert.False(ScreenRecordListViewModel.TryGetExistingFile(path, out _, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VideoListXaml_DoesNotUseSpacingStackPanelInConstrainedLayout()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.DoesNotContain("<ui:StackPanel", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListXaml_HandlesMouseWheelScrolling()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("PreviewMouseWheel=\"VideoListBoxPreviewMouseWheel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.ScrollUnit=\"Pixel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.IsDeferredScrollingEnabled=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetName=\"ThumbSurface\" Property=\"Width\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListXaml_GroupsUiXVideosByDateWithoutAGroupContainer()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        string updateMetricsMethod = ExtractMethod(code, "private void UpdateVideoCardMetrics", "internal static int CalculateVideoCardColumns");
        string contentWidthMethod = ExtractMethod(code, "private double GetVideoCardContentWidth", "private void UpdateVideoCardMetrics");
        XDocument document = XDocument.Parse(xaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement list = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "VideoListBox");
        XElement listStyle = list.Elements().Single(element => element.Name.LocalName == "ListBox.Style").Elements().Single();
        XElement legacyPanel = listStyle.Elements()
            .Single(element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "ItemsPanel")
            .Descendants()
            .Single(element => element.Name.LocalName == "ItemsPanelTemplate");
        XElement uiXPanel = listStyle.Descendants()
            .Where(element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "ItemsPanel")
            .Skip(1)
            .Single()
            .Descendants()
            .Single(element => element.Name.LocalName == "ItemsPanelTemplate");
        XElement groupPanel = document.Descendants()
            .Single(element => element.Name.LocalName == "ItemsPanelTemplate"
                && (string?)element.Attribute(x + "Key") == "UiXVideoGroupPanelTemplate");
        Assert.Contains("Width=\"{Binding VideoCardGridWidth", xaml, StringComparison.Ordinal);
        Assert.Contains("VideoDateGroupHeaderTemplate", xaml, StringComparison.Ordinal);
        Assert.Contains("UiXVideoGroupPanelTemplate", xaml, StringComparison.Ordinal);
        Assert.Contains("UiXVideoGroupTemplate", xaml, StringComparison.Ordinal);
        Assert.Contains(legacyPanel.Descendants(), element => element.Name.LocalName == "VirtualizingStackPanel");
        Assert.Contains(uiXPanel.Descendants(), element => element.Name.LocalName == "VirtualizingWrapPanel"
            && ((string?)element.Attribute("Width"))?.StartsWith("{Binding VideoCardGridWidth", StringComparison.Ordinal) == true
            && ((string?)element.Attribute("ItemSize"))?.StartsWith("{Binding VideoCardItemSize", StringComparison.Ordinal) == true
            && (string?)element.Attribute("HorizontalAlignment") == "Center"
            && (string?)element.Attribute("StretchItems") == "False");
        Assert.Contains(groupPanel.Descendants(), element => element.Name.LocalName == "VirtualizingStackPanel"
            && (string?)element.Attribute("Orientation") == "Vertical");
        Assert.Contains("VirtualizingPanel.IsVirtualizingWhenGrouping=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding VideoCardWidth", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding VideoCardMargin", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VideoCardUiXScaleView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"354\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"91\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height\" Value=\"{Binding VideoCardHeight", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FormatText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsNonTargetTranscodableFormat}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsNonTargetCompletedFormat}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NonStandardFormatText", xaml, StringComparison.Ordinal);
        Assert.Contains("UpdateVideoCardMetrics", code, StringComparison.Ordinal);
        Assert.Contains("CalculateVideoCardColumns", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardBaseWidth = 378d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardMinimumWidth = 227d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardMaximumWidth = 432d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardInformationWidthRatio = 1.5d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardMinimumInformationWidthRatio = 1.5d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardCoverAspectWidth = 3d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardCoverAspectHeight = 2d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardFileNameFontSize = 13d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardSecondaryFontSize = 12d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardDetailFontSize = 11d", code, StringComparison.Ordinal);
        Assert.DoesNotContain("UiXVideoCardMinimumTextScale", code, StringComparison.Ordinal);
        Assert.DoesNotContain("textScale", code, StringComparison.Ordinal);
        Assert.DoesNotContain("UiXVideoCardStatusBottomInset", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardHorizontalGap = 12d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardVerticalGap = 12d", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardColumnHysteresis = 16d", code, StringComparison.Ordinal);
        Assert.Contains("VideoCardItemSize = new Size(slotWidth, cardHeight + UiXVideoCardVerticalGap)", code, StringComparison.Ordinal);
        Assert.Contains("UiXVideoCardBaseHeight * scale", code, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoListScrollContentPresenterSizeChanged", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged=\"VideoListScrollContentPresenterSizeChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight\" Value=\"{Binding VideoCardHeight", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculateVideoCardCoverWidth(", updateMetricsMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculateVideoCardCoverHeight(", updateMetricsMethod, StringComparison.Ordinal);
        Assert.Contains("gridWidth = WindowSizing.RoundLayoutValue(columns * slotWidth)", code, StringComparison.Ordinal);
        Assert.Contains("VideoListScrollContentPresenter", xaml, StringComparison.Ordinal);
        Assert.Contains("GetVideoCardContentWidth", code, StringComparison.Ordinal);
        Assert.Contains("VideoSelectionHost.ActualWidth", contentWidthMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("scrollViewer.ViewportWidth", contentWidthMethod, StringComparison.Ordinal);
        Assert.Contains("CalculateVideoCardLayout", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow.GetCardWidthRange", code, StringComparison.Ordinal);
        Assert.Contains("cardWidth = Math.Clamp(naturalCardWidth, minimumCardWidth, maximumCardWidth)", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Render", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculateVideoCardWidth", code, StringComparison.Ordinal);
        Assert.DoesNotContain("hasPartialRow", code, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding UiXStreamerText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RecordingTimeText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FileSizeText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ImageSource=\"{Binding ThumbnailSource}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageSource=\"{Binding ThumbnailPath, Converter=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding UiXSummaryText}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ResolutionChipText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"32\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DisplayFileName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding UiXWrappedFileName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UiXWrappedFileName => AddFileNameBreakOpportunities(DisplayFileName)", code, StringComparison.Ordinal);
        Assert.Contains("result.Append('\\u200B')", code, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"13\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"11\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LineHeight=\"16\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LineHeight=\"15\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LineHeight=\"14\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LineStackingStrategy=\"BlockLineHeight\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding VideoCardPadding", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"12,0,0,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VideoCardStatusRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"17\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoCardStatusMargin", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding FileName}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HoverMarquee", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VideoCardStatusRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UpdateVideoListGrouping", code, StringComparison.Ordinal);
        Assert.Contains("PropertyGroupDescription(nameof(RecordedVideoItem.DateGroupKey))", code, StringComparison.Ordinal);
        Assert.Contains("VideoDateGroupLabelConverter", code, StringComparison.Ordinal);
        Assert.Contains("DateGroupKey => CreatedAt.Date", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemSize=\"224,224\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListResize_AppliesCardLayoutInTheCurrentFrameAndDefersBackgroundWork()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        int resizeHandlerStart = code.IndexOf("private void VideoSelectionHostSizeChanged", StringComparison.Ordinal);
        int settleHandlerStart = code.IndexOf("private void VideoResizeSettleTimerTick", StringComparison.Ordinal);
        int settleHandlerEnd = code.IndexOf("public Thickness VideoCardPadding", settleHandlerStart, StringComparison.Ordinal);

        Assert.Contains("SizeChanged=\"VideoSelectionHostSizeChanged\"", xaml, StringComparison.Ordinal);
        Assert.True(resizeHandlerStart >= 0);
        Assert.True(settleHandlerStart > resizeHandlerStart);
        Assert.True(settleHandlerEnd > settleHandlerStart);
        Assert.Contains(
            "UpdateVideoCardMetrics(Math.Max(1d, e.NewSize.Width - UiXVideoListFallbackContentInset));",
            code[resizeHandlerStart..settleHandlerStart],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "QueueVideoCardMetricsRefresh(Math.Max(1d, e.NewSize.Width - UiXVideoListFallbackContentInset));",
            code[resizeHandlerStart..settleHandlerStart],
            StringComparison.Ordinal);
        Assert.Contains("pendingVideoCardContentWidth", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Render", code, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "QueueVideoCardMetricsRefresh();",
            code[settleHandlerStart..settleHandlerEnd],
            StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListEnrichment_UsesBoundedWorkersAndBackgroundThumbnailSources()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));

        Assert.Contains("MaximumVideoEnrichmentWorkers = 2", code, StringComparison.Ordinal);
        Assert.Contains("Interlocked.CompareExchange(ref videoEnrichmentWorkerCount", code, StringComparison.Ordinal);
        Assert.Contains("ThumbnailImageConverter.TryLoadImage(thumbnailPath)", code, StringComparison.Ordinal);
        Assert.Contains("item.ThumbnailSource = thumbnailSource", code, StringComparison.Ordinal);
        Assert.Contains("MaximumCachedImages = 256", code, StringComparison.Ordinal);
        Assert.Contains("ImageSource=\"{Binding ThumbnailSource}\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(779d, 1)]
    [InlineData(780d, 2)]
    [InlineData(1169d, 2)]
    [InlineData(1170d, 3)]
    [InlineData(1559d, 3)]
    [InlineData(1560d, 4)]
    public void VideoCardColumns_ChangeMonotonicallyAtTheExactSlotBoundary(double availableWidth, int expectedColumns)
    {
        Assert.Equal(expectedColumns, ScreenRecordListWindow.CalculateVideoCardColumns(availableWidth, 378d, 12d));
    }

    [Fact]
    public void VideoCardLayout_DoesNotResizeCardsWhenAColumnIsAdded()
    {
        (int beforeColumns, double beforeCardWidth, _) = ScreenRecordListWindow.CalculateVideoCardLayout(1560d, 378d, 378d, 432d, 12d);
        (int afterColumns, double afterCardWidth, _) = ScreenRecordListWindow.CalculateVideoCardLayout(1561d, 378d, 378d, 432d, 12d);

        Assert.Equal(4, beforeColumns);
        Assert.Equal(4, afterColumns);
        Assert.InRange(Math.Abs(afterCardWidth - beforeCardWidth), 0d, 1d);
        Assert.Equal(378d, beforeCardWidth);
        Assert.Equal(378d, afterCardWidth);
    }

    [Theory]
    [InlineData(265d, 8d, 8d, 96d, 64d, 59d, 80d)]
    [InlineData(378d, 12d, 12d, 136d, 91d, 83d, 115d)]
    [InlineData(432d, 14d, 14d, 156d, 104d, 95d, 132d)]
    public void VideoCardGeometry_PreservesCoverAndInformationRatios(
        double cardWidth,
        double padding,
        double informationGap,
        double expectedCoverWidth,
        double expectedCoverHeight,
        double informationHeight,
        double expectedCardHeight)
    {
        double coverWidth = ScreenRecordListWindow.CalculateVideoCardCoverWidth(cardWidth, padding, informationGap, 1.5d, 1.5d);
        double coverHeight = ScreenRecordListWindow.CalculateVideoCardCoverHeight(coverWidth, 3d, 2d);
        double informationWidth = cardWidth - padding * 2d - informationGap - coverWidth;

        Assert.Equal(expectedCoverWidth, coverWidth);
        Assert.Equal(expectedCoverHeight, coverHeight);
        Assert.Equal(expectedCardHeight, ScreenRecordListWindow.CalculateVideoCardHeight(padding, coverHeight, informationHeight));
        Assert.InRange(coverWidth / coverHeight, 1.49d, 1.51d);
        Assert.True(informationWidth / coverWidth >= 1.5d);
    }

    [Theory]
    [InlineData(2, 3, 990d, 2)]
    [InlineData(2, 3, 991d, 3)]
    [InlineData(3, 2, 960d, 3)]
    [InlineData(3, 2, 959d, 2)]
    [InlineData(4, 4, 955d, 3)]
    public void VideoCardColumns_UseDirectionalHysteresisAtLayoutBoundaries(
        int currentColumns,
        int candidateColumns,
        double availableWidth,
        int expectedColumns)
    {
        Assert.Equal(expectedColumns, ScreenRecordListWindow.StabilizeVideoCardColumns(
            currentColumns,
            candidateColumns,
            availableWidth,
            390d,
            239d,
            16d));
    }

    [Fact]
    public void VideoCardLayout_UsesAllAvailableWidthWithinTheCurrentColumn()
    {
        (int columns, double cardWidth, double slotWidth) = ScreenRecordListWindow.CalculateVideoCardLayout(1560d, 378d, 227d, 432d, 12d);

        Assert.Equal(4, columns);
        Assert.Equal(378d, cardWidth);
        Assert.Equal(390d, slotWidth);
        Assert.Equal(1560d, slotWidth * columns);
    }

    [Fact]
    public void VideoCardLayout_ProtectsVirtualizedSlotFromNegativeContentSize()
    {
        (int columns, double cardWidth, double slotWidth) = ScreenRecordListWindow.CalculateVideoCardLayout(1d, 378d, 227d, 432d, 12d);

        Assert.Equal(1, columns);
        Assert.True(slotWidth > 12d);
        Assert.True(cardWidth > 0d);
        Assert.True(cardWidth + 12d <= slotWidth);
    }

    [Fact]
    public void VideoDateGrouping_DoesNotDeferCollectionMutationAndBatchesEnrichmentRefreshes()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        string normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("        ReconcileVideoItems(videos, items);\n\n        UpdateStreamerOptions();", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref videoEnrichmentWorkerCount) != 0", source, StringComparison.Ordinal);
        Assert.Contains("TryScheduleVideoViewRefresh();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using (Videos.DeferRefresh())\n            {\n                Videos.Refresh();\n            }", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[NotifyPropertyChangedFor(nameof(DateGroupKey))]", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(900d)]
    [InlineData(975d)]
    [InlineData(1110d)]
    [InlineData(1218d)]
    [InlineData(1560d)]
    public void VideoCardLayout_NeverPlacesTheLastColumnOutsideTheContentTrack(double availableWidth)
    {
        (int columns, double cardWidth, double slotWidth) = ScreenRecordListWindow.CalculateVideoCardLayout(availableWidth, 378d, 227d, 432d, 12d);

        Assert.True(columns * slotWidth <= availableWidth + 0.5d);
        Assert.True(cardWidth + 12d <= slotWidth + 0.5d);
    }

    [Theory]
    [InlineData(-20d, 400d, -20d)]
    [InlineData(0d, 400d, -20d)]
    [InlineData(22d, 400d, -12d)]
    [InlineData(44d, 400d, 0d)]
    [InlineData(200d, 400d, 0d)]
    [InlineData(356d, 400d, 0d)]
    [InlineData(378d, 400d, 12d)]
    [InlineData(400d, 400d, 20d)]
    [InlineData(420d, 400d, 20d)]
    public void MarqueeAutoScroll_UsesBoundedAccelerationAtViewportEdges(double pointerY, double viewportHeight, double expectedDelta)
    {
        Assert.Equal(expectedDelta, Emerde.Controls.MarqueeAutoScroll.GetDelta(pointerY, viewportHeight), 6);
    }

    [Fact]
    public void VideoAndHomeMarquee_KeepEdgeAutoScrollAndAccumulatedSelection()
    {
        string videoCode = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        string homeCode = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("VideoMarqueeAutoScrollTimerTick", videoCode, StringComparison.Ordinal);
        Assert.Contains("AccumulateVideoMarqueeItems", videoCode, StringComparison.Ordinal);
        Assert.Contains("RoomCardMarqueeAutoScrollTimerTick", homeCode, StringComparison.Ordinal);
        Assert.Contains("AccumulateRoomCardMarqueeItems", homeCode, StringComparison.Ordinal);
        Assert.Contains("GetVideoMarqueeContentPoint", videoCode, StringComparison.Ordinal);
        Assert.Contains("ProjectVideoMarqueeToViewport", videoCode, StringComparison.Ordinal);
        Assert.Contains("GetRoomCardMarqueeContentPoint", homeCode, StringComparison.Ordinal);
        Assert.Contains("ProjectRoomCardMarqueeToViewport", homeCode, StringComparison.Ordinal);
        Assert.Contains("!isRoomCardDragging && !isRoomCardMarqueeSelecting", homeCode, StringComparison.Ordinal);
        Assert.Contains("roomCardMarqueeCreatedMultiSelectMode", homeCode, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeMarqueeSelection_RebuildsItemsForCurrentRectangle()
    {
        string source = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "MainWindow.xaml.cs"));
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        string method = source[(source.IndexOf("private void AccumulateRoomCardMarqueeItems", StringComparison.Ordinal))..];
        method = method[..method.IndexOf("private void UpdateRoomCardMarqueeAutoScroll", StringComparison.Ordinal)];

        Assert.StartsWith(
            "private void AccumulateRoomCardMarqueeItems(Rect selection)\n    {\n        roomCardMarqueeItems.Clear();",
            method,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(390d)]
    [InlineData(378d)]
    [InlineData(450d)]
    [InlineData(1560d)]
    [InlineData(2100d)]
    public void VideoCardLayout_StaysWithinItsSingleElasticRange(double availableWidth)
    {
        (_, double cardWidth, _) = ScreenRecordListWindow.CalculateVideoCardLayout(availableWidth, 378d, 227d, 432d, 12d);

        Assert.InRange(cardWidth, 227d, 432d);
    }

    [Fact]
    public void TimeRangeFilter_BindsToTheLocalizedOptionValue()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));

        Assert.Contains("SelectedItem=\"{Binding SelectedTimeRange, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedTimeRange = TimeRangeOptions.Count == 0 ? null : TimeRangeOptions[SelectedTimeRangeIndex]", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupedVideoLayout_StacksDatesAndWrapsVisibleCardsFromLeftToRight()
    {
        RunOnStaThread(() =>
        {
            GroupedVideoLayoutItem[] items =
            [
                new(new DateTime(2026, 8, 13), "first"),
                new(new DateTime(2026, 8, 13), "second"),
                new(new DateTime(2026, 8, 7), "third"),
            ];
            System.Windows.Data.ListCollectionView view = new(items);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(GroupedVideoLayoutItem.Date)));

            System.Windows.FrameworkElementFactory itemPanel = new(typeof(System.Windows.Controls.Primitives.UniformGrid));
            itemPanel.SetValue(System.Windows.FrameworkElement.WidthProperty, 780d);
            itemPanel.SetValue(System.Windows.Controls.Primitives.UniformGrid.ColumnsProperty, 2);
            System.Windows.FrameworkElementFactory groupPanel = new(typeof(System.Windows.Controls.VirtualizingStackPanel));
            groupPanel.SetValue(System.Windows.Controls.VirtualizingStackPanel.OrientationProperty, System.Windows.Controls.Orientation.Vertical);
            System.Windows.FrameworkElementFactory groupItemsPresenter = new(typeof(System.Windows.Controls.ItemsPresenter));
            System.Windows.FrameworkElementFactory groupHeader = new(typeof(System.Windows.Controls.ContentPresenter));
            groupHeader.SetValue(System.Windows.Controls.ContentPresenter.ContentProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.ContentControl.ContentProperty));
            System.Windows.FrameworkElementFactory groupStack = new(typeof(System.Windows.Controls.StackPanel));
            groupStack.AppendChild(groupHeader);
            groupStack.AppendChild(groupItemsPresenter);
            System.Windows.Controls.ControlTemplate groupTemplate = new(typeof(System.Windows.Controls.GroupItem))
            {
                VisualTree = groupStack,
            };

            System.Windows.Controls.ListBox listBox = new()
            {
                Width = 800d,
                Height = 500d,
                ItemsSource = view,
                ItemsPanel = new System.Windows.Controls.ItemsPanelTemplate(itemPanel),
            };
            System.Windows.Controls.VirtualizingPanel.SetIsVirtualizing(listBox, false);
            listBox.GroupStyle.Add(new System.Windows.Controls.GroupStyle
            {
                Panel = new System.Windows.Controls.ItemsPanelTemplate(groupPanel),
                ContainerStyle = new System.Windows.Style(typeof(System.Windows.Controls.GroupItem))
                {
                    Setters =
                    {
                        new System.Windows.Setter(System.Windows.Controls.Control.TemplateProperty, groupTemplate),
                    },
                },
            });
            System.Windows.Window window = new()
            {
                Width = 820d,
                Height = 520d,
                Left = -10000d,
                Top = -10000d,
                ShowInTaskbar = false,
                WindowStyle = System.Windows.WindowStyle.None,
                Content = listBox,
            };

            try
            {
                window.Show();
                listBox.UpdateLayout();

                System.Windows.Controls.GroupItem[] groups = FindVisualDescendants<System.Windows.Controls.GroupItem>(listBox).ToArray();
                System.Windows.Controls.ListBoxItem[] cards = FindVisualDescendants<System.Windows.Controls.ListBoxItem>(listBox).ToArray();

                Assert.Equal(2, groups.Length);
                Assert.Equal(3, cards.Length);
                System.Windows.Point firstGroupPosition = groups[0].TranslatePoint(new System.Windows.Point(), listBox);
                System.Windows.Point secondGroupPosition = groups[1].TranslatePoint(new System.Windows.Point(), listBox);
                System.Windows.Point firstCardPosition = cards[0].TranslatePoint(new System.Windows.Point(), listBox);
                System.Windows.Point secondCardPosition = cards[1].TranslatePoint(new System.Windows.Point(), listBox);
                Assert.True(secondGroupPosition.Y > firstGroupPosition.Y, $"Date groups were arranged at {firstGroupPosition} and {secondGroupPosition}.");
                Assert.True(secondCardPosition.X > firstCardPosition.X, $"Video cards were arranged at {firstCardPosition} and {secondCardPosition}.");
                Assert.All(cards, card => Assert.True(card.ActualHeight > 0d));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void VideoDateGrouping_UsesTheCalendarDateOnly()
    {
        RecordedVideoItem item = new() { CreatedAt = new DateTime(2026, 8, 11, 23, 59, 58) };

        Assert.Equal(new DateTime(2026, 8, 11), item.DateGroupKey);
    }

    [Fact]
    public void VideoDateGroupLabel_UsesTheRequestedCulture()
    {
        CultureInfo previousCulture = Locale.Culture;
        CultureInfo culture = CultureInfo.GetCultureInfo("zh-CN");
        DateTime date = new(2026, 8, 11);
        VideoDateGroupLabelConverter converter = new();

        try
        {
            Locale.Culture = culture;
            Assert.Equal("8月11日", converter.Convert(date, typeof(string), null!, CultureInfo.InvariantCulture));
        }
        finally
        {
            Locale.Culture = previousCulture;
        }
    }

    [Theory]
    [InlineData("video.ts", "TS/FLV", true)]
    [InlineData("video.flv", "TS/FLV", true)]
    [InlineData("video.mp4", "TS/FLV -> MP4", true)]
    [InlineData("video.mkv", "TS/FLV -> MKV", true)]
    [InlineData("video.ts", "TS/FLV -> MP4", false)]
    [InlineData("video.mp4", "TS/FLV -> MKV", false)]
    public void IsTargetVideoFormat_MatchesConfiguredOutput(string filePath, string recordFormat, bool expected)
    {
        Assert.Equal(expected, ScreenRecordListViewModel.IsTargetVideoFormat(filePath, recordFormat));
    }

    [Theory]
    [InlineData("video.ts", false)]
    [InlineData("video.flv", false)]
    [InlineData("video.mp4", true)]
    [InlineData("video.mkv", true)]
    [InlineData("video.webm", false)]
    public void IsCompletedVideoFormat_RecognizesFinalContainers(string filePath, bool expected)
    {
        Assert.Equal(expected, ScreenRecordListViewModel.IsCompletedVideoFormat(filePath));
    }

    [Fact]
    public void VideoFormatStatus_SeparatesTranscodableAndCompletedNonTargets()
    {
        RecordedVideoItem source = new()
        {
            FullPath = "video.ts",
            SupportsTranscode = true,
            TargetRecordFormat = "TS/FLV -> MP4",
        };
        RecordedVideoItem completed = new()
        {
            FullPath = "video.mp4",
            SupportsTranscode = false,
            TargetRecordFormat = "TS/FLV",
        };

        Assert.True(source.IsNonTargetTranscodableFormat);
        Assert.False(source.IsNonTargetCompletedFormat);
        Assert.False(completed.IsNonTargetTranscodableFormat);
        Assert.True(completed.IsNonTargetCompletedFormat);
    }

    [Theory]
    [InlineData("video.ts", "TS")]
    [InlineData("video.Mp4", "MP4")]
    [InlineData("video", "-")]
    public void GetVideoFormatText_ReturnsNormalizedExtension(string filePath, string expected)
    {
        Assert.Equal(expected, ScreenRecordListViewModel.GetVideoFormatText(filePath));
    }

    [Fact]
    public void VideoListXaml_AlignsEdgeFadesWithScrollableContent()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("Property=\"Padding\" Value=\"20,8,0,10\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{StaticResource UiXPageHeaderMargin}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VideoListHeaderActions\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<TranslateTransform Y=\"8\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TranslateTransform Y=\"0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"20,8,22,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpacityMask=\"{StaticResource TopEdgeFadeOpacityMask}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"20,0,22,10\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpacityMask=\"{StaticResource BottomEdgeFadeOpacityMask}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VideoListTopFade\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VideoListBottomFade\"", xaml, StringComparison.Ordinal);

        string codeBehind = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        Assert.Contains("scrollViewer.VerticalOffset > 0.5d", codeBehind, StringComparison.Ordinal);
        Assert.Contains("scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 0.5d", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListUiXToolbar_UsesResponsiveRowsWithoutChangingLegacyDefaults()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement headerRow = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "VideoListHeaderRow");
        XElement toolbarRow = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "VideoListToolbarRow");
        XElement toolbarGrid = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "VideoListToolbarGrid");

        Assert.Equal("92", (string?)headerRow.Attribute("Height"));
        Assert.Equal("44", (string?)toolbarRow.Attribute("Height"));
        Assert.Equal("VideoListToolbarGridSizeChanged", (string?)toolbarGrid.Attribute("SizeChanged"));
        Assert.Contains(toolbarGrid.Descendants(), element => (string?)element.Attribute(xaml + "Name") == "VideoListStreamerFilterGroup");
        Assert.Contains(toolbarGrid.Descendants(), element => (string?)element.Attribute(xaml + "Name") == "VideoListTimeFilterGroup");
        Assert.Contains(toolbarGrid.Descendants(), element => (string?)element.Attribute(xaml + "Name") == "VideoListMultiSelectToolbar");

        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));
        Assert.Contains("VideoListHeaderRow.Height = isUiXEnabled ? GridLength.Auto : new GridLength(92d)", code, StringComparison.Ordinal);
        Assert.Contains("VideoListToolbarRow.Height = isUiXEnabled ? GridLength.Auto : new GridLength(44d)", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(VideoListMultiSelectToolbar, isWide ? 0 : 1)", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(VideoListTimeFilterGroup, isCompact ? 1 : 0)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", toolbarGrid.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(480, null, (int)VideoListToolbarLayoutMode.Compact)]
    [InlineData(620, null, (int)VideoListToolbarLayoutMode.Stacked)]
    [InlineData(720, null, (int)VideoListToolbarLayoutMode.Wide)]
    [InlineData(670, (int)VideoListToolbarLayoutMode.Wide, (int)VideoListToolbarLayoutMode.Wide)]
    [InlineData(659, (int)VideoListToolbarLayoutMode.Wide, (int)VideoListToolbarLayoutMode.Stacked)]
    [InlineData(480, (int)VideoListToolbarLayoutMode.Wide, (int)VideoListToolbarLayoutMode.Compact)]
    [InlineData(699, (int)VideoListToolbarLayoutMode.Stacked, (int)VideoListToolbarLayoutMode.Stacked)]
    [InlineData(700, (int)VideoListToolbarLayoutMode.Stacked, (int)VideoListToolbarLayoutMode.Wide)]
    [InlineData(541, (int)VideoListToolbarLayoutMode.Stacked, (int)VideoListToolbarLayoutMode.Stacked)]
    [InlineData(500, (int)VideoListToolbarLayoutMode.Stacked, (int)VideoListToolbarLayoutMode.Compact)]
    [InlineData(539, (int)VideoListToolbarLayoutMode.Compact, (int)VideoListToolbarLayoutMode.Compact)]
    [InlineData(540, (int)VideoListToolbarLayoutMode.Compact, (int)VideoListToolbarLayoutMode.Stacked)]
    public void VideoListUiXToolbar_UsesStableResponsiveThresholds(
        double width,
        int? currentMode,
        int expectedMode)
    {
        VideoListToolbarLayoutMode? current = currentMode.HasValue
            ? (VideoListToolbarLayoutMode)currentMode.Value
            : null;

        Assert.Equal(
            (VideoListToolbarLayoutMode)expectedMode,
            ScreenRecordListWindow.ResolveVideoListToolbarLayoutMode(width, current));
    }

    [Fact]
    public void StreamerFilter_DefaultsToAllStreamers()
    {
        ScreenRecordListViewModel viewModel = new();

        Assert.NotEmpty(viewModel.StreamerOptions);
        Assert.Equal(viewModel.StreamerOptions[0], viewModel.SelectedStreamer);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.SelectedStreamer));
    }

    [Fact]
    public void VideoListXaml_DisablesHorizontalScrollingAndFocusOutline()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FocusVisualStyle=\"{x:Null}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListXaml_HidesMultiSelectEntryWhenNoVideosAreVisible()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("Binding=\"{Binding HasVisibleVideos}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListXaml_ShowsLocalizedRecordingChipForRecordingFiles()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("Binding=\"{Binding IsRecordingFile}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#24D13438\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"#80D13438\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"{StaticResource Win11ControlCornerRadius}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{I18N RecordStatusOfRecording}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoListXaml_ShowsLocalizedStallSegmentChip()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));

        Assert.Contains("Binding=\"{Binding IsStallSegment}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{I18N StallSegmentChip}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalResources_DoNotOverrideDefaultControlFocusOutlines()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Resources.xaml"));

        Assert.DoesNotContain("x:Key=\"{x:Static SystemParameters.FocusVisualStyleKey}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionHistoryLimit_IsTwoHundred()
    {
        Assert.Equal(200, ScreenRecordListViewModel.SelectionHistoryLimit);
    }

    [Fact]
    public void VideoSelection_SeparatesRegularAndMultiSelection()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        RecordedVideoItem third = new() { FullPath = @"C:\videos\third.ts" };
        videos.Add(first);
        videos.Add(second);
        videos.Add(third);

        viewModel.SelectRegularItem(first);

        Assert.Same(first, viewModel.RegularSelectedItem);
        Assert.False(first.IsSelected);
        Assert.False(viewModel.IsMultiSelectMode);

        viewModel.SelectMultipleItem(second, true, false);

        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.True(viewModel.IsMultiSelectMode);

        viewModel.SelectRegularItem(third);
        viewModel.ClearRegularSelection();

        Assert.Null(viewModel.RegularSelectedItem);
        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.True(viewModel.IsMultiSelectMode);
    }

    [Fact]
    public void VideoMarqueeSelection_ReplacesPreviouslySelectedItems()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        videos.Add(first);
        videos.Add(second);

        viewModel.ReplaceSelectionWithItems([first]);
        viewModel.ReplaceSelectionWithItems([second]);

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.Equal(1, viewModel.SelectedVideoCount);
    }

    [Fact]
    public void VideoSelection_CanSelectRegularItemAfterLeavingMultiSelect()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem item = new() { FullPath = @"C:\videos\first.ts" };
        videos.Add(item);
        viewModel.SelectRegularItem(item);

        viewModel.SelectMultipleItem(item, true, false);

        Assert.True(viewModel.IsMultiSelectMode);
        Assert.True(item.IsSelected);

        viewModel.CancelMultiSelectCommand.Execute(null);
        Assert.Null(viewModel.RegularSelectedItem);
        Assert.False(viewModel.IsMultiSelectMode);
        Assert.False(item.IsSelected);

        viewModel.SelectRegularItem(item);

        Assert.Same(item, viewModel.RegularSelectedItem);
        Assert.False(viewModel.IsMultiSelectMode);
        Assert.False(item.IsSelected);
        Assert.Equal(0, item.SelectionOrder);
    }

    [Fact]
    public void ClearMultiSelection_KeepsMultiSelectModeForSecondEscape()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        videos.Add(first);
        videos.Add(second);

        viewModel.SelectMultipleItem(first, true, false);
        viewModel.SelectMultipleItem(second, true, false);
        viewModel.ClearMultiSelectionCommand.Execute(null);

        Assert.True(viewModel.IsMultiSelectMode);
        Assert.False(first.IsSelected);
        Assert.False(second.IsSelected);
        Assert.Equal(0, viewModel.SelectedVideoCount);
    }

    [Fact]
    public void ResetSelectionForPageNavigation_ClearsMultiSelectionWithoutClearingRegularFocus()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem regular = new() { FullPath = @"C:\videos\regular.ts" };
        RecordedVideoItem multi = new() { FullPath = @"C:\videos\multi.ts" };
        videos.Add(regular);
        videos.Add(multi);

        viewModel.SelectRegularItem(regular);
        viewModel.SelectMultipleItem(multi, true, false);
        viewModel.ResetSelectionForPageNavigation();

        Assert.Same(regular, viewModel.RegularSelectedItem);
        Assert.False(viewModel.IsMultiSelectMode);
        Assert.False(regular.IsSelected);
        Assert.False(multi.IsSelected);
        Assert.Equal(0, viewModel.SelectedVideoCount);
    }

    [Fact]
    public void ExtensionVideoSelection_ReturnsRegularSelectedExistingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.VideoSelection.{Guid.NewGuid():N}");
        string mediaPath = Path.Combine(root, "regular.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(mediaPath, [1]);
        try
        {
            ScreenRecordListViewModel viewModel = new();
            ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
            RecordedVideoItem item = new()
            {
                FullPath = mediaPath,
                NickName = "主播",
                Title = "旧视频",
                CreatedAt = new DateTime(2026, 8, 1),
            };
            videos.Add(item);
            viewModel.SelectRegularItem(item);

            ExtensionVideoFileInfo selected = Assert.Single(viewModel.GetSelectedFiles());

            Assert.Equal(new FileInfo(mediaPath).FullName, selected.FilePath);
            Assert.Equal("主播", selected.NickName);
            Assert.Equal("旧视频", selected.Title);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExtensionVideoSelection_UsesUserOrderAndSkipsMissingFilesInMultiSelect()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Emerde.VideoSelection.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string firstPath = Path.Combine(root, "first.mp4");
        string secondPath = Path.Combine(root, "second.mp4");
        string missingPath = Path.Combine(root, "missing.mp4");
        File.WriteAllBytes(firstPath, [1]);
        File.WriteAllBytes(secondPath, [2]);
        try
        {
            ScreenRecordListViewModel viewModel = new();
            ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
            RecordedVideoItem first = new() { FullPath = firstPath };
            RecordedVideoItem second = new() { FullPath = secondPath };
            RecordedVideoItem missing = new() { FullPath = missingPath };
            videos.Add(first);
            videos.Add(second);
            videos.Add(missing);
            viewModel.SelectMultipleItem(second, true, false);
            viewModel.SelectMultipleItem(missing, true, false);
            viewModel.SelectMultipleItem(first, true, false);

            IReadOnlyList<ExtensionVideoFileInfo> selected = viewModel.GetSelectedFiles();

            Assert.Equal([new FileInfo(secondPath).FullName, new FileInfo(firstPath).FullName], selected.Select(item => item.FilePath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void VideoSelection_AssignsOrderByUserSelectionSequence()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FileName = "first.ts", FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FileName = "second.ts", FullPath = @"C:\videos\second.ts" };
        RecordedVideoItem third = new() { FileName = "third.ts", FullPath = @"C:\videos\third.ts" };
        videos.Add(first);
        videos.Add(second);
        videos.Add(third);

        viewModel.SelectMultipleItem(third, true, false);
        viewModel.SelectMultipleItem(first, true, false);

        Assert.Equal(2, first.SelectionOrder);
        Assert.Equal(0, second.SelectionOrder);
        Assert.Equal(1, third.SelectionOrder);
        Assert.Equal(["third.ts", "first.ts"], ScreenRecordListViewModel.OrderVideosForMerge([first, third]).Select(item => item.FileName));
    }

    [Fact]
    public void VideoSelection_DeselectCompactsOrderAndReselectMovesToEnd()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        videos.Add(first);
        videos.Add(second);

        viewModel.SelectMultipleItem(first, true, false);
        viewModel.SelectMultipleItem(second, true, false);
        viewModel.SelectMultipleItem(first, true, false);

        Assert.Equal(0, first.SelectionOrder);
        Assert.Equal(1, second.SelectionOrder);

        viewModel.SelectMultipleItem(first, true, false);

        Assert.Equal(2, first.SelectionOrder);
        Assert.Equal(1, second.SelectionOrder);
    }

    [Fact]
    public void VideoSelection_UndoRedoRestoresSelectionOrder()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        videos.Add(first);
        videos.Add(second);

        viewModel.SelectMultipleItem(second, true, false);
        viewModel.SelectMultipleItem(first, true, false);
        viewModel.UndoSelection();

        Assert.Equal(0, first.SelectionOrder);
        Assert.Equal(1, second.SelectionOrder);

        viewModel.RedoSelection();

        Assert.Equal(2, first.SelectionOrder);
        Assert.Equal(1, second.SelectionOrder);
    }

    [Fact]
    public void VideoSelection_UsesSegmentOrderOnlyWhileSelectionIsOneSegmentSeries()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem secondSegment = new() { FileName = "record_001.ts", FullPath = @"C:\videos\record_001.ts" };
        RecordedVideoItem firstSegment = new() { FileName = "record_000.ts", FullPath = @"C:\videos\record_000.ts" };
        RecordedVideoItem intro = new() { FileName = "intro.ts", FullPath = @"C:\videos\intro.ts" };
        videos.Add(secondSegment);
        videos.Add(firstSegment);
        videos.Add(intro);

        viewModel.SelectMultipleItem(secondSegment, true, false);
        viewModel.SelectMultipleItem(firstSegment, true, false);

        Assert.Equal(2, secondSegment.SelectionOrder);
        Assert.Equal(1, firstSegment.SelectionOrder);

        viewModel.SelectMultipleItem(intro, true, false);

        Assert.Equal(1, secondSegment.SelectionOrder);
        Assert.Equal(2, firstSegment.SelectionOrder);
        Assert.Equal(3, intro.SelectionOrder);
    }

    [Fact]
    public void VideoSelection_FilterKeepsVisibleOrdersContinuousAndRestoresUserOrder()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem hidden = new() { FullPath = @"C:\videos\hidden.ts", NickName = "Hidden" };
        RecordedVideoItem visible = new() { FullPath = @"C:\videos\visible.ts", NickName = "Visible" };
        videos.Add(hidden);
        videos.Add(visible);
        viewModel.SelectMultipleItem(hidden, true, false);
        viewModel.SelectMultipleItem(visible, true, false);

        viewModel.SelectedStreamer = "Visible";

        Assert.True(hidden.IsSelected);
        Assert.Equal(0, hidden.SelectionOrder);
        Assert.Equal(1, visible.SelectionOrder);

        viewModel.SelectedStreamer = string.Empty;

        Assert.Equal(1, hidden.SelectionOrder);
        Assert.Equal(2, visible.SelectionOrder);
    }

    [Fact]
    public void VideoList_ShowsSelectionOrderBadgeInTheSelectionControlSlot()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XElement badge = document.Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "VideoSelectionOrderBadge");

        Assert.Equal("24", (string?)badge.Attribute("Height"));
        Assert.Equal("12", (string?)badge.Attribute("CornerRadius"));
        Assert.Contains(badge.Descendants(), element => (string?)element.Attribute("Text") == "{Binding SelectionOrder}");
        Assert.Contains(badge.Descendants(), element =>
            element.Name.LocalName == "Condition"
            && (string?)element.Attribute("Binding") == "{Binding IsSelected}"
            && (string?)element.Attribute("Value") == "True");
    }

    [Fact]
    public void VideoList_MarqueeUsesUiXCornerRadiusWithoutChangingLegacyShape()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XElement marquee = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "VideoSelectionRectangle");
        XElement style = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Style"
            && (string?)element.Attribute(x + "Key") == "VideoSelectionRectangleStyle");

        Assert.Equal("Border", marquee.Name.LocalName);
        Assert.Equal("Canvas", marquee.Parent?.Name.LocalName);
        Assert.Equal("{StaticResource VideoSelectionRectangleStyle}", (string?)marquee.Attribute("Style"));
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "CornerRadius"
            && (string?)element.Attribute("Value") == "0");
        Assert.Contains(style.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") == "CornerRadius"
            && (string?)element.Attribute("Value") == "{StaticResource UiXNestedCornerRadius}");
    }

    [Fact]
    public void VideoList_UsesMutuallyExclusiveRegularAndMultiSelectionLayers()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        XElement[] triggers = document.Descendants().Where(element => element.Name.LocalName == "MultiDataTrigger").ToArray();
        XElement regularTrigger = Assert.Single(triggers, trigger =>
            trigger.Descendants().Any(element =>
                element.Name.LocalName == "Condition"
                && (string?)element.Attribute("Binding") == "{Binding IsSelected, RelativeSource={RelativeSource AncestorType={x:Type ListBoxItem}}}"
                && (string?)element.Attribute("Value") == "True")
            && trigger.Descendants().Any(element =>
                element.Name.LocalName == "Condition"
                && (string?)element.Attribute("Binding") == "{Binding DataContext.IsMultiSelectMode, RelativeSource={RelativeSource AncestorType={x:Type local:ScreenRecordListWindow}}}"
                && (string?)element.Attribute("Value") == "False"));
        XElement multiTrigger = Assert.Single(triggers, trigger =>
            trigger.Descendants().Any(element =>
                element.Name.LocalName == "Condition"
                && (string?)element.Attribute("Binding") == "{Binding IsSelected}"
                && (string?)element.Attribute("Value") == "True")
            && trigger.Descendants().Any(element =>
                element.Name.LocalName == "Condition"
                && (string?)element.Attribute("Binding") == "{Binding DataContext.IsMultiSelectMode, RelativeSource={RelativeSource AncestorType={x:Type local:ScreenRecordListWindow}}}"
                && (string?)element.Attribute("Value") == "True")
            && trigger.Descendants().Any(element =>
                element.Name.LocalName == "DoubleAnimation"
                && (string?)element.Attribute("Storyboard.TargetName") == "VideoCardMultiSelectionLayer"));

        Assert.Contains(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "VideoCardSelectionLayer");
        Assert.Contains(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "VideoCardMultiSelectionLayer");
        Assert.All(regularTrigger.Descendants().Where(element => element.Name.LocalName == "DoubleAnimation"), animation =>
        {
            Assert.Equal("VideoCardSelectionLayer", (string?)animation.Attribute("Storyboard.TargetName"));
            Assert.Equal("Stop", (string?)animation.Attribute("FillBehavior"));
        });
        Assert.All(multiTrigger.Descendants().Where(element => element.Name.LocalName == "DoubleAnimation"), animation =>
        {
            Assert.Equal("VideoCardMultiSelectionLayer", (string?)animation.Attribute("Storyboard.TargetName"));
            Assert.Equal("Stop", (string?)animation.Attribute("FillBehavior"));
        });
    }

    [Fact]
    public void SelectItems_EmptySelectionDoesNotEnterMultiSelect()
    {
        ScreenRecordListViewModel viewModel = new();

        viewModel.SelectItems([]);

        Assert.False(viewModel.IsMultiSelectMode);
    }

    [Fact]
    public void EmptyVideoList_CannotEnterMultiSelect()
    {
        ScreenRecordListViewModel viewModel = new();

        viewModel.BeginMultiSelectCommand.Execute(null);
        viewModel.SelectAllCommand.Execute(null);

        Assert.False(viewModel.HasVisibleVideos);
        Assert.False(viewModel.IsMultiSelectMode);
    }

    [Fact]
    public void EmptyFilteredVideoList_ExitsMultiSelectAndClearsSelection()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem item = new() { FullPath = @"C:\videos\first.ts", NickName = "Streamer" };
        videos.Add(item);
        viewModel.SelectAllCommand.Execute(null);

        viewModel.SelectedStreamer = "Other";

        Assert.False(viewModel.HasVisibleVideos);
        Assert.False(viewModel.IsMultiSelectMode);
        Assert.False(item.IsSelected);
        Assert.Null(viewModel.RegularSelectedItem);
    }

    [Fact]
    public void BeginMultiSelect_EntersModeWithoutSelectingRegularItem()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem item = new() { FullPath = @"C:\videos\first.ts" };
        videos.Add(item);
        viewModel.SelectRegularItem(item);

        viewModel.BeginMultiSelectCommand.Execute(null);

        Assert.True(viewModel.IsMultiSelectMode);
        Assert.Same(item, viewModel.RegularSelectedItem);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public void SelectAll_EntersMultiSelectAndCanBeUndoneInOneStep()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem first = new() { FullPath = @"C:\videos\first.ts" };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\second.ts" };
        videos.Add(first);
        videos.Add(second);

        viewModel.SelectAllCommand.Execute(null);

        Assert.True(viewModel.IsMultiSelectMode);
        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);

        viewModel.UndoSelection();

        Assert.False(viewModel.IsMultiSelectMode);
        Assert.False(first.IsSelected);
        Assert.False(second.IsSelected);
    }

    [Fact]
    public void SelectAll_SkipsRecordingFiles()
    {
        ScreenRecordListViewModel viewModel = new();
        ObservableCollection<RecordedVideoItem> videos = Assert.IsType<ObservableCollection<RecordedVideoItem>>(viewModel.Videos.SourceCollection);
        RecordedVideoItem recording = new() { FullPath = @"C:\videos\recording.ts", IsRecordingFile = true };
        RecordedVideoItem completed = new() { FullPath = @"C:\videos\completed.ts" };
        videos.Add(recording);
        videos.Add(completed);

        viewModel.SelectAllCommand.Execute(null);

        Assert.True(viewModel.IsMultiSelectMode);
        Assert.False(recording.IsSelected);
        Assert.True(completed.IsSelected);
    }

    [Fact]
    public void RecordingVideoItem_CannotBeSelectedOrModified()
    {
        RecordedVideoItem item = new() { SupportsTranscode = true, IsSelected = true, SelectionOrder = 1 };

        item.IsRecordingFile = true;

        Assert.False(item.IsSelected);
        Assert.Equal(0, item.SelectionOrder);
        Assert.False(item.CanSelect);
        Assert.False(item.CanModify);
        Assert.False(item.CanTranscode);
    }

    [Theory]
    [InlineData(false, false, false, 2, true)]
    [InlineData(false, false, false, 1, false)]
    [InlineData(true, false, false, 2, false)]
    [InlineData(false, true, false, 2, false)]
    [InlineData(false, false, true, 2, false)]
    public void ShouldOpenVideoFromClick_OnlyAcceptsRegularDoubleClick(
        bool isMultiSelectMode,
        bool toggleSelection,
        bool selectRange,
        int clickCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            ScreenRecordListWindow.ShouldOpenVideoFromClick(isMultiSelectMode, toggleSelection, selectRange, clickCount));
    }

    [Fact]
    public void VideoMarquee_StartsOnlyFromEmptyListSpace()
    {
        Assert.True(ScreenRecordListWindow.CanStartVideoMarquee(null));
        Assert.False(ScreenRecordListWindow.CanStartVideoMarquee(new RecordedVideoItem()));
    }

    [Fact]
    public void ContextMenuCommands_AreAvailable()
    {
        ScreenRecordListViewModel viewModel = new();

        Assert.NotNull(viewModel.DeleteVideoCommand);
        Assert.NotNull(viewModel.SplitVideoCommand);
        Assert.NotNull(viewModel.TranscodeVideoCommand);
        Assert.NotNull(viewModel.OpenDirectoryCommand);
        Assert.NotNull(viewModel.RenameVideoCommand);
        Assert.NotNull(viewModel.OpenVideoCommand);
        Assert.NotNull(viewModel.BeginMultiSelectCommand);
        Assert.NotNull(viewModel.SaveAsVideoCommand);
        Assert.NotNull(viewModel.SplitSelectedCommand);
        Assert.NotNull(viewModel.SplitContextCommand);
        Assert.NotNull(viewModel.MoveContextCommand);
        Assert.NotNull(viewModel.CopyContextCommand);
        Assert.NotNull(viewModel.DeleteContextCommand);
        Assert.NotNull(viewModel.OpenMergeSelectedCommand);
        Assert.NotNull(viewModel.ConfirmMergeSelectedCommand);
    }

    [Fact]
    public void VideoContextMenu_BindsCommandsOnlyAfterCardResolution()
    {
        string xaml = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml"));
        string code = File.ReadAllText(FindRepositoryFile("src", "Emerde", "Views", "ScreenRecordListWindow.xaml.cs"));

        Assert.DoesNotContain("CommandParameter=\"{Binding DataContext}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding Tag.", xaml, StringComparison.Ordinal);
        Assert.Contains("card.DataContext is not RecordedVideoItem item", code, StringComparison.Ordinal);
        Assert.Contains("ConfigureVideoContextMenuItem(menuItem, item, viewModel)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void FileNameBreakOpportunities_PreserveTextElementsAndDisplayNameOmitsExtension()
    {
        const string fileName = "主播😀_2026-08-20.mkv";

        string displayName = RecordedVideoItem.AddFileNameBreakOpportunities(fileName);
        RecordedVideoItem item = new() { FileName = fileName };

        Assert.Equal(fileName, displayName.Replace("\u200B", string.Empty, StringComparison.Ordinal));
        Assert.Contains("\u200B", displayName, StringComparison.Ordinal);
        Assert.DoesNotContain("\uD83D\u200B\uDE00", displayName, StringComparison.Ordinal);
        Assert.Equal("主播😀_2026-08-20", item.DisplayFileName);
        Assert.Equal(item.DisplayFileName, item.UiXWrappedFileName.Replace("\u200B", string.Empty, StringComparison.Ordinal));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    [Fact]
    public void EnumerateVideoFiles_ReturnsEmptyWhenFolderDoesNotExist()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"emerde-missing-{Guid.NewGuid():N}");

        Assert.Empty(ScreenRecordListViewModel.EnumerateVideoFiles(folder));
    }

    [Theory]
    [InlineData(@"主播A\2026-07\03\record.mp4", "主播A")]
    [InlineData(@"主播A\2026-07\record.mp4", "主播A")]
    [InlineData(@"Imported\Nested\record.mp4", "Nested")]
    public void InferNickName_UsesRecordedAuthorFolder(string relativePath, string expected)
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-videos-{Guid.NewGuid():N}");
        string filePath = Path.Combine(relativePath.Split('\\').Prepend(root).ToArray());

        string nickName = ScreenRecordListViewModel.InferNickName(filePath, root);

        Assert.Equal(expected, nickName);
    }

    [Fact]
    public void InferNickName_UsesParentFolderForRootVideos()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-videos-{Guid.NewGuid():N}");
        string filePath = Path.Combine(root, "record.mp4");

        string nickName = ScreenRecordListViewModel.InferNickName(filePath, root);

        Assert.Equal(Path.GetFileName(root), nickName);
    }

    [Fact]
    public void InferNickName_AcceptsFolderStartingWithTwoDotsInsideRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-videos-{Guid.NewGuid():N}");
        string filePath = Path.Combine(root, "..host", "record.mp4");

        string nickName = ScreenRecordListViewModel.InferNickName(filePath, root);

        Assert.Equal("..host", nickName);
    }

    [Fact]
    public void BuildDefaultOpenStartInfo_UsesSystemFileAssociation()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "record.mp4");

        System.Diagnostics.ProcessStartInfo startInfo = ScreenRecordListViewModel.BuildDefaultOpenStartInfo(filePath);

        Assert.Equal(filePath, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), startInfo.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("new-name", "new-name.mkv")]
    [InlineData("new-name.mkv", "new-name.mkv")]
    public void TryBuildRenameTarget_PreservesOriginalExtension(string requestedName, string expectedFileName)
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), "old-name.mkv");

        bool result = ScreenRecordListViewModel.TryBuildRenameTarget(sourcePath, requestedName, out string targetPath);

        Assert.True(result);
        Assert.Equal(Path.Combine(Path.GetTempPath(), expectedFileName), targetPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("folder/name")]
    [InlineData("bad:name")]
    public void TryBuildRenameTarget_RejectsInvalidNames(string requestedName)
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), "old-name.mkv");

        Assert.False(ScreenRecordListViewModel.TryBuildRenameTarget(sourcePath, requestedName, out _));
    }

    [Fact]
    public void GetUniquePath_DoesNotOverwriteExistingVideo()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-unique-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string original = Path.Combine(root, "record.mkv");

        try
        {
            File.WriteAllText(original, "video");

            string result = ScreenRecordListViewModel.GetUniquePath(original);

            Assert.Equal(Path.Combine(root, "record_001.mkv"), result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetUniquePath_DoesNotCollideWithExistingDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-unique-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string original = Path.Combine(root, "record.mkv");

        try
        {
            Directory.CreateDirectory(original);

            string result = ScreenRecordListViewModel.GetUniquePath(original);

            Assert.Equal(Path.Combine(root, "record_001.mkv"), result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "Host")]
    [InlineData(2, @"Host\2026-07")]
    [InlineData(3, @"Host\2026-07\12")]
    public void BuildClassifiedFolder_MatchesRecorderPathLevels(int pathLevel, string relativePath)
    {
        string root = Path.Combine(Path.GetTempPath(), "records");
        FileInfo file = new(Path.Combine(Path.GetTempPath(), "source", "record.ts"));
        VideoRecordingMetadata metadata = new()
        {
            NickName = "Host",
            RecordedAt = new DateTime(2026, 7, 12, 10, 0, 0),
        };

        string result = ScreenRecordListViewModel.BuildClassifiedFolder(root, metadata, file, file.DirectoryName!, pathLevel);

        Assert.Equal(string.IsNullOrEmpty(relativePath) ? root : Path.Combine(root, relativePath), result);
    }

    [Fact]
    public void TransferVideoFile_MovePreservesMetadata()
    {
        string sourceRoot = Path.Combine(Path.GetTempPath(), $"emerde-move-source-{Guid.NewGuid():N}");
        string targetRoot = Path.Combine(Path.GetTempPath(), $"emerde-move-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        string sourceVideo = Path.Combine(sourceRoot, "record.ts");
        string sourceMetadata = Path.Combine(sourceRoot, "record.mplr.json");
        string targetVideo = Path.Combine(targetRoot, "record.ts");

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(sourceMetadata, JsonSerializer.Serialize(new VideoRecordingMetadata { NickName = "Host" }));

            ScreenRecordListViewModel.TransferVideoFile(sourceVideo, targetVideo, move: true);

            Assert.False(File.Exists(sourceVideo));
            Assert.False(File.Exists(sourceMetadata));
            Assert.True(File.Exists(targetVideo));
            Assert.Equal("Host", ScreenRecordListViewModel.LoadMetadata(new FileInfo(targetVideo)).NickName);
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }
            if (Directory.Exists(targetRoot))
            {
                Directory.Delete(targetRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void OrderVideosForMerge_UsesFourDigitSegmentIndex()
    {
        RecordedVideoItem[] videos =
        [
            new() { FileName = "record_1001.ts", FullPath = @"C:\videos\record_1001.ts", CreatedAt = new DateTime(2026, 7, 12) },
            new() { FileName = "record_1000.ts", FullPath = @"C:\videos\record_1000.ts", CreatedAt = new DateTime(2026, 7, 12) },
            new() { FileName = "record_1002.ts", FullPath = @"C:\videos\record_1002.ts", CreatedAt = new DateTime(2026, 7, 12) },
        ];

        string[] result = ScreenRecordListViewModel.OrderVideosForMerge(videos).Select(video => video.FileName).ToArray();

        Assert.Equal(["record_1000.ts", "record_1001.ts", "record_1002.ts"], result);
    }

    [Fact]
    public void OrderVideosForMerge_SameSegmentSeriesOverridesUserSelectionOrder()
    {
        RecordedVideoItem[] videos =
        [
            new() { FileName = "record_001.ts", FullPath = @"C:\videos\record_001.ts", SelectionOrder = 1 },
            new() { FileName = "record_000.ts", FullPath = @"C:\videos\record_000.ts", SelectionOrder = 2 },
        ];

        string[] result = ScreenRecordListViewModel.OrderVideosForMerge(videos).Select(video => video.FileName).ToArray();

        Assert.Equal(["record_000.ts", "record_001.ts"], result);
    }

    [Fact]
    public void OrderVideosForMerge_MixedFilesUseUserSelectionOrder()
    {
        RecordedVideoItem[] videos =
        [
            new() { FileName = "record_000.ts", FullPath = @"C:\videos\record_000.ts", SelectionOrder = 2 },
            new() { FileName = "intro.ts", FullPath = @"C:\videos\intro.ts", SelectionOrder = 1 },
        ];

        string[] result = ScreenRecordListViewModel.OrderVideosForMerge(videos).Select(video => video.FileName).ToArray();

        Assert.Equal(["intro.ts", "record_000.ts"], result);
    }

    [Fact]
    public void OrderVideosForMerge_DifferentSegmentSeriesUseUserSelectionOrder()
    {
        RecordedVideoItem[] videos =
        [
            new() { FileName = "first_000.ts", FullPath = @"C:\videos\first_000.ts", SelectionOrder = 2 },
            new() { FileName = "second_000.ts", FullPath = @"C:\videos\second_000.ts", SelectionOrder = 1 },
        ];

        string[] result = ScreenRecordListViewModel.OrderVideosForMerge(videos).Select(video => video.FileName).ToArray();

        Assert.Equal(["second_000.ts", "first_000.ts"], result);
    }

    [Fact]
    public void BuildMergeWarningText_ReportsNonContinuousSegments()
    {
        RecordedVideoItem[] videos =
        [
            new() { NickName = "Host", Resolution = "1920x1080", FullPath = @"C:\videos\record_001.ts" },
            new() { NickName = "Host", Resolution = "1920x1080", FullPath = @"C:\videos\record_003.ts" },
        ];

        string result = ScreenRecordListViewModel.BuildMergeWarningText(videos);

        Assert.Contains("分段编号不连续", result);
        Assert.Contains("按分段编号合并", result);
    }

    [Fact]
    public void ParseMergeStreamSignature_DistinguishesCodecAndAudioLayout()
    {
        const string first = """
            {"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"pix_fmt":"yuv420p","time_base":"1/90000"},{"codec_type":"audio","codec_name":"aac","sample_rate":"48000","channels":2,"channel_layout":"stereo","time_base":"1/48000"}]}
            """;
        const string second = """
            {"streams":[{"codec_type":"video","codec_name":"hevc","width":1920,"height":1080,"pix_fmt":"yuv420p","time_base":"1/90000"},{"codec_type":"audio","codec_name":"aac","sample_rate":"48000","channels":6,"channel_layout":"5.1","time_base":"1/48000"}]}
            """;

        string firstSignature = ScreenRecordListViewModel.ParseMergeStreamSignature(first);
        string secondSignature = ScreenRecordListViewModel.ParseMergeStreamSignature(second);

        Assert.NotEmpty(firstSignature);
        Assert.NotEqual(firstSignature, secondSignature);
    }

    [Fact]
    public void LoadMetadata_UsesSharedSegmentMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string videoPath = Path.Combine(root, "Host_2026-07-03_12-34-56_001.ts");
        string metadataPath = Path.Combine(root, "Host_2026-07-03_12-34-56.mplr.json");

        try
        {
            File.WriteAllText(videoPath, string.Empty);
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new VideoRecordingMetadata
            {
                NickName = "Host",
                Title = "Live Title",
                Resolution = "1920x1080",
                Bitrate = "8 Mbps",
                RecordedAt = new DateTime(2026, 7, 3, 12, 34, 56),
            }));

            VideoRecordingMetadata metadata = ScreenRecordListViewModel.LoadMetadata(new FileInfo(videoPath));

            Assert.Equal("Host", metadata.NickName);
            Assert.Equal("Live Title", metadata.Title);
            Assert.Equal("1920x1080", metadata.Resolution);
            Assert.Equal("8 Mbps", metadata.Bitrate);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadMetadata_UsesSharedMetadataForFourDigitSegment()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string videoPath = Path.Combine(root, "Host_2026-07-03_12-34-56_1000.ts");
        string metadataPath = Path.Combine(root, "Host_2026-07-03_12-34-56.mplr.json");

        try
        {
            File.WriteAllText(videoPath, string.Empty);
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new VideoRecordingMetadata
            {
                NickName = "Host",
            }));

            VideoRecordingMetadata metadata = ScreenRecordListViewModel.LoadMetadata(new FileInfo(videoPath));

            Assert.Equal("Host", metadata.NickName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void NormalizeResolution_HidesMediaInfoLoadError()
    {
        string resolution = ScreenRecordListViewModel.NormalizeResolution("Unable to load MediaInfo library");

        Assert.Equal(ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown"), resolution);
    }

    [Theory]
    [InlineData("1080p", "1080p")]
    [InlineData("2160P", "2160p")]
    [InlineData("1080I", "1080i")]
    public void NormalizeResolution_PreservesVerticalResolutionLabels(string value, string expected)
    {
        Assert.Equal(expected, ScreenRecordListViewModel.NormalizeResolution(value));
    }

    [Fact]
    public void ParseVideoProbeJson_UsesVideoStreamResolutionAndBitrate()
    {
        string json = """
        {
          "streams": [
            {
              "width": 1920,
              "height": 1080,
              "bit_rate": "8000000"
            }
          ],
          "format": {
            "bit_rate": "4000000"
          }
        }
        """;

        VideoProbeInfo info = ScreenRecordListViewModel.ParseVideoProbeJson(json);

        Assert.Equal("1920x1080", info.Resolution);
        Assert.Equal("8 Mbps", info.Bitrate);
    }

    [Fact]
    public void ParseVideoProbeJson_FallsBackToFormatBitrate()
    {
        string json = """
        {
          "streams": [
            {
              "width": 1280,
              "height": 720
            }
          ],
          "format": {
            "bit_rate": "2500000"
          }
        }
        """;

        VideoProbeInfo info = ScreenRecordListViewModel.ParseVideoProbeJson(json);

        Assert.Equal("1280x720", info.Resolution);
        Assert.Equal("2.5 Mbps", info.Bitrate);
    }

    [Fact]
    public void ParseVideoProbeJson_ReadsEmbeddedMetadataTags()
    {
        string json = """
        {
          "streams": [
            {
              "width": 1920,
              "height": 1080
            }
          ],
          "format": {
            "bit_rate": "8000000",
            "tags": {
              "emerde_nick_name": "Host",
              "emerde_title": "Live Title",
              "emerde_room_url": "https://example.com/live",
              "emerde_platform": "Twitch",
              "emerde_recorded_at": "2026-07-03T12:34:56.0000000"
            }
          }
        }
        """;

        VideoProbeInfo info = ScreenRecordListViewModel.ParseVideoProbeJson(json);

        Assert.NotNull(info.Metadata);
        Assert.Equal("Host", info.Metadata!.NickName);
        Assert.Equal("Live Title", info.Metadata.Title);
        Assert.Equal("https://example.com/live", info.Metadata.RoomUrl);
        Assert.Equal("Twitch", info.Metadata.Platform);
        Assert.Equal(new DateTime(2026, 7, 3, 12, 34, 56), info.Metadata.RecordedAt);
    }

    [Fact]
    public void TryDeleteSidecarIfNoSourceVideosRemain_KeepsSharedMetadataUntilLastSegmentRemoved()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-sidecar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string metadataPath = Path.Combine(root, "Host_2026-07-03_12-34-56.mplr.json");
        string firstSegment = Path.Combine(root, "Host_2026-07-03_12-34-56_000.ts");
        string secondSegment = Path.Combine(root, "Host_2026-07-03_12-34-56_001.ts");

        try
        {
            File.WriteAllText(metadataPath, "{}");
            File.WriteAllText(firstSegment, string.Empty);
            File.WriteAllText(secondSegment, string.Empty);

            File.Delete(firstSegment);
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(firstSegment);

            Assert.True(File.Exists(metadataPath));

            File.Delete(secondSegment);
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(secondSegment);

            Assert.False(File.Exists(metadataPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void GetExistingThumbnailPath_UsesCachedThumbnail()
    {
        string videoPath = Path.Combine(Path.GetTempPath(), $"record-{Guid.NewGuid():N}.mp4");
        string cacheDirectory = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-cache-{Guid.NewGuid():N}");
        string thumbnailPath = ScreenRecordListViewModel.GetThumbnailCachePath(videoPath, cacheDirectory);
        Directory.CreateDirectory(cacheDirectory);

        try
        {
            File.WriteAllText(videoPath, "video");
            File.WriteAllText(thumbnailPath, "fake");
            File.SetLastWriteTimeUtc(thumbnailPath, File.GetLastWriteTimeUtc(videoPath).AddSeconds(1));

            string result = ScreenRecordListViewModel.GetExistingThumbnailPath(videoPath, string.Empty, cacheDirectory);

            Assert.Equal(thumbnailPath, result);
        }
        finally
        {
            if (File.Exists(videoPath))
            {
                File.Delete(videoPath);
            }
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void GetExistingThumbnailPath_RejectsThumbnailOlderThanVideo()
    {
        string videoPath = Path.Combine(Path.GetTempPath(), $"record-{Guid.NewGuid():N}.mp4");
        string cacheDirectory = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-cache-{Guid.NewGuid():N}");
        string thumbnailPath = ScreenRecordListViewModel.GetThumbnailCachePath(videoPath, cacheDirectory);
        Directory.CreateDirectory(cacheDirectory);

        try
        {
            File.WriteAllText(thumbnailPath, "fake");
            File.WriteAllText(videoPath, "video");
            File.SetLastWriteTimeUtc(thumbnailPath, DateTime.UtcNow.AddMinutes(-1));
            File.SetLastWriteTimeUtc(videoPath, DateTime.UtcNow);

            string result = ScreenRecordListViewModel.GetExistingThumbnailPath(videoPath, string.Empty, cacheDirectory);

            Assert.Empty(result);
        }
        finally
        {
            if (File.Exists(videoPath))
            {
                File.Delete(videoPath);
            }
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void GetThumbnailCachePath_SharesCacheAcrossVideoFormats()
    {
        string cacheDirectory = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-cache-{Guid.NewGuid():N}");
        string stem = Path.Combine(Path.GetTempPath(), $"record-{Guid.NewGuid():N}");

        string transportStreamThumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(stem + ".ts", cacheDirectory);
        string mp4Thumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(stem + ".mp4", cacheDirectory);
        string otherThumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(stem + "-other.mp4", cacheDirectory);

        Assert.Equal(transportStreamThumbnail, mp4Thumbnail);
        Assert.NotEqual(mp4Thumbnail, otherThumbnail);
    }

    [Fact]
    public void CleanupOrphanedThumbnailCache_DeletesOnlyUnreferencedThumbnails()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-cleanup-{Guid.NewGuid():N}");
        string cacheDirectory = Path.Combine(root, "cache");
        Directory.CreateDirectory(cacheDirectory);
        string sourceVideo = Path.Combine(root, "record.ts");
        string convertedVideo = Path.Combine(root, "record.mp4");
        string sharedThumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(sourceVideo, cacheDirectory);
        string orphanedThumbnail = ScreenRecordListViewModel.GetThumbnailCachePath(Path.Combine(root, "deleted.mp4"), cacheDirectory);

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(convertedVideo, "video");
            File.WriteAllText(sharedThumbnail, "thumbnail");
            File.WriteAllText(orphanedThumbnail, "thumbnail");

            int deleted = ScreenRecordListViewModel.CleanupOrphanedThumbnailCache([sourceVideo, convertedVideo], cacheDirectory);

            Assert.Equal(1, deleted);
            Assert.True(File.Exists(sharedThumbnail));
            Assert.False(File.Exists(orphanedThumbnail));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DeleteThumbnailCacheIfUnused_WaitsForLastVideoFormat()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-delete-{Guid.NewGuid():N}");
        string cacheDirectory = Path.Combine(root, "cache");
        Directory.CreateDirectory(cacheDirectory);
        string sourceVideo = Path.Combine(root, "record.ts");
        string convertedVideo = Path.Combine(root, "record.mp4");
        string thumbnailPath = ScreenRecordListViewModel.GetThumbnailCachePath(sourceVideo, cacheDirectory);

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(convertedVideo, "video");
            File.WriteAllText(thumbnailPath, "thumbnail");

            File.Delete(sourceVideo);
            Assert.False(ScreenRecordListViewModel.DeleteThumbnailCacheIfUnused(sourceVideo, cacheDirectory));
            Assert.True(File.Exists(thumbnailPath));

            File.Delete(convertedVideo);
            Assert.True(ScreenRecordListViewModel.DeleteThumbnailCacheIfUnused(convertedVideo, cacheDirectory));
            Assert.False(File.Exists(thumbnailPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ThumbnailImageConverter_DoesNotLockThumbnailFile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-thumbnail-image-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string thumbnailPath = Path.Combine(root, "thumbnail.jpg");

        try
        {
            object? image = null;
            Exception? error = null;
            Thread thread = new(() =>
            {
                try
                {
                    System.Windows.Media.Imaging.BitmapSource source = System.Windows.Media.Imaging.BitmapSource.Create(
                        1,
                        1,
                        96,
                        96,
                        System.Windows.Media.PixelFormats.Bgra32,
                        null,
                        new byte[] { 0, 0, 255, 255 },
                        4);
                    System.Windows.Media.Imaging.PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
                    using (FileStream stream = File.Create(thumbnailPath))
                    {
                        encoder.Save(stream);
                    }

                    image = ThumbnailImageConverter.LoadImage(thumbnailPath);
                }
                catch (Exception e)
                {
                    error = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(error);
            Assert.NotSame(System.Windows.DependencyProperty.UnsetValue, image);
            File.Delete(thumbnailPath);
            Assert.False(File.Exists(thumbnailPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("30", 0, 1800)]
    [InlineData("30", 1, 30)]
    [InlineData("2", 2, 7200)]
    public void TryConvertSplitDurationSeconds_ConvertsValidValues(string value, int unitIndex, int expected)
    {
        Assert.True(ScreenRecordListViewModel.TryConvertSplitDurationSeconds(value, unitIndex, out int seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData("NaN", 0)]
    [InlineData("Infinity", 0)]
    [InlineData("-1", 0)]
    [InlineData("999999999999", 2)]
    public void TryConvertSplitDurationSeconds_RejectsInvalidValues(string value, int unitIndex)
    {
        Assert.False(ScreenRecordListViewModel.TryConvertSplitDurationSeconds(value, unitIndex, out _));
    }

    [Fact]
    public void CopyAssociatedMetadata_PreservesSharedSegmentMetadata()
    {
        string sourceRoot = Path.Combine(Path.GetTempPath(), $"emerde-source-{Guid.NewGuid():N}");
        string targetRoot = Path.Combine(Path.GetTempPath(), $"emerde-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        string sourceVideo = Path.Combine(sourceRoot, "Host_2026-07-03_12-34-56_1000.ts");
        string sourceMetadata = Path.Combine(sourceRoot, "Host_2026-07-03_12-34-56.mplr.json");
        string targetVideo = Path.Combine(targetRoot, Path.GetFileName(sourceVideo));
        string targetMetadata = Path.Combine(targetRoot, Path.GetFileName(sourceMetadata));

        try
        {
            File.WriteAllText(sourceVideo, string.Empty);
            File.WriteAllText(sourceMetadata, "{\"NickName\":\"Host\"}");
            File.WriteAllText(targetVideo, string.Empty);

            ScreenRecordListViewModel.CopyAssociatedMetadata(sourceVideo, targetVideo);

            Assert.False(File.Exists(targetMetadata));
            Assert.True(VideoRecordingMetadataStore.HasAttachedMetadata(targetVideo));
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(targetVideo));
            Assert.Equal("Host", metadata.NickName);
            Assert.Equal(Path.GetFileName(targetVideo), metadata.FileName);
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }
            if (Directory.Exists(targetRoot))
            {
                Directory.Delete(targetRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RenameVideoFile_MovesVideoAndUpdatesMetadataFileName()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string sourceVideo = Path.Combine(root, "old-name.mkv");
        string sourceMetadata = Path.Combine(root, "old-name.mplr.json");
        string targetVideo = Path.Combine(root, "new-name.mkv");
        string targetMetadata = Path.Combine(root, "new-name.mplr.json");

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(sourceMetadata, JsonSerializer.Serialize(new VideoRecordingMetadata
            {
                FileName = "old-name.mkv",
                NickName = "Host",
            }));

            ScreenRecordListViewModel.RenameVideoFile(sourceVideo, targetVideo);

            Assert.False(File.Exists(sourceVideo));
            Assert.False(File.Exists(sourceMetadata));
            Assert.True(File.Exists(targetVideo));
            Assert.False(File.Exists(targetMetadata));
            Assert.True(VideoRecordingMetadataStore.HasAttachedMetadata(targetVideo));
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(targetVideo));
            Assert.Equal("new-name.mkv", metadata.FileName);
            Assert.Equal("Host", metadata.NickName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RenameVideoFile_DoesNotRequireLegacySidecarPath()
    {
        string root = Path.Combine(Path.GetTempPath(), $"emerde-rename-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string sourceVideo = Path.Combine(root, "old-name.mkv");
        string sourceMetadata = Path.Combine(root, "old-name.mplr.json");
        string targetVideo = Path.Combine(root, "new-name.mkv");
        string targetMetadata = Path.Combine(root, "new-name.mplr.json");

        try
        {
            File.WriteAllText(sourceVideo, "video");
            File.WriteAllText(sourceMetadata, JsonSerializer.Serialize(new VideoRecordingMetadata
            {
                FileName = "old-name.mkv",
                NickName = "Host",
            }));
            Directory.CreateDirectory(targetMetadata);

            ScreenRecordListViewModel.RenameVideoFile(sourceVideo, targetVideo);

            Assert.False(File.Exists(sourceVideo));
            Assert.False(File.Exists(sourceMetadata));
            Assert.True(File.Exists(targetVideo));
            Assert.True(VideoRecordingMetadataStore.HasAttachedMetadata(targetVideo));
            VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(targetVideo));
            Assert.Equal("new-name.mkv", metadata.FileName);
            Assert.Equal("Host", metadata.NickName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SegmentGrouping_GroupsOnlyFilesWithMultipleNumberedParts()
    {
        RecordedVideoItem first = new() { FullPath = @"C:\videos\session_000.ts", CreatedAt = new DateTime(2026, 8, 16, 10, 0, 0) };
        RecordedVideoItem second = new() { FullPath = @"C:\videos\session_001.ts", CreatedAt = new DateTime(2026, 8, 16, 10, 1, 0) };
        RecordedVideoItem singleton = new() { FullPath = @"C:\videos\other_000.ts", CreatedAt = new DateTime(2026, 8, 16, 11, 0, 0) };

        ScreenRecordListViewModel.AssignFallbackSegmentGroups([first, second, singleton]);

        Assert.Equal(first.SegmentGroupId, second.SegmentGroupId);
        Assert.False(string.IsNullOrWhiteSpace(first.SegmentGroupId));
        Assert.Equal(0, first.SegmentIndex);
        Assert.Equal(1, second.SegmentIndex);
        Assert.Equal(2, first.SegmentCount);
        Assert.Equal(2, second.SegmentCount);
        Assert.True(string.IsNullOrWhiteSpace(singleton.SegmentGroupId));
        Assert.Equal(0, singleton.SegmentCount);
    }

    [Fact]
    public void SegmentSorting_KeepsPartsAscendingWhenGroupsAreDescending()
    {
        RecordedVideoItem first = new()
        {
            FileName = "session_000.ts",
            SegmentGroupId = "session",
            SegmentIndex = 0,
            GroupSortTime = new DateTime(2026, 8, 16, 10, 0, 0),
        };
        RecordedVideoItem second = new()
        {
            FileName = "session_001.ts",
            SegmentGroupId = "session",
            SegmentIndex = 1,
            GroupSortTime = new DateTime(2026, 8, 16, 10, 0, 0),
        };

        RecordedVideoItemComparer comparer = new(true);

        Assert.True(comparer.Compare(first, second) < 0);
        Assert.True(comparer.Compare(second, first) > 0);
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        return source[start..end];
    }

    private static IEnumerable<T> FindVisualDescendants<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            System.Windows.DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private sealed record GroupedVideoLayoutItem(DateTime Date, string Name);
}
