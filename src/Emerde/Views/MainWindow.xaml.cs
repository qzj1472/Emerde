using Microsoft.Toolkit.Uwp.Notifications;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Emerde.Core;
using Emerde.ViewModels;
using Vanara.PInvoke;
using Wpf.Ui.Controls;
using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Emerde.Views;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public static readonly DependencyProperty RoomCardItemWidthProperty = DependencyProperty.Register(nameof(RoomCardItemWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(196d));
    public static readonly DependencyProperty RoomCardItemHeightProperty = DependencyProperty.Register(nameof(RoomCardItemHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(132d));
    public static readonly DependencyProperty RoomCardPanelWidthProperty = DependencyProperty.Register(nameof(RoomCardPanelWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(196d));
    public static readonly DependencyProperty RoomCardWidthProperty = DependencyProperty.Register(nameof(RoomCardWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(184d));
    public static readonly DependencyProperty RoomCardHeightProperty = DependencyProperty.Register(nameof(RoomCardHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(122d));
    public static readonly DependencyProperty RoomCardPaddingProperty = DependencyProperty.Register(nameof(RoomCardPadding), typeof(Thickness), typeof(MainWindow), new PropertyMetadata(new Thickness(8)));
    public static readonly DependencyProperty RoomCardMarginProperty = DependencyProperty.Register(nameof(RoomCardMargin), typeof(Thickness), typeof(MainWindow), new PropertyMetadata(new Thickness(4)));
    public static readonly DependencyProperty RoomCardAvatarSizeProperty = DependencyProperty.Register(nameof(RoomCardAvatarSize), typeof(double), typeof(MainWindow), new PropertyMetadata(32d));
    public static readonly DependencyProperty RoomCardAvatarContainerSizeProperty = DependencyProperty.Register(nameof(RoomCardAvatarContainerSize), typeof(double), typeof(MainWindow), new PropertyMetadata(36d));
    public static readonly DependencyProperty RoomCardAvatarIconSizeProperty = DependencyProperty.Register(nameof(RoomCardAvatarIconSize), typeof(double), typeof(MainWindow), new PropertyMetadata(18d));
    public static readonly DependencyProperty RoomCardHeaderColumnWidthProperty = DependencyProperty.Register(nameof(RoomCardHeaderColumnWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(38)));
    public static readonly DependencyProperty RoomCardAvatarMarginProperty = DependencyProperty.Register(nameof(RoomCardAvatarMargin), typeof(Thickness), typeof(MainWindow), new PropertyMetadata(new Thickness(3, 3, 10, 0)));
    public static readonly DependencyProperty RoomCardNameFontSizeProperty = DependencyProperty.Register(nameof(RoomCardNameFontSize), typeof(double), typeof(MainWindow), new PropertyMetadata(13d));
    public static readonly DependencyProperty RoomCardPlatformFontSizeProperty = DependencyProperty.Register(nameof(RoomCardPlatformFontSize), typeof(double), typeof(MainWindow), new PropertyMetadata(11d));
    public static readonly DependencyProperty RoomCardTitleFontSizeProperty = DependencyProperty.Register(nameof(RoomCardTitleFontSize), typeof(double), typeof(MainWindow), new PropertyMetadata(11d));
    public static readonly DependencyProperty RoomCardTitleLineHeightProperty = DependencyProperty.Register(nameof(RoomCardTitleLineHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(15d));
    public static readonly DependencyProperty RoomCardTitleMaxHeightProperty = DependencyProperty.Register(nameof(RoomCardTitleMaxHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(30d));
    public static readonly DependencyProperty RoomCardTitleVisibilityProperty = DependencyProperty.Register(nameof(RoomCardTitleVisibility), typeof(Visibility), typeof(MainWindow), new PropertyMetadata(Visibility.Visible));
    public static readonly DependencyProperty RoomCardChipFontSizeProperty = DependencyProperty.Register(nameof(RoomCardChipFontSize), typeof(double), typeof(MainWindow), new PropertyMetadata(11d));
    public static readonly DependencyProperty RoomCardChipPaddingProperty = DependencyProperty.Register(nameof(RoomCardChipPadding), typeof(Thickness), typeof(MainWindow), new PropertyMetadata(new Thickness(4, 1, 4, 1)));
    public static readonly DependencyProperty RoomCardChipMinHeightProperty = DependencyProperty.Register(nameof(RoomCardChipMinHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(20d));

    public double RoomCardItemWidth
    {
        get => (double)GetValue(RoomCardItemWidthProperty);
        set => SetValue(RoomCardItemWidthProperty, value);
    }

    public double RoomCardItemHeight
    {
        get => (double)GetValue(RoomCardItemHeightProperty);
        set => SetValue(RoomCardItemHeightProperty, value);
    }

    public double RoomCardPanelWidth
    {
        get => (double)GetValue(RoomCardPanelWidthProperty);
        set => SetValue(RoomCardPanelWidthProperty, value);
    }

    public double RoomCardWidth
    {
        get => (double)GetValue(RoomCardWidthProperty);
        set => SetValue(RoomCardWidthProperty, value);
    }

    public double RoomCardHeight
    {
        get => (double)GetValue(RoomCardHeightProperty);
        set => SetValue(RoomCardHeightProperty, value);
    }

    public Thickness RoomCardPadding
    {
        get => (Thickness)GetValue(RoomCardPaddingProperty);
        set => SetValue(RoomCardPaddingProperty, value);
    }

    public Thickness RoomCardMargin
    {
        get => (Thickness)GetValue(RoomCardMarginProperty);
        set => SetValue(RoomCardMarginProperty, value);
    }

    public double RoomCardAvatarSize
    {
        get => (double)GetValue(RoomCardAvatarSizeProperty);
        set => SetValue(RoomCardAvatarSizeProperty, value);
    }

    public double RoomCardAvatarContainerSize
    {
        get => (double)GetValue(RoomCardAvatarContainerSizeProperty);
        set => SetValue(RoomCardAvatarContainerSizeProperty, value);
    }

    public double RoomCardAvatarIconSize
    {
        get => (double)GetValue(RoomCardAvatarIconSizeProperty);
        set => SetValue(RoomCardAvatarIconSizeProperty, value);
    }

    public GridLength RoomCardHeaderColumnWidth
    {
        get => (GridLength)GetValue(RoomCardHeaderColumnWidthProperty);
        set => SetValue(RoomCardHeaderColumnWidthProperty, value);
    }

    public Thickness RoomCardAvatarMargin
    {
        get => (Thickness)GetValue(RoomCardAvatarMarginProperty);
        set => SetValue(RoomCardAvatarMarginProperty, value);
    }

    public double RoomCardNameFontSize
    {
        get => (double)GetValue(RoomCardNameFontSizeProperty);
        set => SetValue(RoomCardNameFontSizeProperty, value);
    }

    public double RoomCardPlatformFontSize
    {
        get => (double)GetValue(RoomCardPlatformFontSizeProperty);
        set => SetValue(RoomCardPlatformFontSizeProperty, value);
    }

    public double RoomCardTitleFontSize
    {
        get => (double)GetValue(RoomCardTitleFontSizeProperty);
        set => SetValue(RoomCardTitleFontSizeProperty, value);
    }

    public double RoomCardTitleLineHeight
    {
        get => (double)GetValue(RoomCardTitleLineHeightProperty);
        set => SetValue(RoomCardTitleLineHeightProperty, value);
    }

    public double RoomCardTitleMaxHeight
    {
        get => (double)GetValue(RoomCardTitleMaxHeightProperty);
        set => SetValue(RoomCardTitleMaxHeightProperty, value);
    }

    public Visibility RoomCardTitleVisibility
    {
        get => (Visibility)GetValue(RoomCardTitleVisibilityProperty);
        set => SetValue(RoomCardTitleVisibilityProperty, value);
    }

    public double RoomCardChipFontSize
    {
        get => (double)GetValue(RoomCardChipFontSizeProperty);
        set => SetValue(RoomCardChipFontSizeProperty, value);
    }

    public Thickness RoomCardChipPadding
    {
        get => (Thickness)GetValue(RoomCardChipPaddingProperty);
        set => SetValue(RoomCardChipPaddingProperty, value);
    }

    public double RoomCardChipMinHeight
    {
        get => (double)GetValue(RoomCardChipMinHeightProperty);
        set => SetValue(RoomCardChipMinHeightProperty, value);
    }

    private const int RoomCardNormalBaseColumns = 3;
    private const int RoomCardPreviewBaseColumns = 1;
    private const double HomeDetailPanelBaseMaxWidth = 360d;
    private const double HomeDetailPanelMaxWidthReductionRatio = 1d / 7d;
    private const double PreviewWideLayoutThreshold = 1300d;
    private const double PreviewDetailLayoutThreshold = 950d;
    private const double PreviewCompactLayoutThreshold = 760d;
    private const double PreviewWideRoomListWidth = 320d;
    private const double PreviewStandardRoomListWidth = 280d;
    private const double PreviewCompactRoomListWidth = 230d;
    private const double PreviewNarrowRoomListWidth = 190d;
    private const double PreviewWideDetailWidth = 260d;
    private const double PreviewStandardDetailWidth = 220d;
    private const double RoomCardMinScale = 0.86d;
    private const double RoomCardMaxScale = 1.14d;
    private const double RoomCardBoundaryTolerance = 1d;
    private const double RoomCardLargeSizeScale = 1.5d;
    private const double RoomCardMediumSizeScale = 1d;
    private const double RoomCardSmallSizeScale = 0.5d;
    private const double RoomCardMinimumAvatarSize = 18d;
    private const double RoomCardHorizontalGap = 12d;
    private const double RoomCardVerticalGap = 12d;
    private const double RoomCardSmallGapScale = 2d / 3d;
    private const double RoomCardScrollContentPadding = 6d;
    private const double RoomCardScrollBarReservedWidth = 17d;
    private const int RoomCardDragDelayMilliseconds = 260;
    private const int RoomCardBlankLongPressMilliseconds = 560;

    private double normalRoomCardBaseWidth;
    private bool isNormalRoomCardBaseWidthCaptured;
    private double previewRoomCardBaseWidth;
    private bool isPreviewRoomCardBaseWidthCaptured;
    private double roomCardSizePreference = RoomCardMediumSizeScale;
    private Point roomCardDragStart;
    private DateTime roomCardPressedAt = DateTime.MinValue;
    private RoomStatusReactive? draggedRoom;
    private ListBoxItem? draggedRoomItem;
    private Point roomCardDragOffset;
    private bool isRoomCardDragging;
    private DispatcherTimer? roomCardBlankPressTimer;
    private bool roomCardBlankPressCandidate;
    private Point roomCardBlankPressStart;
    private AdornerLayer? roomCardAdornerLayer;
    private DragPreviewAdorner? roomCardDragAdorner;
    private InsertionLineAdorner? roomCardInsertionAdorner;
    private int roomCardInsertionIndex = -1;
    private GridLength previewShellNavigationColumnWidth;
    private GridLength previewShellGapColumnWidth;
    private GridLength previewShellContentColumnWidth;
    private GridLength previewHomeRoomCardColumnWidth;
    private GridLength previewHomePreviewColumnWidth;
    private GridLength previewHomeDetailColumnWidth;
    private double previewHomeDetailColumnMaxWidth;
    private Thickness previewMainContentRootMargin;
    private Thickness previewShellContentPadding;
    private Thickness previewHomePreviewLayoutMargin;
    private Thickness previewHomePreviewPanelMargin;
    private CornerRadius previewShellContentCornerRadius;
    private Brush? previewShellContentBackground;
    private Visibility previewShellNavigationVisibility;
    private Visibility previewHomeActionBarVisibility;
    private Visibility previewRoomCardPanelVisibility;
    private Visibility previewRoomDetailPanelVisibility;
    private Visibility previewHomeStatusTrayVisibility;
    private Visibility previewShellTitleBarVisibility;
    private WindowState previewWindowState;
    private WindowStyle previewWindowStyle;
    private ResizeMode previewResizeMode;
    private bool previewTopmost;
    private double previewLeft;
    private double previewTop;
    private double previewWidth;
    private double previewHeight;
    private bool isPreviewFullScreen;
    private bool previousPreviewingState;

    public MainWindow()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        DataContext = ViewModel = new();
        WindowSizing.UseMainWindowAspectSize(this);
        InitializeComponent();
        previousPreviewingState = ViewModel.IsPreviewing;
        UpdateHomePreviewLayout();
        ViewModel.PropertyChanged += ViewModelPropertyChanged;
        PreviewKeyDown += MainWindowPreviewKeyDown;
        AppSessionLogger.Write($"perf MainWindow initialized in {stopwatch.ElapsedMilliseconds} ms");
        Loaded += (_, _) =>
        {
            UpdateHomePreviewLayout();
            AppSessionLogger.Write($"perf MainWindow loaded in {stopwatch.ElapsedMilliseconds} ms");
        };

        if (Configurations.IsUseKeepAwake.Get())
        {
            // Start keep awake
            _ = Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS | Kernel32.EXECUTION_STATE.ES_SYSTEM_REQUIRED | Kernel32.EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
        }

        if (Environment.GetCommandLineArgs().Any(cli => cli == "/autorun"))
        {
            Visibility = System.Windows.Visibility.Hidden;
            WindowState = System.Windows.WindowState.Minimized;
        }
    }

    private void RoomCardListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRoomCardMetrics(e.NewSize.Width);
    }

    protected override void OnClosed(EventArgs e)
    {
        PreviewKeyDown -= MainWindowPreviewKeyDown;
        ViewModel.PropertyChanged -= ViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void MainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !isPreviewFullScreen)
        {
            return;
        }

        ExitPreviewFullScreen();
        e.Handled = true;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsPreviewing))
        {
            return;
        }

        bool bringSelectedRoomIntoView = !previousPreviewingState && ViewModel.IsPreviewing;
        previousPreviewingState = ViewModel.IsPreviewing;
        if (ViewModel.IsPreviewing)
        {
            isPreviewRoomCardBaseWidthCaptured = false;
        }

        HomePreviewPanel.SetVideoPresentationSuspended(!ViewModel.IsPreviewing);
        Dispatcher.BeginInvoke(() =>
        {
            if (!ViewModel.IsPreviewing && isPreviewFullScreen)
            {
                ExitPreviewFullScreen();
            }

            UpdateHomePreviewLayout();
            UpdateLayout();
            UpdateRoomCardMetrics(RoomCardList.ActualWidth);
            if (bringSelectedRoomIntoView)
            {
                BringSelectedRoomCardIntoView();
            }
        }, DispatcherPriority.Loaded);
    }

    private void BringSelectedRoomCardIntoView()
    {
        RoomStatusReactive? selectedRoom = ViewModel.SelectedItem;
        if (selectedRoom == null)
        {
            return;
        }

        RoomCardList.ScrollIntoView(selectedRoom);
        Dispatcher.BeginInvoke(() =>
        {
            RoomCardList.UpdateLayout();
            if (RoomCardList.ItemContainerGenerator.ContainerFromItem(selectedRoom) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        }, DispatcherPriority.Render);
    }

    internal void SetPreviewPresentationSuspended(bool isSuspended)
    {
        HomePreviewPanel.SetVideoPresentationSuspended(isSuspended);
    }

    private void UpdateHomePreviewLayout()
    {
        if (isPreviewFullScreen)
        {
            ApplyPreviewFullScreenColumns();
            return;
        }

        if (ViewModel.IsPreviewing)
        {
            (double roomListWidth, double detailWidth) = CalculatePreviewPaneWidths(HomePreviewLayoutRoot.ActualWidth);
            HomeRoomCardColumn.Width = new GridLength(roomListWidth);
            HomePreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            HomeDetailColumn.Width = new GridLength(detailWidth);
            RoomDetailPanel.Visibility = detailWidth > 0d ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        HomeRoomCardColumn.Width = new GridLength(7, GridUnitType.Star);
        HomePreviewColumn.Width = new GridLength(0);
        HomeDetailColumn.Width = new GridLength(3, GridUnitType.Star);
        RoomDetailPanel.Visibility = Visibility.Visible;
    }

    internal static (double RoomListWidth, double DetailWidth) CalculatePreviewPaneWidths(double availableWidth)
    {
        if (availableWidth >= PreviewWideLayoutThreshold)
        {
            return (PreviewWideRoomListWidth, PreviewWideDetailWidth);
        }
        if (availableWidth >= PreviewDetailLayoutThreshold)
        {
            return (PreviewStandardRoomListWidth, PreviewStandardDetailWidth);
        }
        if (availableWidth >= PreviewCompactLayoutThreshold)
        {
            return (PreviewCompactRoomListWidth, 0d);
        }

        return (PreviewNarrowRoomListWidth, 0d);
    }

    internal static double GetHomeDetailPanelMaxWidth()
    {
        return Math.Round(HomeDetailPanelBaseMaxWidth * (1d - HomeDetailPanelMaxWidthReductionRatio));
    }

    private void RoundedPanelContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ActualWidth <= 0d || element.ActualHeight <= 0d)
        {
            return;
        }

        element.Clip = new RectangleGeometry(new Rect(0d, 0d, element.ActualWidth, element.ActualHeight), 8d, 8d);
    }

    private void HomePreviewLayoutRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!ViewModel.IsPreviewing || isPreviewFullScreen)
        {
            return;
        }

        UpdateHomePreviewLayout();
        UpdateRoomCardMetrics(RoomCardList.ActualWidth);
    }

    private void RoomCardListLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CaptureRoomCardBaseWidth(RoomCardList.ActualWidth);
            UpdateRoomCardMetrics(RoomCardList.ActualWidth);
        }, DispatcherPriority.Loaded);
    }

    private void UpdateRoomCardMetrics(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return;
        }

        double availableWidth = GetRoomCardAvailableWidth(width - RoomCardScrollContentPadding * 2d - RoomCardScrollBarReservedWidth);
        bool isPreviewMode = ViewModel.IsPreviewing;
        double baseWidth = GetRoomCardBaseWidth(availableWidth, isPreviewMode);
        double effectivePreference = NormalizeRoomCardScale(availableWidth, baseWidth, roomCardSizePreference);
        double horizontalGap = GetRoomCardHorizontalGap(effectivePreference);
        double verticalGap = GetRoomCardVerticalGap(effectivePreference);
        (int columns, double slotWidth, double cardWidth) = CalculateRoomCardLayout(availableWidth, baseWidth, effectivePreference, horizontalGap);
        double cardHeight = Math.Floor(cardWidth * 2d / 3d);
        double itemHeight = cardHeight + verticalGap;

        RoomCardItemWidth = slotWidth;
        RoomCardItemHeight = itemHeight;
        RoomCardPanelWidth = slotWidth * columns;
        RoomCardWidth = cardWidth;
        RoomCardHeight = cardHeight;
        RoomCardMargin = new Thickness(horizontalGap / 2d, verticalGap / 2d, horizontalGap / 2d, verticalGap / 2d);
        UpdateRoomCardVisualMetrics(cardWidth, baseWidth);
    }

    private static double GetRoomCardAvailableWidth(double width)
    {
        return Math.Max(90d, width);
    }

    private void CaptureRoomCardBaseWidth(double width)
    {
        bool isPreviewMode = ViewModel.IsPreviewing;
        if (isPreviewMode && isPreviewRoomCardBaseWidthCaptured)
        {
            return;
        }

        if (!isPreviewMode && isNormalRoomCardBaseWidthCaptured)
        {
            return;
        }

        double availableWidth = GetRoomCardAvailableWidth(width - RoomCardScrollContentPadding * 2d - RoomCardScrollBarReservedWidth);
        int baseColumns = GetRoomCardBaseColumns(isPreviewMode);
        double baseWidth = Math.Max(1d, (availableWidth - GetRoomCardHorizontalGap(RoomCardMediumSizeScale) * baseColumns) / baseColumns);

        if (isPreviewMode)
        {
            previewRoomCardBaseWidth = baseWidth;
            isPreviewRoomCardBaseWidthCaptured = true;
        }
        else
        {
            normalRoomCardBaseWidth = baseWidth;
            isNormalRoomCardBaseWidthCaptured = true;
        }
    }

    private double GetRoomCardBaseWidth(double availableWidth, bool isPreviewMode)
    {
        if (isPreviewMode && isPreviewRoomCardBaseWidthCaptured)
        {
            return previewRoomCardBaseWidth;
        }

        if (!isPreviewMode && isNormalRoomCardBaseWidthCaptured)
        {
            return normalRoomCardBaseWidth;
        }

        int baseColumns = GetRoomCardBaseColumns(isPreviewMode);
        return Math.Max(1d, (availableWidth - GetRoomCardHorizontalGap(RoomCardMediumSizeScale) * baseColumns) / baseColumns);
    }

    private static int GetRoomCardBaseColumns(bool isPreviewMode)
    {
        return isPreviewMode ? RoomCardPreviewBaseColumns : RoomCardNormalBaseColumns;
    }

    private double NormalizeRoomCardScale(double availableWidth, double baseWidth, double preference)
    {
        if (preference > RoomCardMediumSizeScale && !CanUseRoomCardScale(availableWidth, baseWidth, preference, GetRoomCardHorizontalGap(preference)))
        {
            return RoomCardMediumSizeScale;
        }

        if (preference >= RoomCardMediumSizeScale && !CanUseRoomCardScale(availableWidth, baseWidth, RoomCardMediumSizeScale, GetRoomCardHorizontalGap(RoomCardMediumSizeScale)))
        {
            return RoomCardSmallSizeScale;
        }

        return preference;
    }

    private static bool CanUseRoomCardScale(double availableWidth, double baseWidth, double preference, double horizontalGap)
    {
        double targetWidth = Math.Max(1d, baseWidth * preference);
        return availableWidth >= targetWidth * RoomCardMinScale + horizontalGap;
    }

    internal static (int Columns, double SlotWidth, double CardWidth) CalculateRoomCardLayout(double availableWidth, double baseWidth, double preference, double horizontalGap)
    {
        double targetWidth = Math.Max(1d, baseWidth * preference);
        double minWidth = targetWidth * RoomCardMinScale;
        double maxWidth = targetWidth * RoomCardMaxScale;

        double minSlotWidth = minWidth + horizontalGap;
        double maxSlotWidth = maxWidth + horizontalGap;
        double preferredSlotWidth = targetWidth + horizontalGap;
        int columns = Math.Max(1, (int)Math.Ceiling(availableWidth / preferredSlotWidth));
        double normalSlotWidth = availableWidth / columns;
        double normalCardWidth = Math.Max(1d, normalSlotWidth - horizontalGap);

        while (normalCardWidth > maxWidth)
        {
            columns++;
            normalSlotWidth = availableWidth / columns;
            normalCardWidth = Math.Max(1d, normalSlotWidth - horizontalGap);
        }

        while (columns > 1 && normalCardWidth < minWidth - RoomCardBoundaryTolerance)
        {
            columns--;
            normalSlotWidth = availableWidth / columns;
            normalCardWidth = Math.Max(1d, normalSlotWidth - horizontalGap);
        }

        if (normalCardWidth > maxWidth)
        {
            normalCardWidth = maxWidth;
            normalSlotWidth = Math.Min(maxSlotWidth, normalCardWidth + horizontalGap);
        }

        if (normalCardWidth < minWidth - RoomCardBoundaryTolerance && availableWidth >= minSlotWidth)
        {
            normalCardWidth = minWidth;
            normalSlotWidth = normalCardWidth + horizontalGap;
        }

        return (columns, normalSlotWidth, normalCardWidth);
    }

    private void UpdateRoomCardVisualMetrics(double cardWidth, double baseWidth)
    {
        double scale = Math.Clamp(cardWidth / baseWidth, RoomCardSmallSizeScale * RoomCardMinScale, RoomCardLargeSizeScale * RoomCardMaxScale);
        double chipHeight = Math.Clamp((cardWidth - 18d) / 4d, 14d, 42d);

        double avatarSize = CalculateRoomCardAvatarSize(scale);

        RoomCardPadding = new Thickness(Math.Round(8d * scale));
        RoomCardAvatarContainerSize = Math.Max(avatarSize, Math.Round(38d * scale));
        RoomCardAvatarSize = avatarSize;
        RoomCardAvatarIconSize = Math.Round(20d * scale);
        RoomCardAvatarMargin = new Thickness(Math.Round(3d * scale), Math.Round(3d * scale), Math.Round(10d * scale), 0);
        RoomCardHeaderColumnWidth = new GridLength(Math.Round(54d * scale));
        RoomCardNameFontSize = Math.Max(8d, Math.Round(15d * scale));
        RoomCardPlatformFontSize = Math.Max(7d, Math.Round(12d * scale));
        RoomCardTitleFontSize = Math.Max(7d, Math.Round(12d * scale));
        RoomCardTitleLineHeight = Math.Max(9d, Math.Round(16d * scale));
        RoomCardTitleMaxHeight = Math.Round(32d * scale);
        RoomCardTitleVisibility = scale < 0.72d ? Visibility.Collapsed : Visibility.Visible;
        RoomCardChipFontSize = Math.Max(7d, Math.Round(11d * scale));
        RoomCardChipPadding = new Thickness(Math.Round(6d * scale), Math.Round(4d * scale), Math.Round(6d * scale), Math.Round(4d * scale));
        RoomCardChipMinHeight = chipHeight;
    }

    internal static double CalculateRoomCardAvatarSize(double scale)
    {
        return Math.Max(RoomCardMinimumAvatarSize, Math.Round(36d * scale));
    }

    internal static double GetRoomCardHorizontalGap(double preference)
    {
        return GetRoomCardGap(RoomCardHorizontalGap, preference);
    }

    internal static double GetRoomCardVerticalGap(double preference)
    {
        return GetRoomCardGap(RoomCardVerticalGap, preference);
    }

    private static double GetRoomCardGap(double gap, double preference)
    {
        return preference <= RoomCardSmallSizeScale ? gap * RoomCardSmallGapScale : gap;
    }

    private void SetRoomCardLargeClick(object sender, RoutedEventArgs e)
    {
        SetRoomCardScale(RoomCardLargeSizeScale);
    }

    private void SetRoomCardMediumClick(object sender, RoutedEventArgs e)
    {
        SetRoomCardScale(RoomCardMediumSizeScale);
    }

    private void SetRoomCardSmallClick(object sender, RoutedEventArgs e)
    {
        SetRoomCardScale(RoomCardSmallSizeScale);
    }

    private void SetRoomCardScale(double scale)
    {
        double availableWidth = GetRoomCardAvailableWidth(RoomCardList.ActualWidth - RoomCardScrollContentPadding * 2d - RoomCardScrollBarReservedWidth);
        double baseWidth = GetRoomCardBaseWidth(availableWidth, ViewModel.IsPreviewing);

        if (scale > RoomCardMediumSizeScale && !CanUseRoomCardScale(availableWidth, baseWidth, scale, GetRoomCardHorizontalGap(scale)))
        {
            return;
        }

        roomCardSizePreference = Math.Clamp(scale, RoomCardSmallSizeScale, RoomCardLargeSizeScale);
        UpdateRoomCardMetrics(RoomCardList.ActualWidth);
    }

    internal void TogglePreviewFullScreen()
    {
        if (isPreviewFullScreen)
        {
            ExitPreviewFullScreen();
            return;
        }

        EnterPreviewFullScreen();
    }

    internal bool IsPreviewFullScreenActive => isPreviewFullScreen;

    internal void PrepareForTrayHide()
    {
        if (isPreviewFullScreen)
        {
            ExitPreviewFullScreen();
        }

        HomePreviewPanel.HidePreviewControlsImmediately();
    }

    private void EnterPreviewFullScreen()
    {
        if (!ViewModel.IsPreviewing)
        {
            return;
        }

        if (isPreviewFullScreen)
        {
            return;
        }

        SavePreviewFullScreenLayout();
        SavePreviewWindowPlacement();
        try
        {
            isPreviewFullScreen = true;
            ViewModel.IsPreviewDetached = true;
            ApplyPreviewFullScreenWindowBounds();
            ApplyPreviewFullScreenLayout();
            HomePreviewPanel.IsFullScreen = true;
            Activate();
            Focus();
        }
        catch
        {
            HomePreviewPanel.IsFullScreen = false;
            RestorePreviewFullScreenLayout();
            RestorePreviewWindowPlacement();
            isPreviewFullScreen = false;
            ViewModel.IsPreviewDetached = false;
            throw;
        }
    }

    private void ExitPreviewFullScreen()
    {
        if (!isPreviewFullScreen)
        {
            return;
        }

        HomePreviewPanel.HidePreviewControlsImmediately();
        HomePreviewPanel.IsFullScreen = false;
        RestorePreviewFullScreenLayout();
        RestorePreviewWindowPlacement();
        isPreviewFullScreen = false;
        ViewModel.IsPreviewDetached = false;
        UpdateHomePreviewLayout();
        Activate();
        Focus();
    }

    private void SavePreviewFullScreenLayout()
    {
        previewShellNavigationColumnWidth = MainContentRoot.ColumnDefinitions[0].Width;
        previewShellGapColumnWidth = MainContentRoot.ColumnDefinitions[1].Width;
        previewShellContentColumnWidth = MainContentRoot.ColumnDefinitions[2].Width;
        previewHomeRoomCardColumnWidth = HomeRoomCardColumn.Width;
        previewHomePreviewColumnWidth = HomePreviewColumn.Width;
        previewHomeDetailColumnWidth = HomeDetailColumn.Width;
        previewHomeDetailColumnMaxWidth = HomeDetailColumn.MaxWidth;
        previewMainContentRootMargin = MainContentRoot.Margin;
        previewShellContentPadding = ShellContentSurface.Padding;
        previewHomePreviewLayoutMargin = HomePreviewLayoutRoot.Margin;
        previewHomePreviewPanelMargin = HomePreviewPanel.Margin;
        previewShellContentCornerRadius = ShellContentSurface.CornerRadius;
        previewShellContentBackground = ShellContentSurface.Background;
        previewShellNavigationVisibility = ShellNavigationPanel.Visibility;
        previewHomeActionBarVisibility = HomeActionBar.Visibility;
        previewRoomCardPanelVisibility = RoomCardPanel.Visibility;
        previewRoomDetailPanelVisibility = RoomDetailPanel.Visibility;
        previewHomeStatusTrayVisibility = HomeStatusTray.Visibility;
        previewShellTitleBarVisibility = ShellTitleBar.Visibility;
    }

    private void ApplyPreviewFullScreenLayout()
    {
        MainContentRoot.ColumnDefinitions[0].Width = new GridLength(0);
        MainContentRoot.ColumnDefinitions[1].Width = new GridLength(0);
        MainContentRoot.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        MainContentRoot.Margin = new Thickness(0);
        ShellContentSurface.Padding = new Thickness(0);
        ShellContentSurface.CornerRadius = new CornerRadius(0);
        ShellContentSurface.Background = System.Windows.Media.Brushes.Black;
        HomePreviewLayoutRoot.Margin = new Thickness(0);
        HomePreviewPanel.Margin = new Thickness(0);
        ShellNavigationPanel.Visibility = Visibility.Collapsed;
        HomeActionBar.Visibility = Visibility.Collapsed;
        RoomCardPanel.Visibility = Visibility.Collapsed;
        RoomDetailPanel.Visibility = Visibility.Collapsed;
        HomeStatusTray.Visibility = Visibility.Collapsed;
        ShellTitleBar.Visibility = Visibility.Collapsed;
        HomeDetailColumn.MaxWidth = double.PositiveInfinity;
        ApplyPreviewFullScreenColumns();
        HomePreviewPanel.UpdateLayout();
    }

    private void ApplyPreviewFullScreenColumns()
    {
        HomeRoomCardColumn.Width = new GridLength(0);
        HomePreviewColumn.Width = new GridLength(1, GridUnitType.Star);
        HomeDetailColumn.Width = new GridLength(0);
    }

    private void RestorePreviewFullScreenLayout()
    {
        MainContentRoot.ColumnDefinitions[0].Width = previewShellNavigationColumnWidth;
        MainContentRoot.ColumnDefinitions[1].Width = previewShellGapColumnWidth;
        MainContentRoot.ColumnDefinitions[2].Width = previewShellContentColumnWidth;
        HomeRoomCardColumn.Width = previewHomeRoomCardColumnWidth;
        HomePreviewColumn.Width = previewHomePreviewColumnWidth;
        HomeDetailColumn.Width = previewHomeDetailColumnWidth;
        HomeDetailColumn.MaxWidth = previewHomeDetailColumnMaxWidth;
        MainContentRoot.Margin = previewMainContentRootMargin;
        ShellContentSurface.Padding = previewShellContentPadding;
        ShellContentSurface.CornerRadius = previewShellContentCornerRadius;
        ShellContentSurface.Background = previewShellContentBackground;
        HomePreviewLayoutRoot.Margin = previewHomePreviewLayoutMargin;
        HomePreviewPanel.Margin = previewHomePreviewPanelMargin;
        ShellNavigationPanel.Visibility = previewShellNavigationVisibility;
        HomeActionBar.Visibility = previewHomeActionBarVisibility;
        RoomCardPanel.Visibility = previewRoomCardPanelVisibility;
        RoomDetailPanel.Visibility = previewRoomDetailPanelVisibility;
        HomeStatusTray.Visibility = previewHomeStatusTrayVisibility;
        ShellTitleBar.Visibility = previewShellTitleBarVisibility;
    }

    private void SavePreviewWindowPlacement()
    {
        previewWindowState = WindowState;
        previewWindowStyle = WindowStyle;
        previewResizeMode = ResizeMode;
        previewTopmost = Topmost;
        Rect restoreBounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        previewLeft = restoreBounds.Left;
        previewTop = restoreBounds.Top;
        previewWidth = restoreBounds.Width;
        previewHeight = restoreBounds.Height;
    }

    private void ApplyPreviewFullScreenWindowBounds()
    {
        Rect bounds = GetCurrentScreenBounds();
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private void RestorePreviewWindowPlacement()
    {
        WindowState = WindowState.Normal;
        WindowStyle = previewWindowStyle;
        ResizeMode = previewResizeMode;
        Topmost = previewTopmost;
        Left = previewLeft;
        Top = previewTop;
        Width = previewWidth;
        Height = previewHeight;
        WindowState = previewWindowState;
    }

    private Rect GetCurrentScreenBounds()
    {
        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        System.Drawing.Rectangle bounds = screen.Bounds;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);

        return new Rect(
            bounds.Left / dpi.DpiScaleX,
            bounds.Top / dpi.DpiScaleY,
            bounds.Width / dpi.DpiScaleX,
            bounds.Height / dpi.DpiScaleY);
    }

    private void RoomCardListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        CancelRoomCardBlankPress();
        ListBoxItem? item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        roomCardDragStart = e.GetPosition(RoomCardList);
        roomCardPressedAt = DateTime.Now;

        if (item == null)
        {
            draggedRoom = null;
            draggedRoomItem = null;
            ViewModel.IsRoomCardSelectionVisible = false;
            StartRoomCardBlankPress(roomCardDragStart);
            return;
        }

        ViewModel.IsRoomCardSelectionVisible = true;
        draggedRoom = ViewModel.IsCardEditMode ? item.DataContext as RoomStatusReactive : null;
        draggedRoomItem = draggedRoom == null ? null : item;
        roomCardDragOffset = e.GetPosition(item);
    }

    private void RoomCardListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        Point currentPosition = e.GetPosition(RoomCardList);

        if (isRoomCardDragging)
        {
            UpdateRoomCardDrag(currentPosition);
            e.Handled = true;
            return;
        }

        if (roomCardBlankPressCandidate)
        {
            bool movedBlank = Math.Abs(currentPosition.X - roomCardBlankPressStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPosition.Y - roomCardBlankPressStart.Y) >= SystemParameters.MinimumVerticalDragDistance;

            if (movedBlank || e.LeftButton != MouseButtonState.Pressed)
            {
                CancelRoomCardBlankPress();
            }

            return;
        }

        if (!ViewModel.IsCardEditMode || e.LeftButton != MouseButtonState.Pressed || draggedRoom == null || draggedRoomItem == null)
        {
            return;
        }

        bool isHorizontalDrag = Math.Abs(currentPosition.X - roomCardDragStart.X) >= SystemParameters.MinimumHorizontalDragDistance;
        bool isVerticalDrag = Math.Abs(currentPosition.Y - roomCardDragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;

        if ((!isHorizontalDrag && !isVerticalDrag) ||
            (DateTime.Now - roomCardPressedAt).TotalMilliseconds < RoomCardDragDelayMilliseconds)
        {
            return;
        }

        StartRoomCardDrag(currentPosition);
        e.Handled = true;
    }

    private void RoomCardListPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (isRoomCardDragging)
        {
            FinishRoomCardDrag(true);
            e.Handled = true;
            return;
        }

        CancelRoomCardBlankPress();
        draggedRoom = null;
        draggedRoomItem = null;
    }

    private void RoomCardListPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelRoomCardBlankPress();

        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item &&
            item.DataContext is RoomStatusReactive room)
        {
            ViewModel.IsRoomCardSelectionVisible = true;
            RoomCardList.SelectedItem = room;
            ViewModel.SelectedItem = room;
            item.Focus();
            return;
        }

        RoomCardPanel.ContextMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, true);
        e.Handled = true;
    }

    private void RoomCardPanelMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        CancelRoomCardBlankPress();
        if (ViewModel.RefreshRoomCardsCommand.CanExecute(null))
        {
            ViewModel.RefreshRoomCardsCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void RoomCardListLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (isRoomCardDragging)
        {
            FinishRoomCardDrag(false);
        }
    }

    private void StartRoomCardBlankPress(Point position)
    {
        roomCardBlankPressCandidate = true;
        roomCardBlankPressStart = position;
        roomCardBlankPressTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RoomCardBlankLongPressMilliseconds) };
        roomCardBlankPressTimer.Tick -= RoomCardBlankPressTimerTick;
        roomCardBlankPressTimer.Tick += RoomCardBlankPressTimerTick;
        roomCardBlankPressTimer.Stop();
        roomCardBlankPressTimer.Start();
    }

    private void RoomCardBlankPressTimerTick(object? sender, EventArgs e)
    {
        CancelRoomCardBlankPress();

        if (Mouse.LeftButton != MouseButtonState.Pressed || FindRoomCardItemAt(Mouse.GetPosition(RoomCardList)) != null)
        {
            return;
        }

        if (ViewModel.ToggleCardEditModeCommand.CanExecute(null))
        {
            ViewModel.ToggleCardEditModeCommand.Execute(null);
        }
    }

    private void CancelRoomCardBlankPress()
    {
        roomCardBlankPressCandidate = false;
        roomCardBlankPressTimer?.Stop();
    }

    private ListBoxItem? FindRoomCardItemAt(Point position)
    {
        return FindVisualParent<ListBoxItem>(RoomCardList.InputHitTest(position) as DependencyObject);
    }

    private void StartRoomCardDrag(Point position)
    {
        if (draggedRoomItem == null)
        {
            return;
        }

        isRoomCardDragging = true;
        roomCardInsertionIndex = RoomCardList.Items.IndexOf(draggedRoom);
        roomCardAdornerLayer = AdornerLayer.GetAdornerLayer(RoomCardList);

        if (roomCardAdornerLayer != null)
        {
            Size dragSize = new(draggedRoomItem.ActualWidth, draggedRoomItem.ActualHeight);
            roomCardDragAdorner = new DragPreviewAdorner(RoomCardList, CreateRoomCardDragBrush(draggedRoomItem), dragSize);
            roomCardInsertionAdorner = new InsertionLineAdorner(RoomCardList);
            roomCardAdornerLayer.Add(roomCardDragAdorner);
            roomCardAdornerLayer.Add(roomCardInsertionAdorner);
        }

        draggedRoomItem.Opacity = 0;
        draggedRoomItem.IsHitTestVisible = false;
        RoomCardList.CaptureMouse();
        UpdateRoomCardDrag(position);
    }

    private void UpdateRoomCardDrag(Point position)
    {
        roomCardDragAdorner?.Move(position.X - roomCardDragOffset.X, position.Y - roomCardDragOffset.Y);
        (int index, Rect line) = GetRoomCardInsertionPreview(position);
        roomCardInsertionIndex = index;
        roomCardInsertionAdorner?.Update(line);
    }

    private (int Index, Rect Line) GetRoomCardInsertionPreview(Point position)
    {
        int count = RoomCardList.Items.Count;
        int bestIndex = Math.Max(0, count);
        Rect bestLine = Rect.Empty;
        double bestScore = double.MaxValue;

        for (int index = 0; index < count; index++)
        {
            if (RoomCardList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item || item == draggedRoomItem)
            {
                continue;
            }

            Rect bounds = GetElementBounds(item, RoomCardList);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            bool before = position.X < bounds.Left + bounds.Width / 2d;
            double edgeX = before ? bounds.Left : bounds.Right;
            double dy = position.Y < bounds.Top ? bounds.Top - position.Y : position.Y > bounds.Bottom ? position.Y - bounds.Bottom : 0d;
            double dx = Math.Abs(position.X - edgeX);
            double score = dy * 4d + dx;

            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestIndex = before ? index : index + 1;
            double lineTop = bounds.Top + Math.Min(10d, bounds.Height / 5d);
            double lineHeight = Math.Max(28d, bounds.Height - Math.Min(20d, bounds.Height / 2d));
            bestLine = new Rect(edgeX - 1.5d, lineTop, 3d, lineHeight);
        }

        if (bestLine.IsEmpty && draggedRoomItem != null)
        {
            Rect bounds = GetElementBounds(draggedRoomItem, RoomCardList);
            bestIndex = RoomCardList.Items.IndexOf(draggedRoom);
            bestLine = new Rect(bounds.Left - 1.5d, bounds.Top + 8d, 3d, Math.Max(28d, bounds.Height - 16d));
        }

        return (bestIndex, bestLine);
    }

    private static Rect GetElementBounds(FrameworkElement element, Visual relativeTo)
    {
        return element.TransformToVisual(relativeTo).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }

    private void FinishRoomCardDrag(bool commit)
    {
        if (commit && draggedRoom != null && roomCardInsertionIndex >= 0)
        {
            ViewModel.MoveRoom(draggedRoom, roomCardInsertionIndex);
        }

        ClearRoomCardDrag();
    }

    private void ClearRoomCardDrag()
    {
        isRoomCardDragging = false;

        if (draggedRoomItem != null)
        {
            draggedRoomItem.ClearValue(OpacityProperty);
            draggedRoomItem.ClearValue(IsHitTestVisibleProperty);
        }

        if (roomCardAdornerLayer != null)
        {
            if (roomCardDragAdorner != null)
            {
                roomCardAdornerLayer.Remove(roomCardDragAdorner);
            }

            if (roomCardInsertionAdorner != null)
            {
                roomCardAdornerLayer.Remove(roomCardInsertionAdorner);
            }
        }

        if (RoomCardList.IsMouseCaptured)
        {
            RoomCardList.ReleaseMouseCapture();
        }

        roomCardAdornerLayer = null;
        roomCardDragAdorner = null;
        roomCardInsertionAdorner = null;
        roomCardInsertionIndex = -1;
        draggedRoom = null;
        draggedRoomItem = null;
    }

    private static Brush CreateRoomCardDragBrush(FrameworkElement element)
    {
        double width = Math.Max(1d, element.ActualWidth);
        double height = Math.Max(1d, element.ActualHeight);
        DpiScale dpi = VisualTreeHelper.GetDpi(element);
        RenderTargetBitmap bitmap = new(
            Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        DrawingVisual visual = new();

        using (DrawingContext drawingContext = visual.RenderOpen())
        {
            drawingContext.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }

        bitmap.Render(visual);
        return new ImageBrush(bitmap) { Stretch = Stretch.Fill };
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!TrayIconManager.GetInstance().IsShutdownTriggered)
        {
            e.Cancel = true;
            PrepareForTrayHide();
            Hide();

            if (!Configurations.IsOffRemindCloseToTray.Get())
            {
                Notifier.AddNoticeWithButton("Title".Tr(), "CloseToTrayHint".Tr(), [
                    new ToastContentButtonOption()
                    {
                        Content = "ButtonOfOffRemind".Tr(),
                        Arguments = [("OffRemindTheCloseToTrayHint", bool.TrueString)],
                        ActivationType = ToastActivationType.Background,
                    },
                    new ToastContentButtonOption()
                    {
                        Content = "ButtonOfClose".Tr(),
                        ActivationType = ToastActivationType.Foreground,
                    },
                ]);
            }
        }
        else
        {
            if (Configurations.IsUseKeepAwake.Get())
            {
                // Stop keep awake
                _ = Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS);
            }

            ViewModel.Dispose();
        }
    }

    private sealed class DragPreviewAdorner(UIElement adornedElement, Brush brush, Size size) : Adorner(adornedElement)
    {
        private readonly Brush brush = brush;
        private readonly Size size = size;
        private double left;
        private double top;

        public void Move(double x, double y)
        {
            left = x;
            top = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.PushOpacity(0.86);
            drawingContext.DrawRectangle(brush, new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 120, 212)), 1), new Rect(left, top, size.Width, size.Height));
            drawingContext.Pop();
        }
    }

    private sealed class InsertionLineAdorner(UIElement adornedElement) : Adorner(adornedElement)
    {
        private Rect line = Rect.Empty;
        private readonly Brush brush = new SolidColorBrush(Color.FromRgb(0, 120, 212));

        public void Update(Rect rect)
        {
            line = rect;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (line.IsEmpty)
            {
                return;
            }

            drawingContext.DrawRectangle(brush, null, line);
        }
    }
}
