using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emerde.Controls;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.Plugins;
using Emerde.Properties;
using Emerde.Threading;
using Emerde.ViewModels;
using Microsoft.VisualBasic.FileIO;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Windows.System;
using WindowsAPICodePack.Dialogs;
using Wpf.Ui.Violeta.Controls;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Emerde.Views;

public partial class ScreenRecordListWindow : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty VideoCardGridWidthProperty = DependencyProperty.Register(nameof(VideoCardGridWidth), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(228d));
    public static readonly DependencyProperty VideoCardColumnCountProperty = DependencyProperty.Register(nameof(VideoCardColumnCount), typeof(int), typeof(ScreenRecordListWindow), new PropertyMetadata(1));
    public static readonly DependencyProperty VideoCardWidthProperty = DependencyProperty.Register(nameof(VideoCardWidth), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(378d));
    public static readonly DependencyProperty VideoCardHeightProperty = DependencyProperty.Register(nameof(VideoCardHeight), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(115d));
    public static readonly DependencyProperty VideoCardCoverWidthProperty = DependencyProperty.Register(nameof(VideoCardCoverWidth), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(136d));
    public static readonly DependencyProperty VideoCardCoverHeightProperty = DependencyProperty.Register(nameof(VideoCardCoverHeight), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(91d));
    public static readonly DependencyProperty VideoCardMarginProperty = DependencyProperty.Register(nameof(VideoCardMargin), typeof(Thickness), typeof(ScreenRecordListWindow), new PropertyMetadata(new Thickness(6d)));
    public static readonly DependencyProperty VideoCardPaddingProperty = DependencyProperty.Register(nameof(VideoCardPadding), typeof(Thickness), typeof(ScreenRecordListWindow), new PropertyMetadata(new Thickness(12d)));
    public static readonly DependencyProperty VideoCardInfoMarginProperty = DependencyProperty.Register(nameof(VideoCardInfoMargin), typeof(Thickness), typeof(ScreenRecordListWindow), new PropertyMetadata(new Thickness(12d, 0d, 0d, 0d)));
    public static readonly DependencyProperty VideoCardFileNameHeightProperty = DependencyProperty.Register(nameof(VideoCardFileNameHeight), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(30d));
    public static readonly DependencyProperty VideoCardFileNameFontSizeProperty = DependencyProperty.Register(nameof(VideoCardFileNameFontSize), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(13d));
    public static readonly DependencyProperty VideoCardFileNameLineHeightProperty = DependencyProperty.Register(nameof(VideoCardFileNameLineHeight), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(16d));
    public static readonly DependencyProperty VideoCardSecondaryFontSizeProperty = DependencyProperty.Register(nameof(VideoCardSecondaryFontSize), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(11d));
    public static readonly DependencyProperty VideoCardSecondaryLineHeightProperty = DependencyProperty.Register(nameof(VideoCardSecondaryLineHeight), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(13d));
    public static readonly DependencyProperty VideoCardDetailFontSizeProperty = DependencyProperty.Register(nameof(VideoCardDetailFontSize), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(10d));
    public static readonly DependencyProperty VideoCardDetailLineHeightProperty = DependencyProperty.Register(nameof(VideoCardDetailLineHeight), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(12d));
    public static readonly DependencyProperty VideoCardStatusHeightProperty = DependencyProperty.Register(nameof(VideoCardStatusHeight), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(17d));
    public static readonly DependencyProperty VideoCardStatusPaddingProperty = DependencyProperty.Register(nameof(VideoCardStatusPadding), typeof(Thickness), typeof(ScreenRecordListWindow), new PropertyMetadata(new Thickness(7d, 0d, 7d, 0d)));
    public static readonly DependencyProperty VideoCardFormatMarginProperty = DependencyProperty.Register(nameof(VideoCardFormatMargin), typeof(Thickness), typeof(ScreenRecordListWindow), new PropertyMetadata(new Thickness(8d, 0d, 0d, 0d)));
    public static readonly DependencyProperty VideoCardCoverIconSizeProperty = DependencyProperty.Register(nameof(VideoCardCoverIconSize), typeof(double), typeof(ScreenRecordListWindow), new PropertyMetadata(24d));

    private const double UiXVideoCardBaseWidth = 378d;
    private const double UiXVideoCardMinimumWidth = 227d;
    private const double UiXVideoCardMaximumWidth = 432d;
    private const double UiXVideoCardInformationWidthRatio = 1.5d;
    private const double UiXVideoCardMinimumInformationWidthRatio = 1.5d;
    private const double UiXVideoCardCoverAspectWidth = 3d;
    private const double UiXVideoCardCoverAspectHeight = 2d;
    private const double UiXVideoCardFileNameFontSize = 13d;
    private const double UiXVideoCardFileNameLineHeight = 16d;
    private const double UiXVideoCardSecondaryFontSize = 11d;
    private const double UiXVideoCardSecondaryLineHeight = 13d;
    private const double UiXVideoCardDetailFontSize = 10d;
    private const double UiXVideoCardDetailLineHeight = 12d;
    private const double UiXVideoCardStatusHeight = 17d;
    private const double UiXVideoCardColumnHysteresis = 16d;
    private const double UiXVideoCardHorizontalGap = 12d;
    private const double UiXVideoCardVerticalGap = 12d;
    private const double UiXVideoListFallbackContentInset = 42d;
    private readonly DispatcherTimer visibleVideoLoadTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer videoMarqueeAutoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private Task? visibleVideoRefreshTask;
    private int videoCardColumnCount;
    private bool videoMarqueeCandidate;
    private bool isVideoMarqueeSelecting;
    private Point videoMarqueeStart;
    private Point videoMarqueePointer;
    private readonly HashSet<RecordedVideoItem> videoMarqueeItems = [];
    private RecordedVideoItem? videoMarqueePressedItem;
    private int videoMarqueeClickCount;
    private IDisposable? videoSelectionRegistration;
    private bool extensionUiSubscribed;
    private bool videoCardMetricsRefreshPending;
    private MainWindow? hostMainWindow;

    public ScreenRecordListViewModel ViewModel { get; } = new();

    public double VideoCardGridWidth
    {
        get => (double)GetValue(VideoCardGridWidthProperty);
        set => SetValue(VideoCardGridWidthProperty, value);
    }

    public int VideoCardColumnCount
    {
        get => (int)GetValue(VideoCardColumnCountProperty);
        set => SetValue(VideoCardColumnCountProperty, value);
    }

    public double VideoCardWidth
    {
        get => (double)GetValue(VideoCardWidthProperty);
        set => SetValue(VideoCardWidthProperty, value);
    }

    public double VideoCardHeight
    {
        get => (double)GetValue(VideoCardHeightProperty);
        set => SetValue(VideoCardHeightProperty, value);
    }

    public double VideoCardCoverWidth
    {
        get => (double)GetValue(VideoCardCoverWidthProperty);
        set => SetValue(VideoCardCoverWidthProperty, value);
    }

    public double VideoCardCoverHeight
    {
        get => (double)GetValue(VideoCardCoverHeightProperty);
        set => SetValue(VideoCardCoverHeightProperty, value);
    }

    public Thickness VideoCardMargin
    {
        get => (Thickness)GetValue(VideoCardMarginProperty);
        set => SetValue(VideoCardMarginProperty, value);
    }

    public ScreenRecordListWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        VideoListModalOverlay.IsVisibleChanged += (_, _) => DialogBlurScope.ApplyBackdropBrush(VideoListModalOverlay);
        visibleVideoLoadTimer.Tick += VisibleVideoLoadTimerTick;
        videoMarqueeAutoScrollTimer.Tick += VideoMarqueeAutoScrollTimerTick;
        ViewModel.VisibleItemsChanged += (_, _) =>
        {
            ScheduleVisibleVideoLoading();
        };
        Loaded += ScreenRecordListWindowLoaded;
        Unloaded += ScreenRecordListWindowUnloaded;
        IsVisibleChanged += ScreenRecordListWindowIsVisibleChanged;
        SizeChanged += (_, e) =>
        {
            ScheduleVisibleVideoLoading();
            UpdateVideoListEdgeFades();
            if (e.WidthChanged)
            {
                QueueVideoCardMetricsRefresh();
            }
        };
        PreviewKeyDown += ScreenRecordListWindowPreviewKeyDown;
    }

    private async void ScreenRecordListWindowLoaded(object sender, RoutedEventArgs e)
    {
        videoSelectionRegistration ??= ExtensionHostRuntime.RegisterHostObject(ExtensionContractNames.VideoSelection, ViewModel);
        if (!extensionUiSubscribed)
        {
            ExtensionHostRuntime.UiContributionsChanged += ExtensionUiContributionsChanged;
            extensionUiSubscribed = true;
        }
        hostMainWindow = Window.GetWindow(this) as MainWindow;
        if (hostMainWindow != null)
        {
            hostMainWindow.ViewModel.PropertyChanged += HostMainWindowPropertyChanged;
        }
        UpdateVideoListGrouping();
        UpdateExtensionToolbar();
        DialogBlurScope.ApplyBackdropBrush(VideoListModalOverlay);
        ViewModel.StartMonitoring();
        if (IsVisible)
        {
            FocusVideoList();
            await RefreshVisibleVideoListAsync();
            UpdateVideoListEdgeFades();
            QueueVideoCardMetricsRefresh();
        }
    }

    private void ScreenRecordListWindowUnloaded(object sender, RoutedEventArgs e)
    {
        if (extensionUiSubscribed)
        {
            ExtensionHostRuntime.UiContributionsChanged -= ExtensionUiContributionsChanged;
            extensionUiSubscribed = false;
        }
        if (hostMainWindow != null)
        {
            hostMainWindow.ViewModel.PropertyChanged -= HostMainWindowPropertyChanged;
            hostMainWindow = null;
        }
        VideoListExtensionToolbarHost.Children.Clear();
        videoSelectionRegistration?.Dispose();
        videoSelectionRegistration = null;
        visibleVideoLoadTimer.Stop();
        FinishVideoMarquee(false);
        ViewModel.StopMonitoring();
        ViewModel.CancelBackgroundLoading();
    }

    private void ExtensionUiContributionsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(UpdateExtensionToolbar);
            return;
        }
        UpdateExtensionToolbar();
    }

    private void UpdateExtensionToolbar()
    {
        if (!IsLoaded || !extensionUiSubscribed)
        {
            return;
        }
        VideoListExtensionToolbarHost.Children.Clear();
        foreach (ExtensionUiContribution contribution in ExtensionHostRuntime.GetUiContributionsSnapshot()
                     .Where(item => item.RegionName.Equals(ExtensionContractNames.VideoListToolbar, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Order))
        {
            VideoListExtensionToolbarHost.Children.Add(contribution.Content);
        }
    }

    private void HostMainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusOfIsUiXEnabled))
        {
            Dispatcher.BeginInvoke(() =>
            {
                UpdateVideoListGrouping();
                QueueVideoCardMetricsRefresh();
            }, DispatcherPriority.DataBind);
        }
    }

    private void UpdateVideoListGrouping()
    {
        using (ViewModel.Videos.DeferRefresh())
        {
            ViewModel.Videos.GroupDescriptions.Clear();
            if (hostMainWindow?.ViewModel.StatusOfIsUiXEnabled == true)
            {
                ViewModel.Videos.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RecordedVideoItem.DateGroupKey)));
            }
        }
    }

    private void QueueVideoCardMetricsRefresh()
    {
        if (!IsLoaded || videoCardMetricsRefreshPending)
        {
            return;
        }

        videoCardMetricsRefreshPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            videoCardMetricsRefreshPending = false;
            UpdateVideoCardMetrics(GetVideoCardContentWidth());
        }, DispatcherPriority.Render);
    }

    public Thickness VideoCardPadding
    {
        get => (Thickness)GetValue(VideoCardPaddingProperty);
        set => SetValue(VideoCardPaddingProperty, value);
    }

    public Thickness VideoCardInfoMargin
    {
        get => (Thickness)GetValue(VideoCardInfoMarginProperty);
        set => SetValue(VideoCardInfoMarginProperty, value);
    }

    public double VideoCardFileNameHeight
    {
        get => (double)GetValue(VideoCardFileNameHeightProperty);
        set => SetValue(VideoCardFileNameHeightProperty, value);
    }

    public double VideoCardFileNameFontSize
    {
        get => (double)GetValue(VideoCardFileNameFontSizeProperty);
        set => SetValue(VideoCardFileNameFontSizeProperty, value);
    }

    public double VideoCardFileNameLineHeight
    {
        get => (double)GetValue(VideoCardFileNameLineHeightProperty);
        set => SetValue(VideoCardFileNameLineHeightProperty, value);
    }

    public double VideoCardSecondaryFontSize
    {
        get => (double)GetValue(VideoCardSecondaryFontSizeProperty);
        set => SetValue(VideoCardSecondaryFontSizeProperty, value);
    }

    public double VideoCardSecondaryLineHeight
    {
        get => (double)GetValue(VideoCardSecondaryLineHeightProperty);
        set => SetValue(VideoCardSecondaryLineHeightProperty, value);
    }

    public double VideoCardDetailFontSize
    {
        get => (double)GetValue(VideoCardDetailFontSizeProperty);
        set => SetValue(VideoCardDetailFontSizeProperty, value);
    }

    public double VideoCardDetailLineHeight
    {
        get => (double)GetValue(VideoCardDetailLineHeightProperty);
        set => SetValue(VideoCardDetailLineHeightProperty, value);
    }

    public double VideoCardStatusHeight
    {
        get => (double)GetValue(VideoCardStatusHeightProperty);
        set => SetValue(VideoCardStatusHeightProperty, value);
    }

    public Thickness VideoCardStatusPadding
    {
        get => (Thickness)GetValue(VideoCardStatusPaddingProperty);
        set => SetValue(VideoCardStatusPaddingProperty, value);
    }

    public Thickness VideoCardFormatMargin
    {
        get => (Thickness)GetValue(VideoCardFormatMarginProperty);
        set => SetValue(VideoCardFormatMarginProperty, value);
    }

    public double VideoCardCoverIconSize
    {
        get => (double)GetValue(VideoCardCoverIconSizeProperty);
        set => SetValue(VideoCardCoverIconSizeProperty, value);
    }

    private double GetVideoCardContentWidth()
    {
        if (FindVisualChild<ScrollViewer>(VideoListBox) is ScrollViewer scrollViewer
            && !double.IsNaN(scrollViewer.ViewportWidth)
            && !double.IsInfinity(scrollViewer.ViewportWidth)
            && scrollViewer.ViewportWidth > 0d)
        {
            return scrollViewer.ViewportWidth;
        }

        return Math.Max(1d, VideoListBox.ActualWidth - UiXVideoListFallbackContentInset);
    }

    private void UpdateVideoCardMetrics(double width)
    {
        if (hostMainWindow?.ViewModel.StatusOfIsUiXEnabled != true
            || double.IsNaN(width)
            || double.IsInfinity(width)
            || width <= 0d)
        {
            return;
        }

        double availableWidth = Math.Max(1d, width);
        (int columns, double cardWidth, double slotWidth) = CalculateVideoCardLayout(
            availableWidth,
            UiXVideoCardBaseWidth,
            UiXVideoCardMinimumWidth,
            UiXVideoCardMaximumWidth,
            UiXVideoCardHorizontalGap,
            videoCardColumnCount);
        double scale = cardWidth / UiXVideoCardBaseWidth;
        double padding = WindowSizing.RoundLayoutValue(12d * scale);
        double informationGap = WindowSizing.RoundLayoutValue(12d * scale);
        double fileNameLineHeight = WindowSizing.RoundLayoutValue(UiXVideoCardFileNameLineHeight * scale);
        double fileNameHeight = fileNameLineHeight * 2d;
        double secondaryLineHeight = WindowSizing.RoundLayoutValue(UiXVideoCardSecondaryLineHeight * scale);
        double detailLineHeight = WindowSizing.RoundLayoutValue(UiXVideoCardDetailLineHeight * scale);
        double statusHeight = WindowSizing.RoundLayoutValue(UiXVideoCardStatusHeight * scale);
        double coverWidth = CalculateVideoCardCoverWidth(
            cardWidth,
            padding,
            informationGap,
            UiXVideoCardInformationWidthRatio,
            UiXVideoCardMinimumInformationWidthRatio);
        double coverHeight = CalculateVideoCardCoverHeight(
            coverWidth,
            UiXVideoCardCoverAspectWidth,
            UiXVideoCardCoverAspectHeight);
        double informationHeight = fileNameHeight + secondaryLineHeight + detailLineHeight * 2d + statusHeight;
        double cardHeight = CalculateVideoCardHeight(padding, coverHeight, informationHeight);
        double gridWidth = WindowSizing.RoundLayoutValue(columns * slotWidth);
        double horizontalMargin = WindowSizing.RoundLayoutValue(UiXVideoCardHorizontalGap / 2d);
        double verticalMargin = WindowSizing.RoundLayoutValue(UiXVideoCardVerticalGap / 2d);

        VideoCardGridWidth = gridWidth;
        VideoCardColumnCount = columns;
        videoCardColumnCount = columns;
        VideoCardWidth = cardWidth;
        VideoCardHeight = cardHeight;
        VideoCardCoverWidth = coverWidth;
        VideoCardCoverHeight = coverHeight;
        VideoCardMargin = new Thickness(horizontalMargin, verticalMargin, horizontalMargin, verticalMargin);
        VideoCardPadding = new Thickness(padding);
        VideoCardInfoMargin = new Thickness(informationGap, 0d, 0d, 0d);
        VideoCardFileNameHeight = fileNameHeight;
        VideoCardFileNameFontSize = WindowSizing.RoundLayoutValue(UiXVideoCardFileNameFontSize * scale);
        VideoCardFileNameLineHeight = fileNameLineHeight;
        VideoCardSecondaryFontSize = WindowSizing.RoundLayoutValue(UiXVideoCardSecondaryFontSize * scale);
        VideoCardSecondaryLineHeight = secondaryLineHeight;
        VideoCardDetailFontSize = WindowSizing.RoundLayoutValue(UiXVideoCardDetailFontSize * scale);
        VideoCardDetailLineHeight = detailLineHeight;
        VideoCardStatusHeight = statusHeight;
        VideoCardStatusPadding = new Thickness(WindowSizing.RoundLayoutValue(7d * scale), 0d, WindowSizing.RoundLayoutValue(7d * scale), 0d);
        VideoCardFormatMargin = new Thickness(WindowSizing.RoundLayoutValue(8d * scale), 0d, 0d, 0d);
        VideoCardCoverIconSize = WindowSizing.RoundLayoutValue(24d * scale);
    }

    internal static int CalculateVideoCardColumns(double availableWidth, double baseCardWidth, double horizontalGap)
    {
        double preferredSlotWidth = Math.Max(1d, baseCardWidth + horizontalGap);
        return Math.Max(1, (int)Math.Floor(Math.Max(1d, availableWidth) / preferredSlotWidth));
    }

    internal static double CalculateVideoCardCoverWidth(
        double cardWidth,
        double padding,
        double informationGap,
        double preferredInformationWidthRatio,
        double minimumInformationWidthRatio)
    {
        double innerWidth = Math.Max(1d, cardWidth - Math.Max(0d, padding) * 2d - Math.Max(0d, informationGap));
        double informationWidthRatio = Math.Max(minimumInformationWidthRatio, preferredInformationWidthRatio);
        return Math.Max(1d, Math.Floor(innerWidth / (1d + Math.Max(0d, informationWidthRatio))));
    }

    internal static double CalculateVideoCardCoverHeight(double coverWidth, double aspectWidth, double aspectHeight)
    {
        return WindowSizing.RoundLayoutValue(Math.Max(1d, coverWidth) * Math.Max(1d, aspectHeight) / Math.Max(1d, aspectWidth));
    }

    internal static double CalculateVideoCardHeight(double padding, double coverHeight, double informationHeight)
    {
        return WindowSizing.RoundLayoutValue(Math.Max(coverHeight, informationHeight) + Math.Max(0d, padding) * 2d);
    }

    internal static (int Columns, double CardWidth, double SlotWidth) CalculateVideoCardLayout(
        double availableWidth,
        double baseCardWidth,
        double minimumCardWidth,
        double maximumCardWidth,
        double horizontalGap,
        int currentColumns = 0)
    {
        double normalizedWidth = Math.Max(1d, availableWidth);
        double targetSlotWidth = Math.Max(1d, baseCardWidth + horizontalGap);
        int candidateColumns = Math.Max(1, (int)Math.Round(
            normalizedWidth / targetSlotWidth,
            MidpointRounding.AwayFromZero));
        int columns = StabilizeVideoCardColumns(
            currentColumns,
            candidateColumns,
            normalizedWidth,
            targetSlotWidth,
            minimumCardWidth + horizontalGap,
            UiXVideoCardColumnHysteresis);
        double naturalCardWidth = Math.Max(1d, Math.Floor(normalizedWidth / columns) - horizontalGap);
        double maximumFittingCardWidth = Math.Max(1d, naturalCardWidth);
        double cardWidth = Math.Clamp(naturalCardWidth, minimumCardWidth, maximumCardWidth);
        cardWidth = Math.Min(cardWidth, maximumFittingCardWidth);
        double slotWidth = Math.Floor(normalizedWidth / columns * 100d) / 100d;
        return (
            columns,
            WindowSizing.RoundLayoutValue(cardWidth),
            slotWidth);
    }

    internal static int StabilizeVideoCardColumns(
        int currentColumns,
        int candidateColumns,
        double availableWidth,
        double targetSlotWidth,
        double minimumSlotWidth,
        double hysteresis)
    {
        double normalizedWidth = Math.Max(1d, availableWidth);
        int maximumFittingColumns = Math.Max(1, (int)Math.Floor(
            normalizedWidth / Math.Max(1d, minimumSlotWidth)));
        int normalizedCandidate = Math.Clamp(Math.Max(1, candidateColumns), 1, maximumFittingColumns);
        if (currentColumns <= 0)
        {
            return normalizedCandidate;
        }

        if (currentColumns > maximumFittingColumns)
        {
            return maximumFittingColumns;
        }

        if (currentColumns == normalizedCandidate)
        {
            return currentColumns;
        }

        double normalizedTargetSlotWidth = Math.Max(1d, targetSlotWidth);
        double normalizedHysteresis = Math.Max(0d, hysteresis);
        if (normalizedCandidate > currentColumns)
        {
            double growthThreshold = (currentColumns + 0.5d) * normalizedTargetSlotWidth + normalizedHysteresis;
            return normalizedWidth >= growthThreshold ? normalizedCandidate : currentColumns;
        }

        double shrinkThreshold = (currentColumns - 0.5d) * normalizedTargetSlotWidth - normalizedHysteresis;
        return normalizedWidth <= shrinkThreshold ? normalizedCandidate : currentColumns;
    }

    private async void ScreenRecordListWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            FinishVideoMarquee(false);
            ViewModel.ResetSelectionForPageNavigation();
            return;
        }

        if (IsLoaded && e.NewValue is true)
        {
            FinishVideoMarquee(false);
            ViewModel.ResetSelectionForPageNavigation();
            FocusVideoList();
            await RefreshVisibleVideoListAsync();
            UpdateVideoListEdgeFades();
        }
    }

    private async Task RefreshVisibleVideoListAsync()
    {
        Task refreshTask = visibleVideoRefreshTask ??= RefreshVisibleVideoListCoreAsync();
        try
        {
            await refreshTask;
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }
        finally
        {
            if (ReferenceEquals(visibleVideoRefreshTask, refreshTask))
            {
                visibleVideoRefreshTask = null;
            }
        }
    }

    private async Task RefreshVisibleVideoListCoreAsync()
    {
        await ViewModel.RefreshForDisplayAsync();
        ScheduleVisibleVideoLoading();
    }

    private void ScheduleVisibleVideoLoading(bool restartDelay = true)
    {
        if (restartDelay)
        {
            visibleVideoLoadTimer.Stop();
        }
        if (!visibleVideoLoadTimer.IsEnabled)
        {
            visibleVideoLoadTimer.Start();
        }
    }

    private void VisibleVideoLoadTimerTick(object? sender, EventArgs e)
    {
        visibleVideoLoadTimer.Stop();
        ViewModel.QueueVisibleVideoEnrichment(GetVisibleVideoItems());
    }

    private IEnumerable<RecordedVideoItem> GetVisibleVideoItems()
    {
        if (VideoListBox == null)
        {
            yield break;
        }

        foreach (ListBoxItem container in EnumerateVisualDescendants<ListBoxItem>(VideoListBox))
        {
            if (container.DataContext is not RecordedVideoItem item)
            {
                continue;
            }

            if (TryGetElementBounds(container, VideoListBox, out Rect bounds)
                && bounds.Bottom >= 0
                && bounds.Top <= VideoListBox.ActualHeight)
            {
                yield return item;
            }
        }
    }

    private void VideoListScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        ScheduleVisibleVideoLoading();
        UpdateVideoListEdgeFades(e.OriginalSource as ScrollViewer);
    }

    private void UpdateVideoListEdgeFades(ScrollViewer? scrollViewer = null)
    {
        scrollViewer ??= FindVisualChild<ScrollViewer>(VideoListBox);
        if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0.5d)
        {
            VideoListTopFade.Visibility = Visibility.Collapsed;
            VideoListBottomFade.Visibility = Visibility.Collapsed;
            return;
        }

        VideoListTopFade.Visibility = scrollViewer.VerticalOffset > 0.5d
            ? Visibility.Visible
            : Visibility.Collapsed;
        VideoListBottomFade.Visibility = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 0.5d
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void VideoListBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindVisualChild<ScrollViewer>(VideoListBox) is not ScrollViewer scrollViewer || e.Delta == 0)
        {
            return;
        }

        double offset = Math.Max(0d, scrollViewer.VerticalOffset - e.Delta * 0.25d);
        scrollViewer.ScrollToVerticalOffset(Math.Min(scrollViewer.ScrollableHeight, offset));
        if (isVideoMarqueeSelecting)
        {
            UpdateVideoMarquee(Mouse.GetPosition(VideoListBox));
        }
        e.Handled = true;
    }

    private async void VideoSelectionHostPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ListBoxItem? item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        bool isUiXEnabled = hostMainWindow?.ViewModel.StatusOfIsUiXEnabled == true;
        bool isScrollBar = FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(e.OriginalSource as DependencyObject) != null;
        bool isBlank = item == null
            && FindVisualParent<TextBlock>(e.OriginalSource as DependencyObject) == null
            && FindVisualParent<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject) == null;
        if (ShouldRefreshVideoListFromDoubleClick(isUiXEnabled, isBlank, isScrollBar, e.ClickCount)
            && !ViewModel.IsModalOpen)
        {
            e.Handled = true;
            await ViewModel.RefreshAsync();
            return;
        }
        if (e.ChangedButton != MouseButton.Left
            || ViewModel.IsModalOpen
            || isScrollBar
            || item != null && IsInteractiveElement(e.OriginalSource as DependencyObject, item))
        {
            return;
        }

        videoMarqueeCandidate = true;
        videoMarqueeStart = GetVideoMarqueeContentPoint(e.GetPosition(VideoListBox));
        videoMarqueePressedItem = item?.DataContext as RecordedVideoItem;
        videoMarqueeClickCount = e.ClickCount;
        if (item != null)
        {
            item.Focus();
            e.Handled = true;
        }
    }

    internal static bool ShouldRefreshVideoListFromDoubleClick(bool isUiXEnabled, bool isBlank, bool isScrollBar, int clickCount)
    {
        return isUiXEnabled && isBlank && !isScrollBar && clickCount >= 2;
    }

    private void VideoSelectionHostPreviewMouseMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(VideoListBox);
        if (isVideoMarqueeSelecting)
        {
            UpdateVideoMarquee(position);
            e.Handled = true;
            return;
        }

        if (!videoMarqueeCandidate)
        {
            return;
        }

        if (!CanStartVideoMarquee(videoMarqueePressedItem))
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            videoMarqueeCandidate = false;
            videoMarqueePressedItem = null;
            videoMarqueeClickCount = 0;
            return;
        }

        Point contentPosition = GetVideoMarqueeContentPoint(position);
        bool moved = Math.Abs(contentPosition.X - videoMarqueeStart.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(contentPosition.Y - videoMarqueeStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!moved)
        {
            return;
        }

        videoMarqueeCandidate = false;
        isVideoMarqueeSelecting = true;
        videoMarqueeItems.Clear();
        VideoSelectionRectangle.Visibility = Visibility.Visible;
        VideoListBox.CaptureMouse();
        UpdateVideoMarquee(position);
        e.Handled = true;
    }

    private void VideoSelectionHostPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isVideoMarqueeSelecting)
        {
            if (videoMarqueeCandidate)
            {
                if (videoMarqueePressedItem != null)
                {
                    bool toggleSelection = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                    bool selectRange = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                    bool wasMultiSelectMode = ViewModel.IsMultiSelectMode;
                    if (wasMultiSelectMode)
                    {
                        ViewModel.SelectMultipleItem(videoMarqueePressedItem, !selectRange, selectRange);
                    }
                    else if (toggleSelection || selectRange)
                    {
                        ViewModel.SelectMultipleItem(videoMarqueePressedItem, toggleSelection, selectRange);
                    }
                    else
                    {
                        ViewModel.SelectRegularItem(videoMarqueePressedItem);
                    }
                    if (ShouldOpenVideoFromClick(wasMultiSelectMode, toggleSelection, selectRange, videoMarqueeClickCount))
                    {
                        ViewModel.OpenVideoCommand.Execute(videoMarqueePressedItem);
                    }
                }
                else if (!ViewModel.IsMultiSelectMode)
                {
                    ViewModel.ClearRegularSelection();
                }
                e.Handled = true;
            }
            videoMarqueeCandidate = false;
            videoMarqueePressedItem = null;
            videoMarqueeClickCount = 0;
            return;
        }

        FinishVideoMarquee(true);
        e.Handled = true;
    }

    private void VideoSelectionHostLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (isVideoMarqueeSelecting)
        {
            FinishVideoMarquee(false);
            return;
        }

        videoMarqueeCandidate = false;
        videoMarqueePressedItem = null;
        videoMarqueeClickCount = 0;
    }

    private void UpdateVideoMarquee(Point position)
    {
        videoMarqueePointer = position;
        Rect contentSelection = CreateSelectionRect(videoMarqueeStart, GetVideoMarqueeContentPoint(position));
        Rect viewportSelection = ProjectVideoMarqueeToViewport(contentSelection);
        Canvas.SetLeft(VideoSelectionRectangle, WindowSizing.RoundLayoutValue(viewportSelection.Left));
        Canvas.SetTop(VideoSelectionRectangle, WindowSizing.RoundLayoutValue(viewportSelection.Top));
        VideoSelectionRectangle.Width = WindowSizing.RoundLayoutValue(viewportSelection.Width);
        VideoSelectionRectangle.Height = WindowSizing.RoundLayoutValue(viewportSelection.Height);
        AccumulateVideoMarqueeItems(contentSelection);
        UpdateVideoMarqueeAutoScroll();
    }

    private void AccumulateVideoMarqueeItems(Rect selection)
    {
        foreach (ListBoxItem item in EnumerateVisualDescendants<ListBoxItem>(VideoListBox))
        {
            if (TryGetElementBounds(item, VideoListBox, out Rect bounds)
                && selection.IntersectsWith(ProjectVideoMarqueeToContent(bounds))
                && item.DataContext is RecordedVideoItem video)
            {
                videoMarqueeItems.Add(video);
            }
        }
    }

    private void UpdateVideoMarqueeAutoScroll()
    {
        double delta = MarqueeAutoScroll.GetDelta(videoMarqueePointer.Y, VideoListBox.ActualHeight);
        if (Math.Abs(delta) > 0d)
        {
            if (!videoMarqueeAutoScrollTimer.IsEnabled)
            {
                videoMarqueeAutoScrollTimer.Start();
            }
        }
        else
        {
            videoMarqueeAutoScrollTimer.Stop();
        }
    }

    private void VideoMarqueeAutoScrollTimerTick(object? sender, EventArgs e)
    {
        if (!isVideoMarqueeSelecting
            || FindVisualChild<ScrollViewer>(VideoListBox) is not ScrollViewer scrollViewer)
        {
            videoMarqueeAutoScrollTimer.Stop();
            return;
        }

        videoMarqueePointer = Mouse.GetPosition(VideoListBox);
        double delta = MarqueeAutoScroll.GetDelta(videoMarqueePointer.Y, VideoListBox.ActualHeight);
        double targetOffset = Math.Clamp(scrollViewer.VerticalOffset + delta, 0d, scrollViewer.ScrollableHeight);
        if (Math.Abs(targetOffset - scrollViewer.VerticalOffset) < 0.5d)
        {
            videoMarqueeAutoScrollTimer.Stop();
            return;
        }

        scrollViewer.ScrollToVerticalOffset(targetOffset);
        UpdateVideoMarquee(videoMarqueePointer);
    }

    private void FinishVideoMarquee(bool commit)
    {
        Rect selection = CreateSelectionRect(videoMarqueeStart, GetVideoMarqueeContentPoint(Mouse.GetPosition(VideoListBox)));
        AccumulateVideoMarqueeItems(selection);
        isVideoMarqueeSelecting = false;
        videoMarqueeCandidate = false;
        videoMarqueePressedItem = null;
        videoMarqueeClickCount = 0;
        videoMarqueeAutoScrollTimer.Stop();
        VideoSelectionRectangle.Visibility = Visibility.Collapsed;
        if (VideoListBox.IsMouseCaptured)
        {
            VideoListBox.ReleaseMouseCapture();
        }

        if (!commit || selection.Width < 1d || selection.Height < 1d)
        {
            videoMarqueeItems.Clear();
            return;
        }

        ViewModel.SelectItems(videoMarqueeItems);
        videoMarqueeItems.Clear();
    }

    private Point GetVideoMarqueeContentPoint(Point viewportPoint)
    {
        if (FindVisualChild<ScrollViewer>(VideoListBox) is not ScrollViewer scrollViewer)
        {
            return viewportPoint;
        }

        return new Point(viewportPoint.X + scrollViewer.HorizontalOffset, viewportPoint.Y + scrollViewer.VerticalOffset);
    }

    private Rect ProjectVideoMarqueeToViewport(Rect contentRect)
    {
        if (FindVisualChild<ScrollViewer>(VideoListBox) is not ScrollViewer scrollViewer)
        {
            return contentRect;
        }

        return new Rect(
            contentRect.X - scrollViewer.HorizontalOffset,
            contentRect.Y - scrollViewer.VerticalOffset,
            contentRect.Width,
            contentRect.Height);
    }

    private Rect ProjectVideoMarqueeToContent(Rect viewportRect)
    {
        if (FindVisualChild<ScrollViewer>(VideoListBox) is not ScrollViewer scrollViewer)
        {
            return viewportRect;
        }

        return new Rect(
            viewportRect.X + scrollViewer.HorizontalOffset,
            viewportRect.Y + scrollViewer.VerticalOffset,
            viewportRect.Width,
            viewportRect.Height);
    }

    private void VideoSelectionHostPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is not ListBoxItem item
            || item.DataContext is not RecordedVideoItem video)
        {
            return;
        }

        if (!video.CanSelect)
        {
            e.Handled = true;
            return;
        }

        if (ViewModel.IsMultiSelectMode)
        {
            if (!video.IsSelected)
            {
                ViewModel.SelectMultipleItem(video, false, false);
            }
        }
        else
        {
            ViewModel.SelectRegularItem(video);
        }
        item.Focus();
        if (FindVisualChildByName(item, "VideoCardShell") is FrameworkElement { ContextMenu: not null } card)
        {
            PopulateVideoCardContextMenu(card);
            card.ContextMenu.PlacementTarget = card;
            card.ContextMenu.Placement = PlacementMode.MousePoint;
            card.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void VideoCardContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            PopulateVideoCardContextMenu(card);
        }
    }

    private void PopulateVideoCardContextMenu(FrameworkElement card)
    {
        if (card.ContextMenu is not { } contextMenu)
        {
            return;
        }

        foreach (object item in contextMenu.Items.Cast<object>().Where(item => item is MenuItem { Tag: ExtensionVideoActionTag }).ToArray())
        {
            contextMenu.Items.Remove(item);
        }

        IReadOnlyList<ExtensionVideoFileInfo> files = ViewModel.GetSelectedFiles();
        List<ExtensionVideoActionMenuEntry> actions = [];
        foreach (IExtensionVideoAction action in ExtensionHostRuntime.GetOverrides<IExtensionVideoAction>(ExtensionContractNames.VideoListActions))
        {
            try
            {
                actions.Add(new ExtensionVideoActionMenuEntry(
                    action,
                    action.Id,
                    action.Label,
                    action.Order,
                    action.CanExecute(files)));
            }
            catch (Exception exception)
            {
                AppSessionLogger.Event("error", "extension", "video_action_state_failed", exception.Message, new
                {
                    implementation = action.GetType().FullName,
                    type = exception.GetType().FullName,
                });
            }
        }
        int insertionIndex = contextMenu.Items.Cast<object>().TakeWhile(item => item is not Separator).Count();
        foreach (ExtensionVideoActionMenuEntry entry in actions
                     .OrderBy(item => item.Order)
                     .ThenBy(item => item.Label, StringComparer.CurrentCulture))
        {
            MenuItem menuItem = new()
            {
                Header = entry.Label,
                IsEnabled = entry.IsEnabled,
                Tag = new ExtensionVideoActionTag(entry.Id),
                FocusVisualStyle = null,
            };
            menuItem.Click += async (_, _) =>
            {
                menuItem.IsEnabled = false;
                try
                {
                    await entry.Action.ExecuteAsync(files, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    AppSessionLogger.Event("error", "extension", "video_action_failed", exception.Message, new
                    {
                        entry.Id,
                        type = exception.GetType().FullName,
                    });
                    Toast.Warning("OperationFailedWithLabel".Tr(entry.Label, exception.Message));
                }
            };
            contextMenu.Items.Insert(insertionIndex++, menuItem);
        }
    }

    private sealed record ExtensionVideoActionMenuEntry(
        IExtensionVideoAction Action,
        string Id,
        string Label,
        int Order,
        bool IsEnabled);

    private sealed record ExtensionVideoActionTag(string Id);

    internal static bool ShouldOpenVideoFromClick(bool isMultiSelectMode, bool toggleSelection, bool selectRange, int clickCount)
    {
        return !isMultiSelectMode && !toggleSelection && !selectRange && clickCount >= 2;
    }

    internal static bool CanStartVideoMarquee(RecordedVideoItem? pressedItem)
    {
        return pressedItem == null;
    }

    private static Rect CreateSelectionRect(Point start, Point end)
    {
        return new Rect(
            new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
            new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));
    }

    private void VideoCardLoaded(object sender, RoutedEventArgs e)
    {
        ScheduleVisibleVideoLoading(restartDelay: false);
    }

    private void SelectionCheckBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecordedVideoItem item } || !item.CanSelect)
        {
            return;
        }

        e.Handled = true;
        ViewModel.SelectMultipleItem(item, true, (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
    }

    private void ScreenRecordListWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel.IsMultiSelectMode)
        {
            ViewModel.CancelMultiSelectCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ViewModel.IsModalOpen
            || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox)
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (IsVideoListKeyboardNavigationKey(e.Key) && modifiers == ModifierKeys.None)
        {
            int offset = hostMainWindow?.ViewModel.StatusOfIsUiXEnabled == true
                ? GetVideoListKeyboardNavigationOffset(e.Key, VideoCardColumnCount)
                : GetVideoListKeyboardNavigationOffset(e.Key);
            RecordedVideoItem? item = ViewModel.GetAdjacentVisibleVideo(offset);
            if (item != null)
            {
                ViewModel.SelectRegularItem(item);
                BringVideoListItemIntoView(item);
                FocusVideoList();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            object? parameter = ViewModel.RegularSelectedItem;
            if (ViewModel.OpenVideoCommand.CanExecute(parameter))
            {
                ViewModel.OpenVideoCommand.Execute(parameter);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.F5)
        {
            if (ViewModel.RefreshCommand.CanExecute(null))
            {
                ViewModel.RefreshCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Delete)
        {
            object? parameter = ViewModel.RegularSelectedItem;
            if (ViewModel.DeleteContextCommand.CanExecute(parameter))
            {
                ViewModel.DeleteContextCommand.Execute(parameter);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.S && modifiers is ModifierKeys.Shift or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                object? parameter = ViewModel.RegularSelectedItem;
                if (ViewModel.CopyContextCommand.CanExecute(parameter))
                {
                    ViewModel.CopyContextCommand.Execute(parameter);
                    e.Handled = true;
                }
            }
            else
            {
                object? parameter = ViewModel.RegularSelectedItem;
                if (ViewModel.MoveContextCommand.CanExecute(parameter))
                {
                    ViewModel.MoveContextCommand.Execute(parameter);
                    e.Handled = true;
                }
            }
            return;
        }

        if ((modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key == Key.A)
        {
            ViewModel.SelectAllCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Z)
        {
            ViewModel.UndoSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            ViewModel.RedoSelection();
            e.Handled = true;
        }
    }

    internal static bool IsVideoListKeyboardNavigationKey(Key key)
    {
        return key is Key.Up or Key.Down or Key.Left or Key.Right or Key.W or Key.A or Key.S or Key.D;
    }

    internal static int GetVideoListKeyboardNavigationOffset(Key key)
    {
        return key is Key.Down or Key.Right or Key.S or Key.D ? 1 : -1;
    }

    internal static int GetVideoListKeyboardNavigationOffset(Key key, int columnCount)
    {
        return key switch
        {
            Key.Up or Key.W => -Math.Max(1, columnCount),
            Key.Down or Key.S => Math.Max(1, columnCount),
            Key.Left or Key.A => -1,
            Key.Right or Key.D => 1,
            _ => 0,
        };
    }

    private void BringVideoListItemIntoView(RecordedVideoItem item)
    {
        if (VideoListBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            Rect bounds = container.TransformToAncestor(VideoListBox)
                .TransformBounds(new Rect(new Point(0, 0), container.RenderSize));
            if (bounds.Bottom >= 0 && bounds.Top <= VideoListBox.ActualHeight)
            {
                return;
            }
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (ReferenceEquals(ViewModel.RegularSelectedItem, item))
            {
                VideoListBox.ScrollIntoView(item);
            }
        });
    }

    private void FocusVideoList()
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsVisible && !ViewModel.IsModalOpen)
            {
                Keyboard.Focus(VideoListBox);
            }
        });
    }

    private static bool IsInteractiveElement(DependencyObject? source, DependencyObject? boundary)
    {
        while (source != null && !ReferenceEquals(source, boundary))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Selector)
            {
                return true;
            }

            source = GetElementParent(source);
        }

        return false;
    }

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T match)
            {
                return match;
            }
            source = GetElementParent(source);
        }
        return null;
    }

    private static DependencyObject? GetElementParent(DependencyObject source)
    {
        if (source is ContentElement contentElement)
        {
            return System.Windows.ContentOperations.GetParent(contentElement)
                ?? (contentElement as FrameworkContentElement)?.Parent;
        }
        return source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(source)
            : LogicalTreeHelper.GetParent(source);
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is not System.Windows.Media.Visual and not System.Windows.Media.Media3D.Visual3D)
        {
            yield break;
        }

        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }
            foreach (T nested in EnumerateVisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static bool TryGetElementBounds(FrameworkElement element, System.Windows.Media.Visual relativeTo, out Rect bounds)
    {
        try
        {
            bounds = element.TransformToVisual(relativeTo).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return true;
        }
        catch (InvalidOperationException)
        {
            bounds = Rect.Empty;
            return false;
        }
    }

    private static FrameworkElement? FindVisualChildByName(DependencyObject parent, string name)
    {
        int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            FrameworkElement? match = FindVisualChildByName(child, name);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            T? nested = FindVisualChild<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private void VideoListModalOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) && ViewModel.IsIdle)
        {
            ViewModel.CloseModalCommand.Execute(null);
            e.Handled = true;
        }
    }
}

internal sealed class ThumbnailImageConverter : IValueConverter
{
    public static ThumbnailImageConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return DependencyProperty.UnsetValue;
        }

        try
        {
            return LoadImage(path);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    internal static System.Windows.Media.ImageSource LoadImage(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        System.Windows.Media.Imaging.BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 320;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public partial class ScreenRecordListViewModel : ObservableObject, IExtensionVideoSelectionProvider
{
    private static readonly EnumerationOptions RecursiveEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static string UnknownStreamerText => GetResourceText("CommonUnknown", "Unknown");
    private static string UnknownResolutionText => GetResourceText("CommonUnknown", "Unknown");
    private static string UnknownBitrateText => GetResourceText("CommonUnknown", "Unknown");

    private readonly ObservableCollection<RecordedVideoItem> videos = [];
    internal const int SelectionHistoryLimit = 200;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> thumbnailExtractionTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<RecordedVideoItem> videoEnrichmentQueue = new();
    private readonly object videoEnrichmentLock = new();
    private readonly HashSet<string> queuedVideoEnrichmentPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<SelectionSnapshot> selectionUndoStack = [];
    private readonly Stack<SelectionSnapshot> selectionRedoStack = [];
    private CancellationTokenSource? videoLoadCancellationTokenSource;
    private CancellationTokenSource? videoEnrichmentCancellationTokenSource;
    private readonly object refreshTaskSync = new();
    private Task? refreshTask;
    private bool refreshAgainRequested;
    private bool forceRefreshRequested;
    private RecordedVideoItem? lastSelectedItem;
    private bool isRestoringSelection;
    private bool isApplyingSelectionChange;
    private int videoEnrichmentWorkerRunning;
    private int videoViewRefreshPending;
    private readonly Dictionary<string, FileSystemWatcher> directoryWatchers = new(StringComparer.OrdinalIgnoreCase);
    private string[] watchedRoots = [];
    private int directorySnapshotDirty = 1;
    private int automaticRefreshPending;
    private int automaticRefreshDeferred;
    private int automaticRefreshSuppressionDepth;
    private bool isMonitoring;
    private bool isReloadingLocalizedOptions;

    public ICollectionView Videos { get; }
    private string allStreamerOption = string.Empty;

    public ObservableCollection<string> StreamerOptions { get; } = [];
    public ObservableCollection<string> TimeRangeOptions { get; } = [];
    public event EventHandler? VisibleItemsChanged;

    public ScreenRecordListViewModel()
    {
        Videos = CollectionViewSource.GetDefaultView(videos);
        Videos.Filter = FilterVideo;
        ReloadLocalizedOptions();
    }

    internal void StartMonitoring()
    {
        if (isMonitoring)
        {
            return;
        }

        isMonitoring = true;
        Locale.CultureChanged += OnCultureChanged;
        ReloadLocalizedState();
        MediaOperationRegistry.OperationsChanged += MediaOperationRegistryOperationsChanged;
        ConfigureDirectoryWatchers(MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true));
    }

    internal void StopMonitoring()
    {
        if (!isMonitoring)
        {
            return;
        }

        isMonitoring = false;
        Locale.CultureChanged -= OnCultureChanged;
        MediaOperationRegistry.OperationsChanged -= MediaOperationRegistryOperationsChanged;
        foreach (FileSystemWatcher watcher in directoryWatchers.Values)
        {
            watcher.Dispose();
        }
        directoryWatchers.Clear();
        watchedRoots = [];
        Interlocked.Exchange(ref directorySnapshotDirty, 1);
    }

    private void ConfigureDirectoryWatchers(IEnumerable<string> roots)
    {
        string[] normalizedRoots = roots
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (watchedRoots.SequenceEqual(normalizedRoots, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (FileSystemWatcher watcher in directoryWatchers.Values)
        {
            watcher.Dispose();
        }
        directoryWatchers.Clear();
        watchedRoots = normalizedRoots;
        Interlocked.Exchange(ref directorySnapshotDirty, 1);
        if (!isMonitoring)
        {
            return;
        }

        foreach (string root in normalizedRoots)
        {
            try
            {
                FileSystemWatcher watcher = new(root)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                };
                watcher.Changed += DirectoryWatcherChanged;
                watcher.Created += DirectoryWatcherChanged;
                watcher.Deleted += DirectoryWatcherChanged;
                watcher.Renamed += DirectoryWatcherChanged;
                watcher.Error += DirectoryWatcherError;
                watcher.EnableRaisingEvents = true;
                directoryWatchers[root] = watcher;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                AppSessionLogger.WriteException(e);
            }
        }
    }

    private void DirectoryWatcherChanged(object sender, FileSystemEventArgs e)
    {
        if (!ShouldQueueDirectoryRefresh(e.FullPath, e.ChangeType, MediaOperationRegistry.IsPathProtected(e.FullPath)))
        {
            return;
        }
        Interlocked.Exchange(ref directorySnapshotDirty, 1);
        QueueAutomaticRefresh();
    }

    internal static bool ShouldQueueDirectoryRefresh(
        string path,
        WatcherChangeTypes changeType,
        bool isProtected)
    {
        return !isProtected
            && changeType is WatcherChangeTypes.Created or WatcherChangeTypes.Deleted or WatcherChangeTypes.Renamed
            && MediaFileCatalog.IsMediaPath(path)
            && !MediaFileCatalog.IsApplicationTemporaryPath(path);
    }

    private void DirectoryWatcherError(object sender, ErrorEventArgs e)
    {
        Interlocked.Exchange(ref directorySnapshotDirty, 1);
        QueueAutomaticRefresh();
        if (e.GetException() is Exception error)
        {
            AppSessionLogger.WriteException(error);
        }
    }

    private void MediaOperationRegistryOperationsChanged(object? sender, MediaOperationsChangedEventArgs e)
    {
        if (!e.IsActive)
        {
            Interlocked.Exchange(ref directorySnapshotDirty, 1);
        }
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(() =>
        {
            foreach (RecordedVideoItem item in videos)
            {
                UpdateOperationState(item);
            }
            VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
            if (!e.IsActive)
            {
                QueueAutomaticRefresh();
            }
        });
    }

    private void QueueAutomaticRefresh()
    {
        Interlocked.Exchange(ref automaticRefreshDeferred, 1);
        if (Volatile.Read(ref automaticRefreshSuppressionDepth) != 0
            || Interlocked.Exchange(ref automaticRefreshPending, 1) != 0)
        {
            return;
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref automaticRefreshPending, 0);
            return;
        }

        _ = dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(180);
                if (Volatile.Read(ref automaticRefreshSuppressionDepth) != 0)
                {
                    return;
                }

                Interlocked.Exchange(ref automaticRefreshDeferred, 0);
                if (isMonitoring)
                {
                    await RefreshForDisplayAsync();
                }
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
            }
            finally
            {
                Interlocked.Exchange(ref automaticRefreshPending, 0);
                if (isMonitoring
                    && Volatile.Read(ref automaticRefreshSuppressionDepth) == 0
                    && Volatile.Read(ref automaticRefreshDeferred) != 0)
                {
                    QueueAutomaticRefresh();
                }
            }
        });
    }

    private void BeginAutomaticRefreshSuppression()
    {
        Interlocked.Increment(ref automaticRefreshSuppressionDepth);
    }

    private void EndAutomaticRefreshSuppression()
    {
        if (Interlocked.Decrement(ref automaticRefreshSuppressionDepth) == 0
            && Volatile.Read(ref automaticRefreshDeferred) != 0)
        {
            QueueAutomaticRefresh();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionText))]
    [NotifyPropertyChangedFor(nameof(SortDirectionToolTip))]
    private bool isSortDescending = true;

    public string SortDirectionText => IsSortDescending
        ? GetResourceText("SortDescending", "Descending")
        : GetResourceText("SortAscending", "Ascending");

    public string SortDirectionToolTip => IsSortDescending
        ? "SortDescendingDescription".Tr()
        : "SortAscendingDescription".Tr();

    [ObservableProperty]
    private string selectedStreamer = string.Empty;

    partial void OnSelectedStreamerChanged(string value)
    {
        if (!isReloadingLocalizedOptions)
        {
            ApplyFilters();
        }
    }

    [ObservableProperty]
    private int selectedTimeRangeIndex;

    [ObservableProperty]
    private string? selectedTimeRange;

    partial void OnSelectedTimeRangeIndexChanged(int value)
    {
        int next = Math.Clamp(value, 0, Math.Max(0, TimeRangeOptions.Count - 1));
        if (next != value)
        {
            SelectedTimeRangeIndex = next;
            return;
        }

        ApplyFilters();
    }

    partial void OnSelectedTimeRangeChanged(string? value)
    {
        int index = string.IsNullOrEmpty(value) ? 0 : TimeRangeOptions.IndexOf(value);
        SelectedTimeRangeIndex = Math.Max(0, index);
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            ReloadLocalizedState();
            return;
        }

        dispatcher.Invoke(ReloadLocalizedState);
    }

    private void ReloadLocalizedState()
    {
        bool selectedAll = string.IsNullOrWhiteSpace(SelectedStreamer)
            || string.Equals(SelectedStreamer, allStreamerOption, StringComparison.Ordinal);
        ReloadLocalizedOptions();
        if (selectedAll)
        {
            SelectedStreamer = allStreamerOption;
        }
        foreach (RecordedVideoItem item in videos)
        {
            item.RefreshLocalizedText();
        }
        OnPropertyChanged(nameof(SortDirectionText));
        OnPropertyChanged(nameof(SortDirectionToolTip));
        RefreshSelectionSummary();
        ApplyFilters();
    }

    private void ReloadLocalizedOptions()
    {
        isReloadingLocalizedOptions = true;
        try
        {
            allStreamerOption = GetResourceText("VideoAllStreamers", "All streamers");
            TimeRangeOptions.Clear();
            foreach ((string key, string fallback) in new[]
            {
                ("TimeRangeAll", "All time"),
                ("TimeRangeLast24Hours", "Last 24 hours"),
                ("TimeRangeLastWeek", "Last week"),
                ("TimeRangeLastMonth", "Last month"),
                ("TimeRangeLastThreeMonths", "Last 3 months"),
                ("TimeRangeLastYear", "Last year"),
            })
            {
                TimeRangeOptions.Add(GetResourceText(key, fallback));
            }

            UpdateStreamerOptions();
            SelectedTimeRangeIndex = Math.Clamp(SelectedTimeRangeIndex, 0, Math.Max(0, TimeRangeOptions.Count - 1));
            SelectedTimeRange = TimeRangeOptions.Count == 0 ? null : TimeRangeOptions[SelectedTimeRangeIndex];
        }
        finally
        {
            isReloadingLocalizedOptions = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedVideoSummary))]
    [NotifyPropertyChangedFor(nameof(HasSelectedVideos))]
    private bool isMultiSelectMode;

    [ObservableProperty]
    private RecordedVideoItem? regularSelectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    private bool isSplitPanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    private bool isMergePanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isOperating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isUserTranscoding;

    [ObservableProperty]
    private string operationProgressText = string.Empty;

    [ObservableProperty]
    private string splitDurationValue = "30";

    [ObservableProperty]
    private int splitDurationUnitIndex;

    [ObservableProperty]
    private string splitTargetFileName = string.Empty;

    [ObservableProperty]
    private string mergeWarningText = string.Empty;

    [ObservableProperty]
    private double mergeProgressValue;

    [ObservableProperty]
    private bool isMergeProgressIndeterminate;

    private RecordedVideoItem? splitTargetItem;

    public int SelectedVideoCount => GetVisibleVideos().Count(video => video.IsSelected);

    public bool HasVisibleVideos => !Videos.IsEmpty;

    public bool HasSelectedVideos => SelectedVideoCount > 0;

    public bool CanMergeSelectedVideos => SelectedVideoCount >= 2;

    public string SelectedVideoSummary => FormatResourceText("VideoSelectedCount", "{0} selected", SelectedVideoCount);

    public bool IsModalOpen => IsSplitPanelOpen || IsMergePanelOpen;

    public bool IsIdle => !IsOperating || IsUserTranscoding;

    public IReadOnlyList<ExtensionVideoFileInfo> GetSelectedFiles()
    {
        IEnumerable<RecordedVideoItem> selected = IsMultiSelectMode
            ? GetVisibleVideos()
                .Where(item => item.CanSelect && item.IsSelected)
                .OrderBy(item => item.UserSelectionOrder > 0 ? item.UserSelectionOrder : int.MaxValue)
            : RegularSelectedItem is { CanSelect: true } item
                ? [item]
                : [];
        List<ExtensionVideoFileInfo> files = [];
        foreach (RecordedVideoItem video in selected)
        {
            try
            {
                FileInfo file = new(video.FullPath);
                if (!file.Exists)
                {
                    continue;
                }
                VideoRecordingMetadata metadata = LoadMetadata(file);
                files.Add(new ExtensionVideoFileInfo(
                    file.FullName,
                    metadata.RoomUrl,
                    string.IsNullOrWhiteSpace(metadata.NickName) ? video.NickName : metadata.NickName,
                    metadata.Platform,
                    string.IsNullOrWhiteSpace(metadata.Title) ? video.Title : metadata.Title,
                    file.Length,
                    metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt : video.CreatedAt,
                    file.LastWriteTimeUtc));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
            }
        }
        return files;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await RequestVideoRefreshAsync(forceRefresh: true);
    }

    internal async Task RefreshForDisplayAsync()
    {
        await RequestVideoRefreshAsync(forceRefresh: false);
    }

    private Task RequestVideoRefreshAsync(bool forceRefresh)
    {
        lock (refreshTaskSync)
        {
            if (refreshTask != null)
            {
                refreshAgainRequested = true;
                forceRefreshRequested |= forceRefresh;
                return refreshTask;
            }

            forceRefreshRequested = forceRefresh;
            refreshAgainRequested = false;
            refreshTask = RunVideoRefreshLoopAsync();
            return refreshTask;
        }
    }

    private async Task RunVideoRefreshLoopAsync()
    {
        await Task.Yield();
        try
        {
            while (true)
            {
                bool forceRefresh;
                lock (refreshTaskSync)
                {
                    forceRefresh = forceRefreshRequested;
                    forceRefreshRequested = false;
                    refreshAgainRequested = false;
                }

                await LoadVideosFromFoldersAsync(
                    MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true),
                    reuseExistingItems: !forceRefresh);

                lock (refreshTaskSync)
                {
                    if (!refreshAgainRequested)
                    {
                        refreshTask = null;
                        return;
                    }
                }
            }
        }
        catch (Exception e)
        {
            lock (refreshTaskSync)
            {
                refreshTask = null;
            }
            AppSessionLogger.WriteException(e);
        }
    }

    [RelayCommand]
    private void ToggleSort()
    {
        IsSortDescending = !IsSortDescending;
        ApplySort();
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        if (IsOperating)
        {
            return;
        }

        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
            Title = "SelectVideoImportFolder".Tr(),
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok || !Directory.Exists(dialog.FileName))
        {
            return;
        }

        string sourceFolder = dialog.FileName;
        if (!TryChooseImportDestination(out string root))
        {
            return;
        }
        string[] configuredRoots = MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true);
        if (configuredRoots.Any(configuredRoot =>
                IsSameOrAncestorDirectory(sourceFolder, configuredRoot)
                || IsSameOrAncestorDirectory(configuredRoot, sourceFolder)))
        {
            Toast.Warning("VideoImportManagedFolderConflict".Tr());
            return;
        }

        IsOperating = true;
        OperationProgressText = "ImportingVideos".Tr();
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            (int succeeded, int failed) = await Task.Run(() => ImportVideos(sourceFolder, root));
            if (succeeded > 0)
            {
                Toast.Success("ImportedVideos".Tr(succeeded));
            }
            if (failed > 0)
            {
                Toast.Warning("VideoImportFailures".Tr(failed));
            }
            await RefreshForDisplayAsync();
        }
        finally
        {
            IsOperating = false;
            OperationProgressText = string.Empty;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    [RelayCommand]
    private async Task OpenSaveFolderAsync()
    {
        string[] roots = MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true);
        if (!TryChooseConfiguredFolder(roots, "SelectSaveFolderToOpen".Tr(), out string root))
        {
            return;
        }

        await Launcher.LaunchFolderPathAsync(root);
    }

    private static bool TryChooseImportDestination(out string root)
    {
        string[] roots = MediaFileCatalog.GetConfiguredSaveFolders(createDirectories: true);
        return TryChooseConfiguredFolder(roots, "SelectVideoImportTarget".Tr(), out root);
    }

    private static bool TryChooseConfiguredFolder(string[] roots, string title, out string root)
    {
        if (roots.Length == 0)
        {
            root = string.Empty;
            Toast.Warning("NoAvailableSaveFolder".Tr());
            return false;
        }
        if (roots.Length == 1)
        {
            root = roots[0];
            return true;
        }

        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
            Title = title,
            InitialDirectory = roots[0],
        };
        if (dialog.ShowDialog() == CommonFileDialogResult.Ok
            && roots.Any(configuredRoot => IsSameOrAncestorDirectory(configuredRoot, dialog.FileName)))
        {
            root = dialog.FileName;
            return true;
        }

        root = string.Empty;
        Toast.Warning("FolderMustBeInsideConfiguredSaveFolder".Tr());
        return false;
    }

    [RelayCommand]
    private void BeginMultiSelect()
    {
        if (!GetVisibleVideos().Any(item => item.CanSelect))
        {
            return;
        }

        ApplySelectionChange(() => IsMultiSelectMode = true);
    }

    [RelayCommand]
    private void CancelMultiSelect()
    {
        ApplySelectionChange(() =>
        {
            foreach (RecordedVideoItem item in videos)
            {
                SetItemSelection(item, false);
            }
            IsMultiSelectMode = false;
            RegularSelectedItem = null;
            lastSelectedItem = null;
        });
    }

    [RelayCommand]
    private void SelectAll()
    {
        ApplySelectionChange(() =>
        {
            RecordedVideoItem[] visible = GetVisibleVideos();
            RecordedVideoItem[] selectable = visible.Where(item => item.CanSelect).ToArray();
            if (selectable.Length == 0)
            {
                foreach (RecordedVideoItem item in visible)
                {
                    SetItemSelection(item, false);
                }
                IsMultiSelectMode = false;
                lastSelectedItem = null;
                return;
            }

            IsMultiSelectMode = true;
            foreach (RecordedVideoItem item in visible)
            {
                SetItemSelection(item, false);
            }
            foreach (RecordedVideoItem item in selectable)
            {
                SetItemSelection(item, true);
            }
            lastSelectedItem = selectable[^1];
        });
    }

    [RelayCommand]
    private void InvertSelection()
    {
        ApplySelectionChange(() =>
        {
            RecordedVideoItem[] visible = GetVisibleVideos();
            RecordedVideoItem[] selectable = visible.Where(item => item.CanSelect).ToArray();
            if (selectable.Length == 0)
            {
                foreach (RecordedVideoItem item in visible)
                {
                    SetItemSelection(item, false);
                }
                IsMultiSelectMode = false;
                lastSelectedItem = null;
                return;
            }

            foreach (RecordedVideoItem item in visible)
            {
                SetItemSelection(item, item.CanSelect && !item.IsSelected);
            }
            lastSelectedItem = GetLastSelectedItem();
        });
    }

    internal void SelectRegularItem(RecordedVideoItem item)
    {
        RegularSelectedItem = item.CanSelect ? item : null;
    }

    internal RecordedVideoItem? GetAdjacentVisibleVideo(int offset)
    {
        RecordedVideoItem[] visible = GetVisibleVideos()
            .Where(item => item.CanSelect)
            .ToArray();
        if (visible.Length == 0)
        {
            return null;
        }

        int currentIndex = RegularSelectedItem == null ? -1 : Array.IndexOf(visible, RegularSelectedItem);
        if (currentIndex < 0)
        {
            return offset < 0 ? visible[^1] : visible[0];
        }

        int nextIndex = Math.Clamp(currentIndex + Math.Sign(offset), 0, visible.Length - 1);
        return visible[nextIndex];
    }

    internal void ClearRegularSelection()
    {
        RegularSelectedItem = null;
    }

    internal void ResetSelectionForPageNavigation()
    {
        isRestoringSelection = true;
        try
        {
            foreach (RecordedVideoItem item in videos)
            {
                SetItemSelection(item, false);
            }
        }
        finally
        {
            isRestoringSelection = false;
        }

        IsMultiSelectMode = false;
        lastSelectedItem = null;
        RefreshSelectionSummary();
    }

    internal void SelectMultipleItem(RecordedVideoItem item, bool toggleSelection, bool selectRange)
    {
        if (!item.CanSelect)
        {
            return;
        }

        bool wasMultiSelectMode = IsMultiSelectMode;
        ApplySelectionChange(() =>
        {
            IsMultiSelectMode = true;
            if (!wasMultiSelectMode
                && RegularSelectedItem != null
                && !ReferenceEquals(RegularSelectedItem, item))
            {
                SetItemSelection(RegularSelectedItem, RegularSelectedItem.CanSelect);
                lastSelectedItem = RegularSelectedItem.IsSelected ? RegularSelectedItem : null;
            }

            if (selectRange && lastSelectedItem != null)
            {
                RecordedVideoItem[] visible = GetVisibleVideos();
                int start = Array.IndexOf(visible, lastSelectedItem);
                int end = Array.IndexOf(visible, item);
                if (start >= 0 && end >= 0)
                {
                    if (start > end)
                    {
                        (start, end) = (end, start);
                    }

                    for (int index = start; index <= end; index++)
                    {
                        SetItemSelection(visible[index], visible[index].CanSelect);
                    }
                    lastSelectedItem = item;
                    return;
                }
            }

            if (!toggleSelection)
            {
                SetItemSelection(item, true);
            }
            else
            {
                SetItemSelection(item, !item.IsSelected);
            }
            lastSelectedItem = item.IsSelected ? item : null;
        });
    }

    internal void SelectItems(IEnumerable<RecordedVideoItem> items)
    {
        RecordedVideoItem[] targets = items.Where(item => item.CanSelect).Distinct().ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        ApplySelectionChange(() =>
        {
            IsMultiSelectMode = true;
            foreach (RecordedVideoItem item in targets)
            {
                SetItemSelection(item, true);
            }
            lastSelectedItem = targets[^1];
        });
    }

    internal void UndoSelection()
    {
        if (selectionUndoStack.Count == 0)
        {
            return;
        }

        SelectionSnapshot snapshot = selectionUndoStack.Pop();
        selectionRedoStack.Push(snapshot);
        RestoreSelection(snapshot.Before, snapshot.BeforeMultiSelectMode);
    }

    internal void RedoSelection()
    {
        if (selectionRedoStack.Count == 0)
        {
            return;
        }

        SelectionSnapshot snapshot = selectionRedoStack.Pop();
        selectionUndoStack.Push(snapshot);
        RestoreSelection(snapshot.After, snapshot.AfterMultiSelectMode);
    }

    [RelayCommand]
    private void OpenVideo(RecordedVideoItem? item)
    {
        if (item == null || !CanModifyVideo(item))
        {
            return;
        }

        if (!File.Exists(item.FullPath))
        {
            Toast.Warning("PlayerErrorOfNoFile".Tr());
            return;
        }

        try
        {
            _ = Process.Start(BuildDefaultOpenStartInfo(item.FullPath));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Toast.Warning(GetResourceText("OpenVideoFailed", "Failed to open video"));
        }
    }

    internal static ProcessStartInfo BuildDefaultOpenStartInfo(string filePath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = filePath,
            UseShellExecute = true,
        };

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            startInfo.WorkingDirectory = directory;
        }

        return startInfo;
    }

    internal static bool TryBuildRenameTarget(string sourcePath, string? requestedName, out string targetPath)
    {
        targetPath = string.Empty;
        string name = (requestedName ?? string.Empty).Trim();
        string extension = Path.GetExtension(sourcePath);
        if (!string.IsNullOrWhiteSpace(extension) && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^extension.Length].TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(name)
            || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        targetPath = Path.Combine(directory, name + extension);
        return true;
    }

    [RelayCommand]
    private async Task OpenDirectoryAsync(RecordedVideoItem? item)
    {
        if (item == null || !item.CanSelect || string.IsNullOrWhiteSpace(item.DirectoryPath) || !Directory.Exists(item.DirectoryPath))
        {
            Toast.Warning(GetResourceText("OpenFolderFailed", "Folder does not exist"));
            return;
        }

        await Launcher.LaunchFolderPathAsync(item.DirectoryPath);
    }

    [RelayCommand]
    private async Task RenameVideoAsync(RecordedVideoItem? item)
    {
        if (item == null || IsOperating || !CanModifyVideo(item) || !File.Exists(item.FullPath))
        {
            return;
        }

        System.Windows.Controls.TextBox input = new()
        {
            MinWidth = UiXDialogContent.IsEnabled ? 0d : 420d,
            MinHeight = 40d,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = Path.GetFileNameWithoutExtension(item.FileName),
        };
        input.SelectAll();
        ContentDialog dialog = new()
        {
            Title = "RenameVideo".Tr(),
            Content = UiXDialogContent.IsEnabled
                ? UiXDialogContent.CreateForm(
                    item.FileName,
                    input,
                    Wpf.Ui.Controls.FontSymbols.Rename,
                    minimumWidth: 460d)
                : input,
            CloseButtonText = "ButtonOfCancel".Tr(),
            PrimaryButtonText = "Rename".Tr(),
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.TryFindResource("EmerdeContentDialogStyle") as Style,
        };

        Window owner = Application.Current.MainWindow;
        using DialogBlurScope blurScope = UiXDialogContent.IsEnabled
            ? DialogBlurScope.ForLightDismiss(owner, dialog)
            : DialogBlurScope.ForDialog(owner, dialog);
        ContentDialogResult result = await WindowSizing.ShowContentDialogAsync(dialog, owner);
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (!TryBuildRenameTarget(item.FullPath, input.Text, out string targetPath))
        {
            Toast.Warning("InvalidVideoFileName".Tr());
            return;
        }

        if (string.Equals(item.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(targetPath))
        {
            Toast.Warning("VideoFileNameExists".Tr());
            return;
        }

        try
        {
            string sourcePath = item.FullPath;
            RenameVideoFile(sourcePath, targetPath);
            Toast.Success("RenameComplete".Tr());
            await RefreshForDisplayAsync();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Toast.Warning("RenameFailed".Tr());
        }
    }

    [RelayCommand]
    private async Task TranscodeVideoAsync(RecordedVideoItem? item)
    {
        if (item == null || !item.CanTranscode || !CanModifyVideo(item) || IsOperating)
        {
            return;
        }

        ConverterOptions? options = await ShowTranscodeOptionsAsync();
        if (options == null)
        {
            return;
        }
        if (!File.Exists(item.FullPath) || !item.CanTranscode || !CanModifyVideo(item) || IsOperating)
        {
            return;
        }

        IsUserTranscoding = true;
        IsOperating = true;
        OperationProgressText = GetResourceText("TranscodingVideo", "Transcoding...");
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            bool converted = await new Converter().ExecuteAsync(item.FullPath, options);
            if (converted)
            {
                Toast.Success(GetResourceText("TranscodeComplete", "Transcoding complete"));
            }
            else
            {
                VideoRepairResult repairResult = await new VideoRepairService().RepairAsync(item.FullPath, options.TargetFormat);
                switch (repairResult.Status)
                {
                    case VideoRepairStatus.Repaired:
                        Toast.Success(GetResourceText("TranscodeComplete", "Transcoding complete"));
                        break;
                    case VideoRepairStatus.PartiallyRepaired:
                        Toast.Warning(GetResourceText("RepairVideoPartial", "Recoverable content was saved, but the timeline remains incomplete"));
                        break;
                    case VideoRepairStatus.Failed:
                        Toast.Warning(GetResourceText("TranscodeFailed", "Transcoding failed"));
                        break;
                }
            }
            await RefreshForDisplayAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (IsUserTranscoding)
            {
                IsUserTranscoding = false;
                IsOperating = false;
                OperationProgressText = string.Empty;
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    [RelayCommand]
    private async Task RepairVideoAsync(RecordedVideoItem? item)
    {
        if (item == null || !item.CanRepair || !CanModifyVideo(item) || IsOperating)
        {
            return;
        }
        string? targetExtension = await ConfirmVideoRepairAsync();
        if (targetExtension == null)
        {
            return;
        }
        if (!File.Exists(item.FullPath) || !item.CanRepair || !CanModifyVideo(item) || IsOperating)
        {
            return;
        }

        IsOperating = true;
        OperationProgressText = GetResourceText("RepairingVideo", "Repairing recording...");
        OnPropertyChanged(nameof(IsIdle));
        try
        {
            VideoRepairResult result = await new VideoRepairService().RepairAsync(item.FullPath, targetExtension);
            switch (result.Status)
            {
                case VideoRepairStatus.Repaired:
                    Toast.Success(GetResourceText("RepairVideoComplete", "Recording repaired"));
                    break;
                case VideoRepairStatus.PartiallyRepaired:
                    Toast.Warning(GetResourceText("RepairVideoPartial", "Recoverable content was saved, but the timeline remains incomplete"));
                    break;
                case VideoRepairStatus.Failed:
                    Toast.Warning(GetResourceText("RepairVideoFailed", "Recording could not be repaired"));
                    break;
            }
            await RefreshForDisplayAsync();
        }
        finally
        {
            IsOperating = false;
            OperationProgressText = string.Empty;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    private static async Task<string?> ConfirmVideoRepairAsync()
    {
        System.Windows.Controls.ComboBox formatSelector = new()
        {
            MinWidth = 240d,
            Items =
            {
                new ComboBoxItem { Content = "MKV" },
                new ComboBoxItem { Content = "MP4" },
            },
            SelectedIndex = 0,
        };
        TextBlock description = new()
        {
            Text = GetResourceText("RepairVideoDescription", "Creates a new file from readable audio and video data. MKV is recommended for damaged recordings; MP4 requires compatible streams. The original is not changed, and missing content cannot be recreated."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            MaxWidth = 420d,
        };
        description.SetResourceReference(
            TextBlock.ForegroundProperty,
            UiXDialogContent.IsEnabled ? "UiXTextSecondaryBrush" : "TextFillColorSecondaryBrush");
        StackPanel content = new()
        {
            Width = 420d,
            Children =
            {
                formatSelector,
                description,
            },
        };
        ContentDialog dialog = new()
        {
            Title = GetResourceText("RepairVideo", "Repair recording"),
            Content = UiXDialogContent.IsEnabled
                ? UiXDialogContent.CreateForm(
                    GetResourceText("TargetFormat", "Target format"),
                    content,
                    Wpf.Ui.Controls.FontSymbols.Video,
                    minimumWidth: 420d)
                : content,
            CloseButtonText = GetResourceText("ButtonOfCancel", "Cancel"),
            PrimaryButtonText = GetResourceText("StartButton", "Start"),
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.TryFindResource("EmerdeContentDialogStyle") as Style,
        };
        Window owner = Application.Current.MainWindow;
        using DialogBlurScope blurScope = UiXDialogContent.IsEnabled
            ? DialogBlurScope.ForLightDismiss(owner, dialog)
            : DialogBlurScope.ForDialog(owner, dialog);
        ContentDialogResult result = await WindowSizing.ShowContentDialogAsync(dialog, owner);
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }
        return formatSelector.SelectedIndex == 1 ? ".mp4" : ".mkv";
    }

    private static async Task<ConverterOptions?> ShowTranscodeOptionsAsync()
    {
        System.Windows.Controls.ComboBox formatSelector = new()
        {
            MinWidth = 240,
            Items =
            {
                new ComboBoxItem { Content = "MP4" },
                new ComboBoxItem { Content = "MKV" },
            },
            SelectedIndex = 0,
        };
        System.Windows.Controls.CheckBox optimizeAudio = new()
        {
            Content = GetResourceText("CreateOptimizedAudioTrack", "Create optimized audio track (recommended)"),
            IsChecked = Configurations.IsOptimizeAudio.Get(),
            Margin = new Thickness(0, 16, 0, 0),
        };
        TextBlock description = new()
        {
            Text = GetResourceText("OptimizedAudioTrackDescription", "MP4 keeps the original audio and adds an AAC track with gain, compression, and limiting."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            Margin = new Thickness(0, 8, 0, 0),
        };
        formatSelector.SelectionChanged += (_, _) =>
        {
            bool isMp4 = formatSelector.SelectedIndex == 0;
            optimizeAudio.IsEnabled = isMp4;
            optimizeAudio.Visibility = isMp4 ? Visibility.Visible : Visibility.Collapsed;
            description.Visibility = isMp4 ? Visibility.Visible : Visibility.Collapsed;
        };
        TextBlock targetFormatLabel = new()
        {
            Text = GetResourceText("TargetFormat", "Target format"),
            Margin = new Thickness(0, 0, 0, 8),
        };
        StackPanel content = new()
        {
            MinWidth = UiXDialogContent.IsEnabled ? 0d : 360d,
            Children =
            {
                targetFormatLabel,
                formatSelector,
                optimizeAudio,
                description,
            },
        };
        if (UiXDialogContent.IsEnabled)
        {
            content.Children.Remove(targetFormatLabel);
        }
        ContentDialog dialog = new()
        {
            Title = GetResourceText("TranscodeVideo", "Transcode"),
            Content = UiXDialogContent.IsEnabled
                ? UiXDialogContent.CreateForm(
                    GetResourceText("TargetFormat", "Target format"),
                    content,
                    Wpf.Ui.Controls.FontSymbols.Video,
                    minimumWidth: 460d)
                : content,
            CloseButtonText = GetResourceText("ButtonOfCancel", "Cancel"),
            PrimaryButtonText = GetResourceText("StartButton", "Start"),
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.TryFindResource("EmerdeContentDialogStyle") as Style,
        };
        Window owner = Application.Current.MainWindow;
        using DialogBlurScope blurScope = UiXDialogContent.IsEnabled
            ? DialogBlurScope.ForLightDismiss(owner, dialog)
            : DialogBlurScope.ForDialog(owner, dialog);
        ContentDialogResult result = await WindowSizing.ShowContentDialogAsync(dialog, owner);
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }
        ConverterOptions options = CreateTranscodeOptions(formatSelector.SelectedIndex, optimizeAudio.IsChecked == true);
        if (options.TargetFormat == ".mp4")
        {
            Configurations.IsOptimizeAudio.Set(options.OptimizeAudio);
            ConfigurationSaveScheduler.Request();
            if (owner is MainWindow mainWindow)
            {
                mainWindow.SettingsPage.ViewModel.IsOptimizeAudio = options.OptimizeAudio;
            }
        }
        return options;
    }

    internal static ConverterOptions CreateTranscodeOptions(int selectedIndex, bool optimizeAudio)
    {
        return selectedIndex switch
        {
            0 => new ConverterOptions(".mp4", optimizeAudio),
            1 => new ConverterOptions(".mkv", false),
            _ => throw new ArgumentOutOfRangeException(nameof(selectedIndex)),
        };
    }

    [RelayCommand]
    private void SplitVideo(RecordedVideoItem? item)
    {
        if (item == null || !CanModifyVideoForUserOperation(item) || !File.Exists(item.FullPath))
        {
            Toast.Warning("VideoFileMissing".Tr());
            return;
        }

        splitTargetItem = item;
        SplitTargetFileName = item.FileName;
        OperationProgressText = string.Empty;
        IsSplitPanelOpen = true;
    }

    [RelayCommand]
    private void SplitSelected()
    {
        RecordedVideoItem[] selected = GetSelectedVideos();
        if (selected.Length == 0 || selected.Any(item => !CanModifyVideoForUserOperation(item)))
        {
            Toast.Warning("SelectVideosToSplit".Tr());
            return;
        }

        splitTargetItem = null;
        SplitTargetFileName = "SelectedVideos".Tr(selected.Length);
        OperationProgressText = string.Empty;
        IsSplitPanelOpen = true;
    }

    [RelayCommand]
    private void SplitContext(RecordedVideoItem? item)
    {
        RecordedVideoItem[] targets = GetContextVideos(item);
        if (targets.Length == 1)
        {
            SplitVideo(targets[0]);
            return;
        }

        SplitSelected();
    }

    [RelayCommand]
    private void CloseModal()
    {
        if (IsOperating && !IsUserTranscoding)
        {
            return;
        }

        IsSplitPanelOpen = false;
        IsMergePanelOpen = false;
        splitTargetItem = null;
        OperationProgressText = string.Empty;
        MergeProgressValue = 0;
        IsMergeProgressIndeterminate = false;
    }

    [RelayCommand]
    private async Task ConfirmSplitAsync()
    {
        if (IsOperating && !IsUserTranscoding)
        {
            return;
        }

        RecordedVideoItem[] targets = splitTargetItem != null ? [splitTargetItem] : GetSelectedVideos();
        if (targets.Length == 0 || targets.Any(item => !CanModifyVideoForUserOperation(item)))
        {
            Toast.Warning("SelectVideosToSplit".Tr());
            return;
        }

        if (!TryGetSplitDurationSeconds(out int seconds))
        {
            Toast.Warning(GetResourceText("SplitDurationInvalid", "Invalid split interval"));
            return;
        }

        bool ownsOperation = false;
        try
        {
            if (!await CancelConversionsForUserOperationAsync(targets))
            {
                return;
            }

            IsUserTranscoding = false;
            IsOperating = true;
            ownsOperation = true;
            OperationProgressText = GetResourceText("SplittingVideo", "Splitting...");
            OnPropertyChanged(nameof(IsIdle));

            int completed = 0;
            foreach (RecordedVideoItem target in targets)
            {
                OperationProgressText = "SplittingProgress".Tr(completed + 1, targets.Length);
                if (await SplitVideoFileAsync(target, seconds))
                {
                    completed++;
                }
            }

            if (completed > 0)
            {
                Toast.Success("SplitCompletedCount".Tr(completed));
                IsSplitPanelOpen = false;
                await RefreshForDisplayAsync();
            }
            else
            {
                Toast.Warning(GetResourceText("SplitFailed", "Split failed"));
            }
        }
        finally
        {
            if (ownsOperation)
            {
                IsOperating = false;
                OperationProgressText = string.Empty;
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    [RelayCommand]
    private void OpenMergeSelected()
    {
        RecordedVideoItem[] selected = GetSelectedVideos();
        if (selected.Length < 2 || selected.Any(item => !CanModifyVideoForUserOperation(item)))
        {
            Toast.Warning(GetResourceText("SelectAtLeastTwoVideos", "Select at least two videos"));
            return;
        }

        MergeWarningText = BuildMergeWarningText(selected);
        MergeProgressValue = 0;
        IsMergeProgressIndeterminate = false;
        IsMergePanelOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmMergeSelectedAsync()
    {
        if (IsOperating && !IsUserTranscoding)
        {
            return;
        }

        RecordedVideoItem[] selected = OrderVideosForMerge(GetSelectedVideos()).ToArray();

        if (selected.Length < 2 || selected.Any(item => !CanModifyVideoForUserOperation(item)))
        {
            Toast.Warning(GetResourceText("SelectAtLeastTwoVideos", "Select at least two videos"));
            return;
        }

        if (selected.Select(video => Path.GetExtension(video.FullPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            Toast.Warning(GetResourceText("MergeFormatsMustMatch", "Only videos with the same format can be merged"));
            return;
        }

        bool ownsOperation = false;
        try
        {
            if (!await CancelConversionsForUserOperationAsync(selected))
            {
                return;
            }

            IsUserTranscoding = false;
            IsOperating = true;
            ownsOperation = true;
            OperationProgressText = "PreparingMerge".Tr();
            OnPropertyChanged(nameof(IsIdle));

            bool streamsCompatible = await Task.Run(() => HaveCompatibleMergeStreams(selected));
            if (!streamsCompatible)
            {
                Toast.Warning("LosslessMergeIncompatible".Tr());
                return;
            }

            using CommonOpenFileDialog dialog = new()
            {
                IsFolderPicker = true,
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            {
                return;
            }

            OperationProgressText = GetResourceText("MergingVideos", "Merging...");
            IsMergeProgressIndeterminate = true;
            MergeProgressValue = 0;
            bool result = await MergeVideosAsync(selected, dialog.FileName, progress =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsMergeProgressIndeterminate = false;
                    MergeProgressValue = progress;
                });
            });
            if (result)
            {
                MergeProgressValue = 100;
                IsMergePanelOpen = false;
                Toast.Success(GetResourceText("MergeComplete", "Merge complete"));
                await RefreshForDisplayAsync();
            }
            else
            {
                Toast.Warning(GetResourceText("MergeFailed", "Merge failed"));
            }
        }
        finally
        {
            if (ownsOperation)
            {
                IsOperating = false;
                IsMergeProgressIndeterminate = false;
                OperationProgressText = string.Empty;
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        await DeleteVideosAsync(GetSelectedVideos());
    }

    [RelayCommand]
    private async Task DeleteContextAsync(RecordedVideoItem? item)
    {
        await DeleteVideosAsync(GetContextVideos(item));
    }

    [RelayCommand]
    private async Task DeleteVideoAsync(RecordedVideoItem? item)
    {
        if (item != null)
        {
            await DeleteVideosAsync([item]);
        }
    }

    private async Task DeleteVideosAsync(IReadOnlyCollection<RecordedVideoItem> items)
    {
        if (IsOperating && !IsUserTranscoding
            || items.Count == 0
            || items.Any(item => !CanModifyVideoForUserOperation(item)))
        {
            return;
        }

        string prompt = FormatResourceText("ConfirmDeleteVideos", "Delete {0} video files?", items.Count);
        bool confirmed;
        if (UiXDialogContent.IsEnabled)
        {
            confirmed = await UiXDialogContent.ConfirmAsync(
                Application.Current.MainWindow,
                GetResourceText("DeleteButton", "Delete"),
                prompt,
                GetResourceText("DeleteButton", "Delete"),
                "ButtonOfCancel".Tr(),
                Wpf.Ui.Controls.FontSymbols.Delete,
                UiXDialogTone.Danger);
        }
        else
        {
            using DialogBlurScope blurScope = DialogBlurScope.ForMessageBox(Application.Current.MainWindow);
            confirmed = await MessageBox.QuestionAsync(prompt) == System.Windows.MessageBoxResult.Yes;
        }
        if (!confirmed)
        {
            return;
        }

        int deleted = 0;
        int failed = 0;
        bool ownsOperation = false;
        BeginAutomaticRefreshSuppression();
        try
        {
            if (!await CancelConversionsForUserOperationAsync(items))
            {
                return;
            }

            IsUserTranscoding = false;
            IsOperating = true;
            ownsOperation = true;
            OperationProgressText = "PreparingDelete".Tr();
            OnPropertyChanged(nameof(IsIdle));

            foreach (RecordedVideoItem item in items)
            {
                try
                {
                    if (File.Exists(item.FullPath))
                    {
                        FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(item.FullPath, sendToRecycleBin: true);
                        DeleteThumbnailCacheIfUnused(item.FullPath);
                        deleted++;
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    failed++;
                }
            }

            if (deleted > 0)
            {
                Toast.Success("DeletedVideos".Tr(deleted));
            }
            if (failed > 0)
            {
                Toast.Warning("VideoDeleteFailures".Tr(failed));
            }
            Interlocked.Exchange(ref automaticRefreshDeferred, 0);
            Interlocked.Exchange(ref directorySnapshotDirty, 1);
            await RefreshForDisplayAsync();
        }
        finally
        {
            if (ownsOperation)
            {
                IsOperating = false;
                OperationProgressText = string.Empty;
                OnPropertyChanged(nameof(IsIdle));
            }
            EndAutomaticRefreshSuppression();
        }
    }

    [RelayCommand]
    private async Task MoveSelectedAsync()
    {
        await CopyOrMoveVideosAsync(GetSelectedVideos(), move: true);
    }

    [RelayCommand]
    private async Task MoveContextAsync(RecordedVideoItem? item)
    {
        await CopyOrMoveVideosAsync(GetContextVideos(item), move: true);
    }

    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        await CopyOrMoveVideosAsync(GetSelectedVideos(), move: false);
    }

    [RelayCommand]
    private async Task CopyContextAsync(RecordedVideoItem? item)
    {
        await CopyOrMoveVideosAsync(GetContextVideos(item), move: false);
    }

    [RelayCommand]
    private async Task SaveAsVideoAsync(RecordedVideoItem? item)
    {
        if (item != null)
        {
            await CopyOrMoveVideosAsync([item], move: false);
        }
    }

    private async Task CopyOrMoveVideosAsync(IReadOnlyCollection<RecordedVideoItem> items, bool move)
    {
        if (IsOperating || items.Count == 0 || items.Any(item => !CanModifyVideo(item)))
        {
            return;
        }

        using CommonOpenFileDialog dialog = new()
        {
            IsFolderPicker = true,
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return;
        }

        IsOperating = true;
        OperationProgressText = move
            ? GetResourceText("MovingVideos", "Moving...")
            : GetResourceText("CopyingVideos", "Copying...");
        OnPropertyChanged(nameof(IsIdle));

        try
        {
            int succeeded = 0;
            int failed = 0;
            foreach (RecordedVideoItem item in items)
            {
                try
                {
                    string targetPath = GetUniquePath(Path.Combine(dialog.FileName, item.FileName));
                    if (string.Equals(item.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        failed++;
                        continue;
                    }

                    await Task.Run(() =>
                    {
                        TransferVideoFile(item.FullPath, targetPath, move);
                        if (move)
                        {
                            DeleteThumbnailCacheIfUnused(item.FullPath);
                        }
                    });
                    succeeded++;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    failed++;
                }
            }

            if (succeeded > 0)
            {
                Toast.Success(move ? "MovedVideos".Tr(succeeded) : "CopiedVideos".Tr(succeeded));
            }
            if (failed > 0)
            {
                Toast.Warning("VideoTransferFailures".Tr(failed));
            }
            await RefreshForDisplayAsync();
        }
        finally
        {
            IsOperating = false;
            OperationProgressText = string.Empty;
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    private async Task LoadVideosFromFoldersAsync(IEnumerable<string> folders, bool reuseExistingItems)
    {
        if (videoLoadCancellationTokenSource == null || videoLoadCancellationTokenSource.IsCancellationRequested)
        {
            videoLoadCancellationTokenSource?.Dispose();
            videoLoadCancellationTokenSource = new CancellationTokenSource();
        }
        CancellationToken loadToken = videoLoadCancellationTokenSource.Token;

        videoEnrichmentCancellationTokenSource?.Cancel();
        videoEnrichmentCancellationTokenSource?.Dispose();
        videoEnrichmentCancellationTokenSource = new CancellationTokenSource();
        lock (videoEnrichmentLock)
        {
            queuedVideoEnrichmentPaths.Clear();
        }
        videoEnrichmentQueue.Clear();

        string[] roots = folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string globalRecordFormat = RoomRecordingSettings.GetGlobal().RecordFormat;
        Dictionary<string, string> roomRecordFormats = Configurations.Rooms.Get()
            .Where(room => !string.IsNullOrWhiteSpace(room.RoomUrl))
            .GroupBy(room => room.RoomUrl, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => RoomRecordingSettings.Get(group.Last()).RecordFormat, StringComparer.OrdinalIgnoreCase);
        bool rootsChanged = !watchedRoots.SequenceEqual(roots, StringComparer.OrdinalIgnoreCase);
        ConfigureDirectoryWatchers(roots);
        if (reuseExistingItems && !rootsChanged && Volatile.Read(ref directorySnapshotDirty) == 0)
        {
            UpdateTargetRecordFormats(videos, globalRecordFormat, roomRecordFormats);
            foreach (RecordedVideoItem item in videos)
            {
                UpdateOperationState(item);
            }
            VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        Interlocked.Exchange(ref directorySnapshotDirty, 0);

        RecordedVideoItem[] items;
        Dictionary<string, RecordedVideoItem> existingByPath = reuseExistingItems
            ? videos.Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
                .GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, RecordedVideoItem>(StringComparer.OrdinalIgnoreCase);
        try
        {
            items = await Task.Run(() =>
            {
                VideoFileSnapshot[] snapshots = roots
                    .Where(Directory.Exists)
                    .SelectMany(root => EnumerateVideoFiles(root, loadToken).Select(path => (Path: path, Root: root)))
                    .Where(item => MediaFileCatalog.IsMediaPath(item.Path) && !MediaFileCatalog.IsApplicationTemporaryPath(item.Path))
                    .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(group => CreateVideoFileSnapshot(group.First().Path, group.First().Root))
                    .Where(snapshot => snapshot.HasValue)
                    .Select(snapshot => snapshot.GetValueOrDefault())
                    .OrderBy(snapshot => snapshot.Path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                CleanupOrphanedThumbnailCache(snapshots.Select(snapshot => snapshot.Path));
                return snapshots
                    .Select(snapshot =>
                    {
                        loadToken.ThrowIfCancellationRequested();
                        bool isInProgress = MediaOperationRegistry.IsPathProtected(snapshot.Path);
                        bool isConverting = MediaOperationRegistry.IsPathProtectedBy(MediaOperationKind.Conversion, snapshot.Path);
                        bool isRecordingFile = MediaOperationRegistry.IsPathProtectedBy(MediaOperationKind.Recording, snapshot.Path);
                        if (existingByPath.TryGetValue(snapshot.Path, out RecordedVideoItem? existing)
                            && existing.SourceLength == snapshot.Length
                            && existing.SourceLastWriteTimeUtc == snapshot.LastWriteTimeUtc
                            && existing.MetadataLastWriteTimeUtc == snapshot.MetadataLastWriteTimeUtc)
                        {
                            return existing;
                        }

                        return TryCreateRecordedVideoItem(snapshot, isInProgress, isConverting, isRecordingFile);
                    })
                    .Where(item => item != null)
                    .Select(item => item!)
                    .ToArray();
            }, loadToken);
        }
        catch (OperationCanceledException) when (loadToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref directorySnapshotDirty, 1);
            return;
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
            return;
        }

        if (loadToken.IsCancellationRequested)
        {
            return;
        }

        foreach (RecordedVideoItem item in items)
        {
            UpdateTargetRecordFormat(item, globalRecordFormat, roomRecordFormats);
            UpdateOperationState(item);
        }

        string[] selectedPaths = CaptureSelectedPaths();
        Dictionary<string, int> selectedOrderByPath = selectedPaths
            .Select((path, index) => new KeyValuePair<string, int>(path, index + 1))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        string regularSelectedPath = RegularSelectedItem?.FullPath ?? string.Empty;
        if (reuseExistingItems)
        {
            items = ReuseExistingVideoItems(videos, items);
            if (videos.SequenceEqual(items))
            {
                lastSelectedItem = GetLastSelectedItem();
                RegularSelectedItem = videos.FirstOrDefault(item => item.CanSelect && string.Equals(item.FullPath, regularSelectedPath, StringComparison.OrdinalIgnoreCase));
                VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        HashSet<RecordedVideoItem> previousItems = new(videos, ReferenceEqualityComparer.Instance);
        HashSet<RecordedVideoItem> nextItems = new(items, ReferenceEqualityComparer.Instance);
        foreach (RecordedVideoItem item in previousItems)
        {
            if (!nextItems.Contains(item))
            {
                item.PropertyChanged -= VideoItemPropertyChanged;
            }
        }
        foreach (RecordedVideoItem item in items)
        {
            int selectionOrder = 0;
            item.IsSelected = item.CanSelect && selectedOrderByPath.TryGetValue(item.FullPath, out selectionOrder);
            item.UserSelectionOrder = item.IsSelected ? selectionOrder : 0;
            item.SelectionOrder = item.IsSelected ? selectionOrder : 0;
            if (!previousItems.Contains(item))
            {
                item.PropertyChanged += VideoItemPropertyChanged;
            }
        }
        ReconcileVideoItems(videos, items);

        UpdateStreamerOptions();
        ApplySort();
        ApplyFilters();
        NormalizeSelectionOrders();
        RefreshSelectionSummary();
        lastSelectedItem = GetLastSelectedItem();
        RegularSelectedItem = videos.FirstOrDefault(item => item.CanSelect && string.Equals(item.FullPath, regularSelectedPath, StringComparison.OrdinalIgnoreCase));
        VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static RecordedVideoItem[] ReuseExistingVideoItems(
        IEnumerable<RecordedVideoItem> existingItems,
        IEnumerable<RecordedVideoItem> loadedItems)
    {
        Dictionary<string, RecordedVideoItem> existingByPath = existingItems
            .Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
            .GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return loadedItems
            .Select(item => existingByPath.TryGetValue(item.FullPath, out RecordedVideoItem? existing)
                && existing.SourceLength == item.SourceLength
                && existing.SourceLastWriteTimeUtc == item.SourceLastWriteTimeUtc
                && existing.MetadataLastWriteTimeUtc == item.MetadataLastWriteTimeUtc
                    ? existing
                    : item)
            .ToArray();
    }

    internal static void ReconcileVideoItems(
        ObservableCollection<RecordedVideoItem> currentItems,
        IReadOnlyList<RecordedVideoItem> nextItems)
    {
        HashSet<RecordedVideoItem> nextSet = new(nextItems, ReferenceEqualityComparer.Instance);
        for (int targetIndex = 0; targetIndex < nextItems.Count; targetIndex++)
        {
            RecordedVideoItem target = nextItems[targetIndex];
            if (targetIndex < currentItems.Count && ReferenceEquals(currentItems[targetIndex], target))
            {
                continue;
            }

            int currentIndex = currentItems.IndexOf(target);
            if (currentIndex >= 0)
            {
                currentItems.Move(currentIndex, targetIndex);
            }
            else if (targetIndex < currentItems.Count && !nextSet.Contains(currentItems[targetIndex]))
            {
                currentItems[targetIndex] = target;
            }
            else
            {
                currentItems.Insert(targetIndex, target);
            }
        }

        while (currentItems.Count > nextItems.Count)
        {
            currentItems.RemoveAt(currentItems.Count - 1);
        }
    }

    private static VideoFileSnapshot? CreateVideoFileSnapshot(string path, string rootFolder)
    {
        try
        {
            FileInfo fileInfo = new(path);
            if (!fileInfo.Exists)
            {
                return null;
            }

            DateTime metadataLastWriteTimeUtc = VideoRecordingMetadataStore.GetMetadataCandidates(fileInfo)
                .Where(File.Exists)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            return new VideoFileSnapshot(fileInfo.FullName, rootFolder, fileInfo.Length, fileInfo.LastWriteTimeUtc, metadataLastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static RecordedVideoItem? TryCreateRecordedVideoItem(VideoFileSnapshot snapshot, bool isInProgress, bool isConverting, bool isRecordingFile)
    {
        try
        {
            return CreateRecordedVideoItem(snapshot, isInProgress, isConverting, isRecordingFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static RecordedVideoItem CreateRecordedVideoItem(VideoFileSnapshot snapshot, bool isInProgress, bool isConverting, bool isRecordingFile)
    {
        FileInfo fileInfo = new(snapshot.Path);
        VideoRecordingMetadata metadata = LoadMetadata(fileInfo);
        string resolution = NormalizeResolution(metadata.Resolution);
        string bitrate = NormalizeBitrate(metadata.Bitrate);

        string nickName = NormalizeStreamerName(string.IsNullOrWhiteSpace(metadata.NickName) ? InferNickName(snapshot.Path, snapshot.RootFolder) : metadata.NickName);
        DateTime createdAt = metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt : fileInfo.LastWriteTime;
        string thumbnailPath = GetExistingThumbnailPath(fileInfo.FullName, metadata.CoverPath);
        return new RecordedVideoItem
        {
            FileName = fileInfo.Name,
            FullPath = fileInfo.FullName,
            DirectoryPath = fileInfo.DirectoryName ?? string.Empty,
            RoomUrl = metadata.RoomUrl,
            NickName = nickName,
            Resolution = resolution,
            Bitrate = bitrate,
            CoverPath = metadata.CoverPath,
            ThumbnailPath = thumbnailPath,
            Title = BuildDisplayTitle(metadata.Title, createdAt, fileInfo),
            CreatedAt = createdAt,
            SupportsTranscode = fileInfo.Extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                || fileInfo.Extension.Equals(".flv", StringComparison.OrdinalIgnoreCase),
            IsInProgress = isInProgress,
            IsConverting = isConverting,
            IsRecordingFile = isRecordingFile,
            IsStallSegment = string.Equals(metadata.SegmentReason, VideoRecordingMetadataStore.TimelineStallSegmentReason, StringComparison.Ordinal),
            SourceLength = snapshot.Length,
            SourceLastWriteTimeUtc = snapshot.LastWriteTimeUtc,
            MetadataLastWriteTimeUtc = snapshot.MetadataLastWriteTimeUtc,
        };
    }

    private static void UpdateTargetRecordFormats(
        IEnumerable<RecordedVideoItem> items,
        string globalRecordFormat,
        IReadOnlyDictionary<string, string> roomRecordFormats)
    {
        foreach (RecordedVideoItem item in items)
        {
            UpdateTargetRecordFormat(item, globalRecordFormat, roomRecordFormats);
        }
    }

    private static void UpdateTargetRecordFormat(
        RecordedVideoItem item,
        string globalRecordFormat,
        IReadOnlyDictionary<string, string> roomRecordFormats)
    {
        item.TargetRecordFormat = !string.IsNullOrWhiteSpace(item.RoomUrl)
            && roomRecordFormats.TryGetValue(item.RoomUrl, out string? roomRecordFormat)
                ? roomRecordFormat
                : globalRecordFormat;
    }

    internal static string GetVideoFormatText(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return string.IsNullOrWhiteSpace(extension) ? "-" : extension.TrimStart('.').ToUpperInvariant();
    }

    internal static bool IsTargetVideoFormat(string filePath, string recordFormat)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        string? targetFormat = Recorder.GetTargetFormat(recordFormat);
        return string.IsNullOrWhiteSpace(targetFormat)
            ? extension is ".ts" or ".flv"
            : extension.Equals(targetFormat, StringComparison.OrdinalIgnoreCase);
    }

    internal static VideoRecordingMetadata LoadMetadata(FileInfo file)
    {
        return VideoRecordingMetadataStore.Load(file);
    }

    internal static string NormalizeResolution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsMediaInfoErrorText(value))
        {
            return UnknownResolutionText;
        }

        string text = value.Trim();
        string[] parts = text.Split(['x', 'X', '*', '×'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            && width > 0
            && height > 0)
        {
            return $"{width}x{height}";
        }

        if (text.Length > 1
            && (text[^1] is 'p' or 'P' or 'i' or 'I')
            && int.TryParse(text[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int verticalResolution)
            && verticalResolution > 0)
        {
            return $"{verticalResolution}{char.ToLowerInvariant(text[^1])}";
        }

        return UnknownResolutionText;
    }

    private static bool TryBuildResolution(string widthText, string heightText, out string resolution)
    {
        resolution = string.Empty;
        if (IsMediaInfoErrorText(widthText) || IsMediaInfoErrorText(heightText))
        {
            return false;
        }

        if (int.TryParse(widthText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            && int.TryParse(heightText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            && width > 0
            && height > 0)
        {
            resolution = $"{width}x{height}";
            return true;
        }

        return false;
    }

    private static string NormalizeBitrate(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || IsMediaInfoErrorText(value)
            ? UnknownBitrateText
            : value.Trim();
    }

    private static string NormalizeStreamerName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || IsMediaInfoErrorText(value)
            ? UnknownStreamerText
            : value.Trim();
    }

    private static bool IsMediaInfoErrorText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains("Unable to load MediaInfo", StringComparison.OrdinalIgnoreCase);
    }

    private static VideoProbeInfo ProbeVideoFileInfo(string filePath, long fileSize)
    {
        if (!File.Exists(filePath)
            || !FfmpegMediaEngine.TryProbe(filePath, out FfmpegMediaProbeResult result, out _))
        {
            return VideoProbeInfo.Empty;
        }

        string resolution = result.Width > 0 && result.Height > 0 ? $"{result.Width}x{result.Height}" : string.Empty;
        double bitrate = result.Bitrate > 0
            ? result.Bitrate
            : result.DurationSeconds > 0 && fileSize > 0 ? fileSize * 8d / result.DurationSeconds : 0;
        return new VideoProbeInfo(resolution, bitrate > 0 ? FormatBitrate(bitrate) : string.Empty, result.Metadata);
    }

    internal static VideoProbeInfo ParseVideoProbeJson(string json, long fileSize = 0)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return VideoProbeInfo.Empty;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string resolution = string.Empty;
        double bitrate = 0;
        VideoRecordingMetadata? metadata = null;

        if (root.TryGetProperty("streams", out JsonElement streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                if (TryGetInt(stream, "width", out int width)
                    && TryGetInt(stream, "height", out int height)
                    && width > 0
                    && height > 0)
                {
                    resolution = $"{width}x{height}";
                }

                if (TryGetDouble(stream, "bit_rate", out double streamBitrate) && streamBitrate > 0)
                {
                    bitrate = streamBitrate;
                }

                break;
            }
        }

        if (root.TryGetProperty("format", out JsonElement format))
        {
            if (format.TryGetProperty("tags", out JsonElement tags))
            {
                VideoRecordingMetadata embeddedMetadata = VideoRecordingMetadataStore.FromTags(tags, string.Empty);
                if (VideoRecordingMetadataStore.HasAnyMetadata(embeddedMetadata))
                {
                    metadata = embeddedMetadata;
                }
            }

            if (bitrate <= 0 && TryGetDouble(format, "bit_rate", out double formatBitrate) && formatBitrate > 0)
            {
                bitrate = formatBitrate;
            }

            if (bitrate <= 0
                && fileSize > 0
                && TryGetDouble(format, "duration", out double duration)
                && duration > 0)
            {
                bitrate = fileSize * 8d / duration;
            }
        }

        return new VideoProbeInfo(resolution, bitrate > 0 ? FormatBitrate(bitrate) : string.Empty, metadata);
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static string FormatBitrate(double bitsPerSecond)
    {
        return $"{(bitsPerSecond / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture)} Mbps";
    }

    internal void QueueVisibleVideoEnrichment(IEnumerable<RecordedVideoItem> items)
    {
        CancellationTokenSource? source = videoEnrichmentCancellationTokenSource;
        if (source == null)
        {
            return;
        }

        foreach (RecordedVideoItem item in items)
        {
            QueueVideoEnrichment(item, source.Token);
        }
    }

    internal void CancelBackgroundLoading()
    {
        videoLoadCancellationTokenSource?.Cancel();
        videoEnrichmentCancellationTokenSource?.Cancel();
        videoEnrichmentQueue.Clear();
        lock (videoEnrichmentLock)
        {
            queuedVideoEnrichmentPaths.Clear();
        }
    }

    private void QueueVideoEnrichment(RecordedVideoItem item, CancellationToken token)
    {
        if (item.IsEnriched || item.IsInProgress || MediaOperationRegistry.IsPathProtected(item.FullPath) || token.IsCancellationRequested)
        {
            return;
        }

        lock (videoEnrichmentLock)
        {
            if (!queuedVideoEnrichmentPaths.Add(item.FullPath))
            {
                return;
            }
        }

        videoEnrichmentQueue.Enqueue(item);
        StartVideoEnrichmentWorker(token);
    }

    private void StartVideoEnrichmentWorker(CancellationToken token)
    {
        if (Interlocked.Exchange(ref videoEnrichmentWorkerRunning, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && videoEnrichmentQueue.TryDequeue(out RecordedVideoItem? item))
                {
                    try
                    {
                        await EnrichVideoAsync(item, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        AppSessionLogger.WriteException(e);
                    }
                    finally
                    {
                        lock (videoEnrichmentLock)
                        {
                            queuedVideoEnrichmentPaths.Remove(item.FullPath);
                        }
                    }

                    await Task.Yield();
                }
            }
            finally
            {
                Interlocked.Exchange(ref videoEnrichmentWorkerRunning, 0);
                CancellationTokenSource? source = videoEnrichmentCancellationTokenSource;
                if (source != null && !source.IsCancellationRequested && !videoEnrichmentQueue.IsEmpty)
                {
                    StartVideoEnrichmentWorker(source.Token);
                }
            }
        }, CancellationToken.None);
    }

    private async Task EnrichVideoAsync(RecordedVideoItem item, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!TryGetExistingFile(item.FullPath, out FileInfo? file, out long fileLength)
                || MediaOperationRegistry.IsPathProtected(item.FullPath))
            {
                return;
            }

            VideoRecordingMetadata metadata = LoadMetadata(file);
            VideoProbeInfo probeInfo = ProbeVideoFileInfo(item.FullPath, fileLength);
            metadata = VideoRecordingMetadataStore.Merge(metadata, probeInfo.Metadata);
            if (!VideoRecordingMetadataStore.HasValidMetadata(file)
                && VideoRecordingMetadataStore.HasAnyMetadata(probeInfo.Metadata))
            {
                _ = VideoRecordingMetadataStore.WriteCompletedMetadata(item.FullPath, metadata);
            }
            string coverPath = string.IsNullOrWhiteSpace(metadata.CoverPath) ? item.CoverPath : metadata.CoverPath;
            string thumbnailPath = File.Exists(coverPath) ? coverPath : await ExtractThumbnailAsync(item.FullPath, token);
            file.Refresh();
            if (!file.Exists)
            {
                return;
            }
            DateTime createdAt = metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt : file.LastWriteTime;
            string resolution = string.IsNullOrWhiteSpace(probeInfo.Resolution) ? NormalizeResolution(metadata.Resolution) : probeInfo.Resolution;
            string bitrate = string.IsNullOrWhiteSpace(probeInfo.Bitrate) ? NormalizeBitrate(metadata.Bitrate) : probeInfo.Bitrate;

            token.ThrowIfCancellationRequested();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested && videos.Contains(item) && File.Exists(item.FullPath))
                {
                    bool createdAtChanged = item.CreatedAt != createdAt;
                    item.NickName = NormalizeStreamerName(string.IsNullOrWhiteSpace(metadata.NickName) ? item.NickName : metadata.NickName);
                    item.Resolution = resolution;
                    item.Bitrate = bitrate;
                    item.CoverPath = coverPath;
                    if (!string.IsNullOrWhiteSpace(thumbnailPath))
                    {
                        item.ThumbnailPath = thumbnailPath;
                    }
                    item.CreatedAt = createdAt;
                    item.Title = BuildDisplayTitle(metadata.Title, createdAt, file);
                    item.IsStallSegment = string.Equals(metadata.SegmentReason, VideoRecordingMetadataStore.TimelineStallSegmentReason, StringComparison.Ordinal);
                    item.IsEnriched = true;
                    if (createdAtChanged)
                    {
                        QueueVideoViewRefresh();
                    }
                }
            });
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
        }
    }

    internal static bool TryGetExistingFile(string path, out FileInfo file, out long length)
    {
        file = new FileInfo(path);
        length = 0;
        try
        {
            file.Refresh();
            if (!file.Exists)
            {
                return false;
            }
            length = file.Length;
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private async Task<string> ExtractThumbnailAsync(string filePath, CancellationToken token)
    {
        if (!File.Exists(filePath) || MediaOperationRegistry.IsPathProtected(filePath))
        {
            return string.Empty;
        }

        string cacheDir = GetThumbnailCacheDirectory();
        Directory.CreateDirectory(cacheDir);
        string imagePath = GetThumbnailCachePath(filePath);

        if (IsThumbnailCacheCurrent(filePath, imagePath))
        {
            return imagePath;
        }

        await Task.CompletedTask;
        return string.Empty;
    }

    private static async Task<string> ExtractThumbnailSharedAsync(string ffmpegPath, string filePath, string imagePath)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(45));
        return await ExtractThumbnailCoreAsync(ffmpegPath, filePath, imagePath, timeout.Token);
    }

    private static async Task<string> ExtractThumbnailCoreAsync(string ffmpegPath, string filePath, string imagePath, CancellationToken token)
    {
        if (IsThumbnailCacheCurrent(filePath, imagePath))
        {
            return imagePath;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };

            foreach (string argument in new[] { "-y", "-v", "error", "-ss", "00:00:01", "-i", filePath, "-frames:v", "1", "-vf", "scale=320:-1", imagePath })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            _ = ChildProcessTracerPeriodicTimer.Default.TryTraceProcess(process);

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(token);
            Task exitTask = process.WaitForExitAsync(CancellationToken.None);
            Task timeoutTask = Task.Delay(12000, token);

            if (await Task.WhenAny(exitTask, timeoutTask) != exitTask)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(outputTask, errorTask);
                token.ThrowIfCancellationRequested();
                return string.Empty;
            }

            await Task.WhenAll(outputTask, errorTask);
            return File.Exists(imagePath) && new FileInfo(imagePath).Length > 0 ? imagePath : string.Empty;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or OperationCanceledException or Win32Exception)
        {
            return string.Empty;
        }
    }

    private static string ToStableHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    private static string GetThumbnailCacheDirectory()
    {
        return AppPaths.ThumbnailCacheDirectory;
    }

    internal static string GetThumbnailCachePath(string filePath)
    {
        return GetThumbnailCachePath(filePath, GetThumbnailCacheDirectory());
    }

    internal static string GetThumbnailCachePath(string filePath, string cacheDirectory)
    {
        return Path.Combine(cacheDirectory, $"{ToStableHash(GetThumbnailIdentity(filePath))}.jpg");
    }

    internal static string GetExistingThumbnailPath(string filePath, string coverPath, string? cacheDirectory = null)
    {
        if (File.Exists(coverPath))
        {
            return coverPath;
        }

        string cachePath = cacheDirectory == null
            ? GetThumbnailCachePath(filePath)
            : GetThumbnailCachePath(filePath, cacheDirectory);
        return IsThumbnailCacheCurrent(filePath, cachePath) ? cachePath : string.Empty;
    }

    internal static string GetResourceText(string key, string fallback)
    {
        return Resources.ResourceManager.GetString(key, Resources.Culture ?? CultureInfo.CurrentUICulture) ?? fallback;
    }

    internal static string FormatResourceText(string key, string fallback, params object[] values)
    {
        return string.Format(CultureInfo.CurrentCulture, GetResourceText(key, fallback), values);
    }

    private static bool IsThumbnailCacheCurrent(string filePath, string cachePath)
    {
        if (!File.Exists(filePath) || !File.Exists(cachePath))
        {
            return false;
        }

        FileInfo video = new(filePath);
        FileInfo thumbnail = new(cachePath);
        return thumbnail.Length > 0 && thumbnail.LastWriteTimeUtc >= video.LastWriteTimeUtc;
    }

    private static string GetThumbnailIdentity(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, stem).ToUpperInvariant();
    }

    internal static int CleanupOrphanedThumbnailCache(IEnumerable<string> videoFilePaths, string? cacheDirectory = null)
    {
        string directory = cacheDirectory ?? GetThumbnailCacheDirectory();
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        HashSet<string> expectedFileNames = videoFilePaths
            .Select(path => Path.GetFileName(GetThumbnailCachePath(path, directory)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int deleted = 0;

        try
        {
            foreach (string thumbnailPath in Directory.EnumerateFiles(directory, "*.jpg", System.IO.SearchOption.TopDirectoryOnly))
            {
                if (expectedFileNames.Contains(Path.GetFileName(thumbnailPath)))
                {
                    continue;
                }

                try
                {
                    File.Delete(thumbnailPath);
                    deleted++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine(e);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
        }

        return deleted;
    }

    internal static void CopyAssociatedMetadata(string sourceFilePath, string targetFilePath)
    {
        FileInfo source = new(sourceFilePath);
        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(source);
        if (!VideoRecordingMetadataStore.HasAnyMetadata(metadata))
        {
            return;
        }

        if (!VideoRecordingMetadataStore.WriteCompletedMetadata(targetFilePath, metadata))
        {
            throw new IOException("Failed to write recording metadata.");
        }
    }

    internal static void TransferVideoFile(string sourceFilePath, string targetFilePath, bool move)
    {
        if (!move)
        {
            string temporaryTargetPath = MediaFileCatalog.CreateTemporaryPath(targetFilePath, "copy");
            try
            {
                CopyAndVerifyFile(sourceFilePath, temporaryTargetPath);
                File.Move(temporaryTargetPath, targetFilePath, overwrite: false);
                CopyAssociatedMetadata(sourceFilePath, targetFilePath);
                return;
            }
            catch
            {
                DeleteFileIfExists(temporaryTargetPath);
                DeleteFileIfExists(targetFilePath);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFilePath);
                throw;
            }
        }

        if (!IsSameVolume(sourceFilePath, targetFilePath))
        {
            TransferAcrossVolumes(sourceFilePath, targetFilePath);
            return;
        }

        VideoRecordingMetadata sourceMetadata = VideoRecordingMetadataStore.Load(new FileInfo(sourceFilePath));
        File.Move(sourceFilePath, targetFilePath);
        try
        {
            if (VideoRecordingMetadataStore.HasAnyMetadata(sourceMetadata)
                && !VideoRecordingMetadataStore.WriteCompletedMetadata(targetFilePath, sourceMetadata))
            {
                throw new IOException("Failed to write recording metadata after moving the video.");
            }
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(sourceFilePath);
        }
        catch
        {
            try
            {
                if (File.Exists(targetFilePath) && !File.Exists(sourceFilePath))
                {
                    File.Move(targetFilePath, sourceFilePath);
                }
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFilePath);
            }
            catch (Exception rollbackException)
            {
                AppSessionLogger.WriteException(rollbackException);
            }
            throw;
        }
    }

    internal static void RenameVideoFile(string sourceFilePath, string targetFilePath)
    {
        VideoRecordingMetadata metadata = VideoRecordingMetadataStore.Load(new FileInfo(sourceFilePath));
        File.Move(sourceFilePath, targetFilePath);
        try
        {
            if (VideoRecordingMetadataStore.HasAnyMetadata(metadata))
            {
                if (!VideoRecordingMetadataStore.WriteCompletedMetadata(targetFilePath, metadata))
                {
                    throw new IOException("Failed to write recording metadata after renaming the video.");
                }
            }

            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(sourceFilePath);
            DeleteThumbnailCacheIfUnused(sourceFilePath);
        }
        catch
        {
            if (File.Exists(targetFilePath) && !File.Exists(sourceFilePath))
            {
                File.Move(targetFilePath, sourceFilePath);
            }
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFilePath);
            throw;
        }
    }

    internal static bool DeleteThumbnailCacheIfUnused(string filePath, string? cacheDirectory = null)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(filePath);
        if (Directory.Exists(directory)
            && Directory.EnumerateFiles(directory, stem + ".*", System.IO.SearchOption.TopDirectoryOnly).Any(MediaFileCatalog.IsMediaPath))
        {
            return false;
        }

        string cachePath = cacheDirectory == null
            ? GetThumbnailCachePath(filePath)
            : GetThumbnailCachePath(filePath, cacheDirectory);
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            File.Delete(cachePath);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
            return false;
        }
    }

    private static bool IsSameVolume(string sourceFilePath, string targetFilePath)
    {
        string sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourceFilePath)) ?? string.Empty;
        string targetRoot = Path.GetPathRoot(Path.GetFullPath(targetFilePath)) ?? string.Empty;
        return string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TransferAcrossVolumes(string sourceFilePath, string targetFilePath)
    {
        string temporaryTargetPath = MediaFileCatalog.CreateTemporaryPath(targetFilePath, "move");
        bool targetCommitted = false;
        try
        {
            CopyAndVerifyFile(sourceFilePath, temporaryTargetPath);

            File.Move(temporaryTargetPath, targetFilePath, overwrite: false);
            targetCommitted = true;
            CopyAssociatedMetadata(sourceFilePath, targetFilePath);
            File.Delete(sourceFilePath);
            VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(sourceFilePath);
        }
        catch
        {
            DeleteFileIfExists(temporaryTargetPath);
            if (targetCommitted)
            {
                DeleteFileIfExists(targetFilePath);
                VideoRecordingMetadataStore.TryDeleteSidecarIfNoSourceVideosRemain(targetFilePath);
            }
            throw;
        }
    }

    private static void CopyAndVerifyFile(string sourceFilePath, string targetFilePath)
    {
        File.Copy(sourceFilePath, targetFilePath, overwrite: false);
        using FileStream source = new(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        using FileStream target = new(targetFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        if (source.Length != target.Length)
        {
            throw new IOException("The copied video length does not match the source.");
        }

        byte[] sourceHash = System.Security.Cryptography.SHA256.HashData(source);
        byte[] targetHash = System.Security.Cryptography.SHA256.HashData(target);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(sourceHash, targetHash))
        {
            throw new IOException("The copied video content does not match the source.");
        }
    }

    private static bool CanModifyVideo(RecordedVideoItem item)
    {
        return item.CanModify && !MediaOperationRegistry.IsPathProtected(item.FullPath);
    }

    private static void UpdateOperationState(RecordedVideoItem item)
    {
        item.IsInProgress = MediaOperationRegistry.IsPathProtected(item.FullPath);
        item.IsConverting = MediaOperationRegistry.IsPathProtectedBy(MediaOperationKind.Conversion, item.FullPath);
        item.IsRecordingFile = MediaOperationRegistry.IsPathProtectedBy(MediaOperationKind.Recording, item.FullPath);
        if (item.IsRecordingFile)
        {
            item.IsSelected = false;
        }
    }

    private static bool CanModifyVideoForUserOperation(RecordedVideoItem item)
    {
        bool isConverting = item.IsConverting || MediaOperationRegistry.IsPathProtectedBy(MediaOperationKind.Conversion, item.FullPath);
        if (!item.CanModify && !isConverting)
        {
            return false;
        }

        return !MediaOperationRegistry.IsPathProtectedBy(MediaOperationKind.Recording, item.FullPath)
            && !MediaOperationRegistry.IsPathProtectedBy(MediaOperationKind.Split, item.FullPath)
            && !MediaOperationRegistry.IsPathProtectedBy(MediaOperationKind.Merge, item.FullPath);
    }

    private async Task<bool> CancelConversionsForUserOperationAsync(IReadOnlyCollection<RecordedVideoItem> items)
    {
        string[] paths = items
            .Select(item => item.FullPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            return true;
        }

        int cancelled = MediaOperationRegistry.Cancel(MediaOperationKind.Conversion, paths);
        if (cancelled <= 0)
        {
            return true;
        }

        OperationProgressText = "CancellingTranscode".Tr();
        bool released = await MediaOperationRegistry.WaitForPathReleaseAsync(MediaOperationKind.Conversion, paths, TimeSpan.FromSeconds(8));
        foreach (RecordedVideoItem item in items)
        {
            UpdateOperationState(item);
        }

        if (!released)
        {
            Toast.Warning("TranscodeStopping".Tr());
            return false;
        }

        return true;
    }

    internal static string InferNickName(string filePath, string rootFolder)
    {
        FileInfo fileInfo = new(filePath);
        string fallback = fileInfo.Directory?.Name ?? "-";

        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            return fallback;
        }

        if (!PathUtility.TryGetRelativePathWithinRoot(filePath, rootFolder, out string relativePath)
            || relativePath == ".")
        {
            return fallback;
        }

        string? relativeDirectory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(relativeDirectory))
        {
            return fallback;
        }

        string[] parts = relativeDirectory
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return fallback;
        }

        if (parts.Length >= 2 && IsYearMonth(parts[1]))
        {
            return parts[0];
        }

        return parts[^1];
    }

    private static (int Succeeded, int Failed) ImportVideos(string sourceFolder, string root)
    {
        Directory.CreateDirectory(root);
        int succeeded = 0;
        int failed = 0;
        foreach (string path in EnumerateVideoFiles(sourceFolder).Where(IsVideoFile))
        {
            try
            {
                FileInfo source = new(path);
                VideoRecordingMetadata metadata = LoadMetadata(source);
                string targetFolder = BuildClassifiedFolder(root, metadata, source, sourceFolder);
                Directory.CreateDirectory(targetFolder);
                string targetPath = GetUniquePath(Path.Combine(targetFolder, source.Name));
                TransferVideoFile(source.FullName, targetPath, move: false);
                succeeded++;
            }
            catch (Exception e)
            {
                AppSessionLogger.WriteException(e);
                failed++;
            }
        }

        return (succeeded, failed);
    }

    internal static IEnumerable<string> EnumerateVideoFiles(string folder, CancellationToken cancellationToken = default)
    {
        try
        {
            List<string> files = [];
            foreach (string path in Directory.EnumerateFiles(folder, "*.*", RecursiveEnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.Add(path);
            }
            return files;
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static string BuildClassifiedFolder(string root, VideoRecordingMetadata metadata, FileInfo file, string sourceRoot, int? pathLevel = null)
    {
        string nickName = string.IsNullOrWhiteSpace(metadata.NickName) ? InferNickName(file.FullName, sourceRoot) : metadata.NickName;
        string author = SanitizeFolderName(nickName);
        DateTime recordedAt = metadata.RecordedAt > DateTime.MinValue ? metadata.RecordedAt : file.LastWriteTime;
        string month = recordedAt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        string day = recordedAt.ToString("dd", CultureInfo.InvariantCulture);
        return Math.Clamp(pathLevel ?? Configurations.SaveFolderPathLevel.Get(), 0, 3) switch
        {
            1 => Path.Combine(root, author),
            2 => Path.Combine(root, author, month),
            3 => Path.Combine(root, author, month, day),
            _ => root,
        };
    }

    private static string SanitizeFolderName(string? value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? UnknownStreamerText : value.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        name = name.TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? UnknownStreamerText : name;
    }

    internal static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int index = 1; index < 10000; index++)
        {
            string candidate = Path.Combine(directory, $"{name}_{index:000}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
    }

    private static bool IsSameOrAncestorDirectory(string parent, string child)
    {
        return PathUtility.IsSameOrDescendant(child, parent);
    }

    private static bool IsVideoFile(string path)
    {
        return MediaFileCatalog.IsMediaPath(path) && !MediaFileCatalog.IsApplicationTemporaryPath(path);
    }

    private static string BuildDisplayTitle(string? title, DateTime createdAt, FileInfo file)
    {
        string details = $"{createdAt:yyyy-MM-dd HH:mm:ss}  |  {FormatFileSize(file.Length)}";
        return string.IsNullOrWhiteSpace(title) ? $"{details}  |  {file.DirectoryName}" : $"{title}  |  {details}";
    }

    private static bool IsYearMonth(string value)
    {
        return value.Length == 7
            && value[4] == '-'
            && int.TryParse(value[..4], out int year)
            && int.TryParse(value[5..], out int month)
            && year is >= 1900 and <= 9999
            && month is >= 1 and <= 12;
    }

    private void ApplySort()
    {
        Videos.SortDescriptions.Clear();
        Videos.SortDescriptions.Add(new SortDescription(
            nameof(RecordedVideoItem.CreatedAt),
            IsSortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        Videos.Refresh();
        VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyFilters()
    {
        Videos.Refresh();
        NormalizeSelectionForVisibleVideos();
        NormalizeSelectionOrders();
        RefreshSelectionSummary();
        VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QueueVideoViewRefresh()
    {
        if (Interlocked.Exchange(ref videoViewRefreshPending, 1) != 0)
        {
            return;
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref videoViewRefreshPending, 0);
            return;
        }

        _ = dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref videoViewRefreshPending, 0);
            using (Videos.DeferRefresh())
            {
                Videos.Refresh();
            }
            VisibleItemsChanged?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Background);
    }

    private void NormalizeSelectionForVisibleVideos()
    {
        OnPropertyChanged(nameof(HasVisibleVideos));
        if (HasVisibleVideos)
        {
            return;
        }

        isRestoringSelection = true;
        try
        {
            foreach (RecordedVideoItem item in videos)
            {
                item.IsSelected = false;
            }
        }
        finally
        {
            isRestoringSelection = false;
        }

        NormalizeSelectionOrders();
        IsMultiSelectMode = false;
        RegularSelectedItem = null;
        lastSelectedItem = null;
    }

    private bool FilterVideo(object item)
    {
        if (item is not RecordedVideoItem video)
        {
            return false;
        }

        bool streamerMatched = string.IsNullOrWhiteSpace(SelectedStreamer)
            || string.Equals(SelectedStreamer, allStreamerOption, StringComparison.Ordinal)
            || string.Equals(video.NickName, SelectedStreamer, StringComparison.OrdinalIgnoreCase);
        if (!streamerMatched)
        {
            return false;
        }

        DateTime threshold = SelectedTimeRangeIndex switch
        {
            1 => DateTime.Now.AddDays(-1),
            2 => DateTime.Now.AddDays(-7),
            3 => DateTime.Now.AddDays(-30),
            4 => DateTime.Now.AddDays(-90),
            5 => DateTime.Now.AddDays(-365),
            _ => DateTime.MinValue,
        };

        return threshold == DateTime.MinValue || video.CreatedAt >= threshold;
    }

    private void UpdateStreamerOptions()
    {
        string currentSelection = SelectedStreamer;
        string[] streamers = videos
            .Select(video => video.NickName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        StreamerOptions.Clear();
        StreamerOptions.Add(allStreamerOption);
        foreach (string streamer in streamers)
        {
            StreamerOptions.Add(streamer);
        }

        SelectedStreamer = StreamerOptions.FirstOrDefault(option =>
            string.Equals(option, currentSelection, StringComparison.OrdinalIgnoreCase)) ?? allStreamerOption;
    }

    private void VideoItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordedVideoItem.IsSelected) && !isRestoringSelection)
        {
            if (!isApplyingSelectionChange)
            {
                NormalizeSelectionOrders();
            }
            RefreshSelectionSummary();
        }
        else if (e.PropertyName == nameof(RecordedVideoItem.NickName))
        {
            UpdateStreamerOptions();
            ApplyFilters();
        }
    }

    private void RefreshSelectionSummary()
    {
        OnPropertyChanged(nameof(SelectedVideoCount));
        OnPropertyChanged(nameof(SelectedVideoSummary));
        OnPropertyChanged(nameof(HasSelectedVideos));
        OnPropertyChanged(nameof(CanMergeSelectedVideos));
    }

    private RecordedVideoItem[] GetSelectedVideos()
    {
        return GetVisibleVideos().Where(video => video.CanSelect && video.IsSelected && File.Exists(video.FullPath)).ToArray();
    }

    private RecordedVideoItem[] GetContextVideos(RecordedVideoItem? item)
    {
        RecordedVideoItem[] selected = GetSelectedVideos();
        if (IsMultiSelectMode && selected.Length > 0)
        {
            return selected;
        }

        return item != null && item.CanSelect && File.Exists(item.FullPath) ? [item] : [];
    }

    private RecordedVideoItem[] GetVisibleVideos()
    {
        return Videos.Cast<RecordedVideoItem>().ToArray();
    }

    private void ApplySelectionChange(Action change)
    {
        string[] before = CaptureSelectedPaths();
        bool beforeMultiSelectMode = IsMultiSelectMode;
        isApplyingSelectionChange = true;
        try
        {
            change();
        }
        finally
        {
            isApplyingSelectionChange = false;
        }
        NormalizeSelectionOrders();
        if (!HasVisibleVideos)
        {
            IsMultiSelectMode = false;
        }
        string[] after = CaptureSelectedPaths();
        bool afterMultiSelectMode = IsMultiSelectMode;
        if (!before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase) || beforeMultiSelectMode != afterMultiSelectMode)
        {
            selectionUndoStack.Push(new SelectionSnapshot(before, after, beforeMultiSelectMode, afterMultiSelectMode));
            while (selectionUndoStack.Count > SelectionHistoryLimit)
            {
                SelectionSnapshot[] snapshots = selectionUndoStack.Reverse().Skip(1).ToArray();
                selectionUndoStack.Clear();
                foreach (SelectionSnapshot snapshot in snapshots)
                {
                    selectionUndoStack.Push(snapshot);
                }
            }
            selectionRedoStack.Clear();
        }

        RefreshSelectionSummary();
    }

    private string[] CaptureSelectedPaths()
    {
        return videos.Where(video => video.CanSelect && video.IsSelected)
            .OrderBy(video => video.UserSelectionOrder > 0 ? video.UserSelectionOrder : int.MaxValue)
            .Select(video => video.FullPath)
            .ToArray();
    }

    private void RestoreSelection(IReadOnlyList<string> selectedPaths, bool multiSelectMode)
    {
        bool canRestoreMultiSelect = multiSelectMode && HasVisibleVideos;
        Dictionary<string, int> selectedOrderByPath = selectedPaths
            .Select((path, index) => new KeyValuePair<string, int>(path, index + 1))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        isRestoringSelection = true;
        try
        {
            foreach (RecordedVideoItem item in videos)
            {
                int selectionOrder = 0;
                item.IsSelected = canRestoreMultiSelect && item.CanSelect && selectedOrderByPath.TryGetValue(item.FullPath, out selectionOrder);
                item.UserSelectionOrder = item.IsSelected ? selectionOrder : 0;
                item.SelectionOrder = item.IsSelected ? selectionOrder : 0;
            }
        }
        finally
        {
            isRestoringSelection = false;
        }

        NormalizeSelectionOrders();
        lastSelectedItem = GetLastSelectedItem();
        IsMultiSelectMode = canRestoreMultiSelect;
        RefreshSelectionSummary();
    }

    private void SetItemSelection(RecordedVideoItem item, bool isSelected)
    {
        bool canSelect = isSelected && item.CanSelect;
        item.IsSelected = canSelect;
        if (!canSelect)
        {
            item.UserSelectionOrder = 0;
            item.SelectionOrder = 0;
            return;
        }

        if (item.UserSelectionOrder <= 0)
        {
            item.UserSelectionOrder = videos
                .Where(video => video.IsSelected)
                .Select(video => video.UserSelectionOrder)
                .DefaultIfEmpty(0)
                .Max() + 1;
        }
    }

    private void NormalizeSelectionOrders()
    {
        RecordedVideoItem[] selectedByUser = videos
            .Where(video => video.CanSelect && video.IsSelected)
            .OrderBy(video => video.UserSelectionOrder > 0 ? video.UserSelectionOrder : int.MaxValue)
            .ToArray();
        HashSet<RecordedVideoItem> selectedItems = selectedByUser.ToHashSet();
        foreach (RecordedVideoItem item in videos)
        {
            if (!selectedItems.Contains(item))
            {
                item.UserSelectionOrder = 0;
                item.SelectionOrder = 0;
            }
        }

        for (int index = 0; index < selectedByUser.Length; index++)
        {
            selectedByUser[index].UserSelectionOrder = index + 1;
        }

        RecordedVideoItem[] visibleSelectedByUser = GetVisibleVideos()
            .Where(video => video.CanSelect && video.IsSelected)
            .OrderBy(video => video.UserSelectionOrder)
            .ToArray();
        foreach (RecordedVideoItem item in selectedByUser)
        {
            item.SelectionOrder = 0;
        }

        RecordedVideoItem[] effectiveOrder = IsSingleSegmentSeries(visibleSelectedByUser)
            ? OrderVideosForMerge(visibleSelectedByUser).ToArray()
            : visibleSelectedByUser;
        for (int index = 0; index < effectiveOrder.Length; index++)
        {
            effectiveOrder[index].SelectionOrder = index + 1;
        }
    }

    private RecordedVideoItem? GetLastSelectedItem()
    {
        return GetVisibleVideos()
            .Where(item => item.IsSelected)
            .MaxBy(item => item.UserSelectionOrder);
    }

    private bool TryGetSplitDurationSeconds(out int seconds)
    {
        return TryConvertSplitDurationSeconds(SplitDurationValue, SplitDurationUnitIndex, out seconds);
    }

    internal static bool TryConvertSplitDurationSeconds(string? valueText, int unitIndex, out int seconds)
    {
        seconds = 0;
        if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            || !double.IsFinite(value)
            || value <= 0)
        {
            return false;
        }

        double multiplier = unitIndex switch
        {
            1 => 1d,
            2 => 3600d,
            0 or _ => 60d,
        };
        double totalSeconds = Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(totalSeconds) || totalSeconds < 1d || totalSeconds > int.MaxValue)
        {
            return false;
        }

        seconds = (int)totalSeconds;
        return true;
    }

    internal static string BuildMergeWarningText(IReadOnlyList<RecordedVideoItem> selected)
    {
        List<string> reasons = [];
        bool isSingleSegmentSeries = IsSingleSegmentSeries(selected);
        if (selected.Count < 2)
        {
            reasons.Add("MergeReasonAtLeastTwoVideos".Tr());
        }
        if (selected.Select(video => video.NickName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            reasons.Add("MergeReasonDifferentStreamers".Tr());
        }
        if (selected.Select(video => Path.GetExtension(video.FullPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            reasons.Add("MergeReasonDifferentFormats".Tr());
        }
        if (selected.Select(video => video.Resolution).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            reasons.Add("MergeReasonDifferentResolutions".Tr());
        }
        if (isSingleSegmentSeries && !IsContinuousSegmentSelection(selected))
        {
            reasons.Add("MergeReasonNonContiguousSegments".Tr());
        }

        string orderText = isSingleSegmentSeries ? "MergeBySegmentNumber".Tr() : "MergeBySelectionOrder".Tr();
        return reasons.Count == 0
            ? "MergeValidationPassed".Tr(orderText)
            : "MergeValidationWarnings".Tr(string.Join("MergeReasonSeparator".Tr(), reasons.Distinct()), orderText);
    }

    internal static IEnumerable<RecordedVideoItem> OrderVideosForMerge(IEnumerable<RecordedVideoItem> source)
    {
        RecordedVideoItem[] items = source.ToArray();
        if (IsSingleSegmentSeries(items))
        {
            return items.OrderBy(item =>
            {
                _ = TryGetSegmentIdentity(item.FullPath, out _, out int index);
                return index;
            });
        }

        if (items.Length > 0
            && items.All(item => item.SelectionOrder > 0)
            && items.Select(item => item.SelectionOrder).Distinct().Count() == items.Length)
        {
            return items.OrderBy(item => item.SelectionOrder);
        }

        return items.OrderBy(item => item.CreatedAt).ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSingleSegmentSeries(IReadOnlyList<RecordedVideoItem> items)
    {
        if (items.Count == 0 || items.Any(item => !TryGetSegmentIdentity(item.FullPath, out _, out _)))
        {
            return false;
        }

        return items.Select(item =>
        {
            _ = TryGetSegmentIdentity(item.FullPath, out string baseStem, out _);
            return baseStem;
        }).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
    }

    internal static bool HaveCompatibleMergeStreams(IReadOnlyList<RecordedVideoItem> selected)
    {
        string[] signatures = selected
            .Select(video => ProbeMergeStreamSignature(video.FullPath))
            .ToArray();
        return signatures.Length >= 2
            && signatures.All(signature => !string.IsNullOrWhiteSpace(signature))
            && signatures.Distinct(StringComparer.Ordinal).Count() == 1;
    }

    private static string ProbeMergeStreamSignature(string filePath)
    {
        if (!File.Exists(filePath)
            || !FfmpegMediaEngine.TryProbe(filePath, out FfmpegMediaProbeResult result, out _))
        {
            return string.Empty;
        }

        return result.StreamSignature;
    }

    internal static string ParseMergeStreamSignature(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("streams", out JsonElement streams)
            || streams.ValueKind != JsonValueKind.Array
            || streams.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        string[] properties = ["codec_type", "codec_name", "profile", "width", "height", "pix_fmt", "time_base", "sample_rate", "channels", "channel_layout"];
        return string.Join(";", streams.EnumerateArray().Select(stream => string.Join("|", properties.Select(property =>
            stream.TryGetProperty(property, out JsonElement value) ? value.ToString() : string.Empty))));
    }

    private static bool IsContinuousSegmentSelection(IReadOnlyList<RecordedVideoItem> selected)
    {
        if (selected.Count < 2)
        {
            return false;
        }

        List<(string BaseStem, int Index)> segments = [];
        foreach (RecordedVideoItem item in selected)
        {
            if (!TryGetSegmentIdentity(item.FullPath, out string baseStem, out int index))
            {
                return false;
            }
            segments.Add((baseStem, index));
        }

        if (segments.Select(segment => segment.BaseStem).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            return false;
        }

        int[] indexes = segments.Select(segment => segment.Index).Order().ToArray();
        return indexes.Skip(1).Select((index, offset) => index == indexes[offset] + 1).All(value => value);
    }

    private static bool TryGetSegmentIdentity(string path, out string baseStem, out int index)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        int separator = stem.LastIndexOf('_');
        if (separator > 0 && separator < stem.Length - 1
            && int.TryParse(stem[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            baseStem = stem[..separator];
            return true;
        }

        baseStem = string.Empty;
        index = 0;
        return false;
    }

    private static async Task<bool> SplitVideoFileAsync(RecordedVideoItem item, int seconds)
    {
        using CancellationTokenSource operationCancellation = new(TimeSpan.FromHours(12));
        MediaFileWorkflowResult result;
        try
        {
            result = await MediaFileWorkflow.SplitAsync(item.FullPath, seconds, operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            AppSessionLogger.Event("error", "video_list", "ffmpeg_operation_failed", result.Error);
        }
        return result.Success;
    }

    private static async Task<bool> MergeVideosAsync(IReadOnlyList<RecordedVideoItem> selected, string targetFolder, Action<double> progress)
    {
        RecordedVideoItem[] ordered = OrderVideosForMerge(selected).ToArray();
        using CancellationTokenSource operationCancellation = new(TimeSpan.FromHours(12));
        Progress<double> operationProgress = new(progress);
        MediaFileWorkflowResult result;
        try
        {
            result = await MediaFileWorkflow.MergeAsync(
                ordered.Select(item => item.FullPath).ToArray(),
                targetFolder,
                operationProgress,
                operationCancellation.Token,
                validateStreams: false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            AppSessionLogger.Event("error", "video_list", "ffmpeg_operation_failed", result.Error);
        }
        return result.Success;
    }

    private static bool TryReadProcessOutput(Process process, int timeoutMilliseconds, out string output)
    {
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task completionTask = Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());
        Task completedTask = Task.WhenAny(completionTask, Task.Delay(timeoutMilliseconds)).GetAwaiter().GetResult();
        if (!ReferenceEquals(completedTask, completionTask))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
            }
            _ = completionTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            output = string.Empty;
            return false;
        }

        completionTask.GetAwaiter().GetResult();
        output = outputTask.Result;
        return true;
    }

    private static async Task<(bool Succeeded, string Error)> ExecuteFfmpegAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        Func<string, Task>? outputLine = null)
    {
        await Task.CompletedTask;
        return (false, "ffmpeg command-line backend is not available");
    }

    private static async Task ReadFfmpegOutputAsync(StreamReader reader, Func<string, Task>? outputLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (outputLine != null)
            {
                await outputLine(line);
            }
        }
    }

    private static string EscapeConcatPath(string path)
    {
        return path.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "'\\''", StringComparison.Ordinal);
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    internal static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int index = 0;

        while (value >= 1024d && index < units.Length - 1)
        {
            value /= 1024d;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }
}

internal readonly record struct VideoProbeInfo(string Resolution, string Bitrate, VideoRecordingMetadata? Metadata)
{
    public static VideoProbeInfo Empty { get; } = new(string.Empty, string.Empty, null);
}

internal readonly record struct VideoFileSnapshot(
    string Path,
    string RootFolder,
    long Length,
    DateTime LastWriteTimeUtc,
    DateTime MetadataLastWriteTimeUtc);

internal sealed record SelectionSnapshot(
    string[] Before,
    string[] After,
    bool BeforeMultiSelectMode,
    bool AfterMultiSelectMode);

public sealed class VideoDateGroupLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime date)
        {
            return string.Empty;
        }

        CultureInfo currentCulture = Locale.Culture;
        return currentCulture.TwoLetterISOLanguageName is "zh" or "ja"
            ? $"{date.Month}月{date.Day}日"
            : date.ToString("MMM d", currentCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return System.Windows.Data.Binding.DoNothing;
    }
}

public partial class RecordedVideoItem : ObservableObject
{
    internal int UserSelectionOrder { get; set; }

    internal bool SupportsTranscode { get; init; }

    internal long SourceLength { get; init; }

    internal DateTime SourceLastWriteTimeUtc { get; init; }

    internal DateTime MetadataLastWriteTimeUtc { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UiXWrappedFileName))]
    private string fileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatText))]
    [NotifyPropertyChangedFor(nameof(IsTargetFormat))]
    private string fullPath = string.Empty;

    [ObservableProperty]
    private string directoryPath = string.Empty;

    [ObservableProperty]
    private string roomUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StreamerChipText))]
    [NotifyPropertyChangedFor(nameof(UiXStreamerText))]
    [NotifyPropertyChangedFor(nameof(UiXSummaryText))]
    private string nickName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolutionChipText))]
    private string resolution = "-";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateChipText))]
    private string bitrate = "-";

    [ObservableProperty]
    private string coverPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    private string thumbnailPath = string.Empty;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordingDetailsText))]
    [NotifyPropertyChangedFor(nameof(RecordingTimeText))]
    [NotifyPropertyChangedFor(nameof(UiXSummaryText))]
    [NotifyPropertyChangedFor(nameof(DateGroupKey))]
    private DateTime createdAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTargetFormat))]
    private string targetRecordFormat = "TS/FLV";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanModify))]
    [NotifyPropertyChangedFor(nameof(CanTranscode))]
    [NotifyPropertyChangedFor(nameof(CanRepair))]
    private bool isInProgress;

    [ObservableProperty]
    private bool isConverting;

    [ObservableProperty]
    private bool isStallSegment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    [NotifyPropertyChangedFor(nameof(CanModify))]
    [NotifyPropertyChangedFor(nameof(CanTranscode))]
    [NotifyPropertyChangedFor(nameof(CanRepair))]
    private bool isRecordingFile;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private int selectionOrder;

    [ObservableProperty]
    private bool isEnriched;

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);

    public string UiXWrappedFileName => string.Concat(FileName.Select(character => $"{character}\u200B"));

    public DateTime DateGroupKey => CreatedAt.Date;

    public string FormatText => ScreenRecordListViewModel.GetVideoFormatText(FullPath);

    public bool IsTargetFormat => ScreenRecordListViewModel.IsTargetVideoFormat(FullPath, TargetRecordFormat);

    public bool CanModify => !IsInProgress && !IsRecordingFile;

    public bool CanTranscode => SupportsTranscode && !IsInProgress && !IsRecordingFile;

    public bool CanRepair => SupportsTranscode && !IsInProgress && !IsRecordingFile;

    public bool CanSelect => !IsRecordingFile;

    partial void OnIsRecordingFileChanged(bool value)
    {
        if (value)
        {
            IsSelected = false;
            UserSelectionOrder = 0;
            SelectionOrder = 0;
        }
    }

    internal void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(StreamerChipText));
        OnPropertyChanged(nameof(UiXStreamerText));
        OnPropertyChanged(nameof(ResolutionChipText));
        OnPropertyChanged(nameof(BitrateChipText));
        OnPropertyChanged(nameof(RecordingDetailsText));
        OnPropertyChanged(nameof(UiXSummaryText));
    }

    public string StreamerChipText => ScreenRecordListViewModel.FormatResourceText(
        "StreamerChip",
        "Streamer {0}",
        string.IsNullOrWhiteSpace(NickName) ? ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown") : NickName);

    public string UiXStreamerText => string.IsNullOrWhiteSpace(NickName)
        ? ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown")
        : NickName;

    public string ResolutionChipText => ScreenRecordListViewModel.FormatResourceText(
        "ResolutionChip",
        "Resolution {0}",
        string.IsNullOrWhiteSpace(Resolution) ? ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown") : Resolution);

    public string BitrateChipText => ScreenRecordListViewModel.FormatResourceText(
        "BitrateChip",
        "Bitrate {0}",
        string.IsNullOrWhiteSpace(Bitrate) ? ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown") : Bitrate);

    public string RecordingDetailsText => $"{CreatedAt:yyyy-MM-dd HH:mm:ss}  ·  {ScreenRecordListViewModel.FormatFileSize(SourceLength)}";

    public string RecordingTimeText => CreatedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public string FileSizeText => ScreenRecordListViewModel.FormatFileSize(SourceLength);

    public string UiXSummaryText => $"{(string.IsNullOrWhiteSpace(NickName) ? ScreenRecordListViewModel.GetResourceText("CommonUnknown", "Unknown") : NickName)}  ·  {RecordingDetailsText}";
}
