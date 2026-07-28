using Microsoft.Toolkit.Uwp.Notifications;
using Fischless.Configuration;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Emerde.Core;
using Emerde.ViewModels;
using Vanara.PInvoke;
using Wpf.Ui.Controls;
using AppResources = Emerde.Properties.Resources;
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
    private const int VirtualKeyCapsLock = 0x14;
    private HwndSource? hwndSource;
    public MainViewModel ViewModel { get; }

    public static readonly DependencyProperty RoomCardColumnCountProperty = DependencyProperty.Register(nameof(RoomCardColumnCount), typeof(int), typeof(MainWindow), new PropertyMetadata(RoomCardNormalBaseColumns));
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
    public static readonly DependencyProperty IsPreviewSurfaceVisibleProperty = DependencyProperty.Register(nameof(IsPreviewSurfaceVisible), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public int RoomCardColumnCount
    {
        get => (int)GetValue(RoomCardColumnCountProperty);
        set => SetValue(RoomCardColumnCountProperty, value);
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

    public bool IsPreviewSurfaceVisible
    {
        get => (bool)GetValue(IsPreviewSurfaceVisibleProperty);
        set => SetValue(IsPreviewSurfaceVisibleProperty, value);
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
    private const double RoomCardLargeSizeScale = 1.5d;
    private const double RoomCardMediumSizeScale = 1d;
    private const double RoomCardSmallSizeScale = 0.5d;
    private const double RoomCardMinimumAvatarSize = 18d;
    private const double RoomCardHorizontalGap = 12d;
    private const double RoomCardVerticalGap = 12d;
    private const double RoomCardSmallGapScale = 2d / 3d;
    private const double RoomCardScrollContentPadding = 6d;
    private const double RoomCardScrollBarReservedWidth = 17d;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcHitTest = 0x0084;
    private const int WmSysCommand = 0x0112;
    private const int HtClient = 1;
    private const int SysCommandMask = 0xFFF0;
    private const int ScSize = 0xF000;
    private const int ScMove = 0xF010;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const int PreviewFullScreenOverscanPixels = 2;
    private const int PreviewFullScreenTransitionMilliseconds = 210;

    private double normalRoomCardBaseWidth;
    private bool isNormalRoomCardBaseWidthCaptured;
    private double previewRoomCardBaseWidth;
    private bool isPreviewRoomCardBaseWidthCaptured;
    private double roomCardSizePreference = RoomCardMediumSizeScale;
    private Point roomCardDragStart;
    private RoomStatusReactive? draggedRoom;
    private ListBoxItem? draggedRoomItem;
    private Point roomCardDragOffset;
    private bool isRoomCardDragging;
    private bool roomCardBlankPressCandidate;
    private Point roomCardBlankPressStart;
    private bool isRoomCardMarqueeSelecting;
    private Point roomCardMarqueeStart;
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
    private double previewLeft;
    private double previewTop;
    private double previewWidth;
    private double previewHeight;
    private Rect previewPanelScreenBounds;
    private int previewDwmTransitionsForcedDisabled;
    private bool isPreviewWindowFrameAttributesCaptured;
    private bool isPreviewFullScreen;
    private bool isPreviewFullScreenTransitionActive;
    private int previewFullScreenTransitionGeneration;
    private int previewWindowFrameRestoreGeneration;
    private bool previousPreviewingState;
    private bool isStartupAboutNoticeQueued;
    private bool isStartupAboutNoticeShowing;
    private int homePreviewLayoutAnimationGeneration;
    private int homePreviewLayoutUpdateGeneration;
    private int previewPresentationUpdateGeneration;
    private bool isHomePreviewColumnAnimationActive;
    private bool isPreviewClosingTransitionActive;
    private readonly ScaleTransform previewFullScreenScaleTransform = new(1d, 1d);
    private readonly TranslateTransform previewFullScreenTranslateTransform = new();

    public MainWindow()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        DataContext = ViewModel = new();
        WindowSizing.UseMainWindowAspectSize(this);
        InitializeComponent();
        HomePreviewPanel.RenderTransformOrigin = new Point(0d, 0d);
        HomePreviewPanel.RenderTransform = new TransformGroup
        {
            Children =
            {
                previewFullScreenScaleTransform,
                previewFullScreenTranslateTransform,
            },
        };
        IsPreviewSurfaceVisible = ViewModel.IsPreviewing;
        previousPreviewingState = ViewModel.IsPreviewing;
        UpdateHomePreviewLayout();
        ViewModel.PropertyChanged += ViewModelPropertyChanged;
        PreviewKeyDown += MainWindowPreviewKeyDown;
        ComponentDispatcher.ThreadPreprocessMessage += MainWindowThreadPreprocessMessage;
        AppSessionLogger.Write($"perf MainWindow initialized in {stopwatch.ElapsedMilliseconds} ms");
        Loaded += (_, _) =>
        {
            UpdateHomePreviewLayout();
            AppSessionLogger.Write($"perf MainWindow loaded in {stopwatch.ElapsedMilliseconds} ms");
            QueueStartupAboutNotice();
        };
        IsVisibleChanged += (_, _) =>
        {
            UpdatePreviewPresentationState();
            QueueStartupAboutNotice();
        };
        StateChanged += (_, _) =>
        {
            CloseActiveToolTips();
            UpdatePreviewPresentationState();
            QueueStartupAboutNotice();
        };
        Deactivated += (_, _) => CloseActiveToolTips();
        SizeChanged += (_, _) => CloseActiveToolTips();

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(MainWindowWindowProc);
    }

    protected override void OnClosed(EventArgs e)
    {
        hwndSource?.RemoveHook(MainWindowWindowProc);
        hwndSource = null;
        PreviewKeyDown -= MainWindowPreviewKeyDown;
        ComponentDispatcher.ThreadPreprocessMessage -= MainWindowThreadPreprocessMessage;
        ViewModel.IsPreviewDetached = false;
        ViewModel.PropertyChanged -= ViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void MainWindowPreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (IsModalDialogActive())
        {
            return;
        }

        if (ConfigRestoreContentDialog.TryGetDraggedConfigFile(e.Data, out _))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.None;
    }

    private async void MainWindowPreviewDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (IsModalDialogActive())
        {
            return;
        }

        if (!ConfigRestoreContentDialog.TryGetDraggedConfigFile(e.Data, out string? filePath) || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        e.Handled = true;
        await SettingsViewModel.RestoreConfigFromDroppedFileAsync(this, filePath);
    }

    private IntPtr MainWindowWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!isPreviewFullScreen && message == WmGetMinMaxInfo && TryApplyMaximizedWindowBounds(hwnd, lParam))
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (IsPreviewFullScreenClientHitTest(isPreviewFullScreen, message))
        {
            handled = true;
            return new IntPtr(HtClient);
        }

        if (IsPreviewFullScreenBlockedSystemCommand(isPreviewFullScreen, message, wParam))
        {
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private static bool TryApplyMaximizedWindowBounds(IntPtr hwnd, IntPtr lParam)
    {
        if (hwnd == IntPtr.Zero || lParam == IntPtr.Zero)
        {
            return false;
        }

        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        MaximizedWindowBounds bounds = CalculateMaximizedWindowBounds(screen.Bounds, screen.WorkingArea);
        NativeMinMaxInfo info = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
        info.MaxPosition = new NativePoint(bounds.X, bounds.Y);
        info.MaxSize = new NativePoint(bounds.Width, bounds.Height);
        info.MaxTrackSize = new NativePoint(bounds.MaxTrackWidth, bounds.MaxTrackHeight);
        Marshal.StructureToPtr(info, lParam, false);
        return true;
    }

    internal static MaximizedWindowBounds CalculateMaximizedWindowBounds(System.Drawing.Rectangle monitorBounds, System.Drawing.Rectangle workArea)
    {
        return new MaximizedWindowBounds(
            workArea.Left - monitorBounds.Left,
            workArea.Top - monitorBounds.Top,
            workArea.Width,
            workArea.Height,
            workArea.Width,
            workArea.Height);
    }

    internal static bool IsPreviewFullScreenClientHitTest(bool isFullScreen, int message)
    {
        return isFullScreen && message == WmNcHitTest;
    }

    internal static bool IsPreviewFullScreenBlockedSystemCommand(bool isFullScreen, int message, IntPtr command)
    {
        long commandType = command.ToInt64() & SysCommandMask;
        return isFullScreen && message == WmSysCommand && (commandType == ScSize || commandType == ScMove);
    }

    private void MainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = GetShortcutKey(e);
        ModifierKeys modifiers = Keyboard.Modifiers;

        if (ShouldBypassAppShortcutsForDialog(IsModalDialogActive()))
        {
            return;
        }

        if (isPreviewFullScreen)
        {
            e.Handled = TryHandlePreviewShortcut(key, modifiers);
            return;
        }

        if (IsShortcutInputSuppressed(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (TryHandleWindowShortcut(key, modifiers))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && ViewModel.IsHomePageSelected && ViewModel.IsRoomMultiSelectMode)
        {
            ViewModel.CancelRoomMultiSelect();
            e.Handled = true;
            return;
        }

        if (TryHandlePreviewShortcut(key, modifiers))
        {
            e.Handled = true;
            return;
        }

        if (TryHandlePageShortcut(key, modifiers)
            || TryHandleGlobalShortcut(key, modifiers))
        {
            e.Handled = true;
            return;
        }

        if (!ViewModel.IsHomePageSelected)
        {
            return;
        }

        e.Handled = TryHandleHomeShortcut(key, modifiers);
    }

    private bool TryHandleWindowShortcut(Key key, ModifierKeys modifiers)
    {
        if (key != Key.W)
        {
            return false;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ViewModel.ExitApplicationCommand.Execute(null);
            return true;
        }

        if (modifiers == ModifierKeys.Control)
        {
            HideMainWindowToTray();
            return true;
        }

        return false;
    }

    private static Key GetShortcutKey(KeyEventArgs e)
    {
        return e.Key == Key.System ? e.SystemKey : e.Key;
    }

    private bool TryHandlePageShortcut(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Alt)
        {
            int pageIndex = key switch
            {
                Key.D1 or Key.NumPad1 => 0,
                Key.D2 or Key.NumPad2 => 1,
                Key.D3 or Key.NumPad3 => 2,
                Key.D4 or Key.NumPad4 => 3,
                _ => -1,
            };
            if (pageIndex < 0)
            {
                return false;
            }

            ViewModel.SelectedMainPageIndex = pageIndex;
            FocusActivePage();
            return true;
        }

        if (modifiers != ModifierKeys.None)
        {
            return false;
        }

        int direction = key switch
        {
            Key.Tab => -1,
            Key.CapsLock => 1,
            _ => 0,
        };
        if (direction == 0)
        {
            return false;
        }

        if (key == Key.CapsLock)
        {
            RestoreCapsLockState();
        }

        ViewModel.SelectedMainPageIndex = (ViewModel.SelectedMainPageIndex + direction + 4) % 4;
        FocusActivePage();
        return true;
    }

    private bool TryHandleGlobalShortcut(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control)
        {
            return false;
        }

        switch (key)
        {
            case Key.N:
                ViewModel.AddRoomCommand.Execute(null);
                return true;
            case Key.T:
                ViewModel.TestNetworkCapacityCommand.Execute(null);
                return true;
            case Key.M:
                ViewModel.ToggleMonitorCommand.Execute(null);
                return true;
            case Key.R:
                ViewModel.ToggleStatusRecordCommand.Execute(null);
                return true;
            case Key.F:
                ViewModel.RefreshRoomCardsCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private bool TryHandleHomeShortcut(Key key)
    {
        if (IsRoomCardKeyboardNavigationKey(key))
        {
            MoveRoomCardSelection(key);
            return true;
        }

        switch (key)
        {
            case Key.Delete:
                ViewModel.RemoveRoomUrlCommand.Execute(null);
                return true;
            case Key.M:
                ViewModel.ToggleSelectedRoomMonitorCommand.Execute(null);
                return true;
            case Key.R:
                ViewModel.ToggleSelectedRoomRecordCommand.Execute(null);
                return true;
            case Key.E:
                ViewModel.GotoRoomUrlCommand.Execute(null);
                return true;
            case Key.Q:
                ViewModel.PreviewLiveRoomCommand.Execute(ViewModel.SelectedItem);
                FocusRoomCardList();
                return true;
            case Key.C:
                ViewModel.CopySelectedRoomUrlCommand.Execute(null);
                return true;
            case Key.F:
                ViewModel.RefreshSelectedRoomInfoCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private bool TryHandleHomeShiftShortcut(Key key)
    {
        if (key != Key.C)
        {
            return false;
        }

        ViewModel.CopySelectedPreviewUrlCommand.Execute(null);
        return true;
    }

    private bool TryHandleHomeControlShortcut(Key key)
    {
        switch (key)
        {
            case Key.A:
                ViewModel.SelectAllRoomCardsCommand.Execute(null);
                return true;
            case Key.Z:
                ViewModel.UndoRoomSelection();
                return true;
            case Key.Y:
                ViewModel.RedoRoomSelection();
                return true;
            default:
                return false;
        }
    }

    private bool TryHandleHomeShortcut(Key key, ModifierKeys modifiers)
    {
        return modifiers switch
        {
            ModifierKeys.None => TryHandleHomeShortcut(key),
            ModifierKeys.Shift => TryHandleHomeShiftShortcut(key),
            ModifierKeys.Control => TryHandleHomeControlShortcut(key),
            _ => false,
        };
    }

    internal static bool IsRoomCardKeyboardNavigationKey(Key key)
    {
        return key is Key.Up or Key.Down or Key.Left or Key.Right or Key.W or Key.A or Key.S or Key.D;
    }

    private void MoveRoomCardSelection(Key key)
    {
        RoomStatusReactive[] visibleRooms = RoomStatusesViewItems();
        if (visibleRooms.Length == 0)
        {
            return;
        }

        int currentIndex = ViewModel.SelectedItem == null ? -1 : Array.IndexOf(visibleRooms, ViewModel.SelectedItem);
        int offset = key switch
        {
            Key.Up or Key.W => -Math.Max(1, RoomCardColumnCount),
            Key.Down or Key.S => Math.Max(1, RoomCardColumnCount),
            Key.Left or Key.A => -1,
            Key.Right or Key.D => 1,
            _ => 0,
        };
        int nextIndex = ResolveCyclicRoomIndex(currentIndex, offset, visibleRooms.Length);
        RoomStatusReactive room = visibleRooms[nextIndex];

        ViewModel.SelectRoom(room, false, false);
        ViewModel.SelectedItem = room;
        RoomCardList.SelectedItem = room;
        RoomCardList.ScrollIntoView(room);
        FocusRoomCardList();
    }

    internal static int ResolveCyclicRoomIndex(int currentIndex, int offset, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (currentIndex < 0)
        {
            return offset < 0 ? count - 1 : 0;
        }

        int normalizedIndex = currentIndex % count;
        int normalizedOffset = offset % count;
        return (normalizedIndex + normalizedOffset + count) % count;
    }

    private RoomStatusReactive[] RoomStatusesViewItems()
    {
        return RoomCardList.Items
            .OfType<RoomStatusReactive>()
            .Where(room => !string.IsNullOrWhiteSpace(room.RoomUrl))
            .ToArray();
    }

    private void FocusActivePage()
    {
        if (ViewModel.IsHomePageSelected)
        {
            FocusRoomCardList();
        }
    }

    private void FocusRoomCardList()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible && ViewModel.IsHomePageSelected)
            {
                Keyboard.Focus(RoomCardList);
            }
        }, DispatcherPriority.Input);
    }

    private void HideMainWindowToTray()
    {
        PrepareForTrayHide();
        Hide();
    }

    private static bool IsShortcutInputSuppressed(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.PasswordBox)
            {
                return true;
            }

            source = GetShortcutParent(source);
        }

        return false;
    }

    private static DependencyObject? GetShortcutParent(DependencyObject source)
    {
        if (source is ContentElement contentElement)
        {
            return System.Windows.ContentOperations.GetParent(contentElement)
                ?? (contentElement as FrameworkContentElement)?.Parent;
        }
        if (source is FrameworkElement element && element.Parent is DependencyObject frameworkParent)
        {
            return frameworkParent;
        }

        return source is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(source)
            : null;
    }

    private static void RestoreCapsLockState()
    {
        byte[] keyboardState = new byte[256];
        if (!GetKeyboardState(keyboardState))
        {
            return;
        }

        keyboardState[VirtualKeyCapsLock] ^= 1;
        _ = SetKeyboardState(keyboardState);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] keyState);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] keyState);

    private void MainWindowThreadPreprocessMessage(ref System.Windows.Interop.MSG msg, ref bool handled)
    {
        if (handled || msg.message is not 0x0100 and not 0x0104)
        {
            return;
        }

        Key key = KeyInterop.KeyFromVirtualKey(msg.wParam.ToInt32());
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (ShouldBypassAppShortcutsForDialog(IsModalDialogActive()))
        {
            return;
        }

        if (IsShortcutInputSuppressed(Keyboard.FocusedElement as DependencyObject))
        {
            return;
        }

        handled = TryHandlePreviewShortcut(key, modifiers);
        if (handled)
        {
            return;
        }

        if (isPreviewFullScreen)
        {
            handled = modifiers != ModifierKeys.Alt || key != Key.F4;
            return;
        }

        if (TryHandleWindowShortcut(key, modifiers)
            || TryHandlePageShortcut(key, modifiers)
            || TryHandleGlobalShortcut(key, modifiers))
        {
            handled = true;
            return;
        }

        handled = ViewModel.IsHomePageSelected && TryHandleHomeShortcut(key, modifiers);
    }

    private static bool IsModalDialogActive()
    {
        return WindowSizing.HasOpenContentDialog || DialogBlurScope.HasActiveDialog;
    }

    internal static bool ShouldBypassAppShortcutsForDialog(bool hasOpenDialog)
    {
        return hasOpenDialog;
    }

    private bool TryHandlePreviewShortcut(Key key, ModifierKeys modifiers)
    {
        if (!IsPreviewControlShortcut(ViewModel.IsPreviewing, key, modifiers))
        {
            return false;
        }

        switch (key)
        {
            case Key.Space:
                ViewModel.TogglePreviewPauseCommand.Execute(null);
                return true;
            case Key.M:
                ViewModel.TogglePreviewMuteCommand.Execute(null);
                return true;
            case Key.OemMinus:
                ViewModel.AdjustPreviewVolume(-5);
                return true;
            case Key.OemPlus:
                ViewModel.AdjustPreviewVolume(5);
                return true;
            case Key.G:
                ViewModel.RefreshPreviewCommand.Execute(null);
                return true;
            case Key.V:
                TogglePreviewFullScreen();
                return true;
            case Key.Escape:
                if (isPreviewFullScreen)
                {
                    ExitPreviewFullScreen();
                }
                else
                {
                    ViewModel.StopPreviewCommand.Execute(null);
                }
                return true;
            default:
                return false;
        }
    }

    internal static bool IsPreviewControlShortcut(bool isPreviewing, Key key, ModifierKeys modifiers)
    {
        return isPreviewing
            && modifiers == ModifierKeys.None
            && key is Key.Space or Key.M or Key.OemMinus or Key.OemPlus or Key.G or Key.V or Key.Escape;
    }

    internal static bool IsPreviewFullScreenExitMessage(bool isFullScreen, int message, IntPtr key)
    {
        return isFullScreen
            && message is 0x0100 or 0x0104
            && key == new IntPtr(0x1B);
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedMainPageIndex))
        {
            CloseActiveToolTips();
            if (!ViewModel.IsHomePageSelected && isPreviewClosingTransitionActive)
            {
                homePreviewLayoutUpdateGeneration++;
                InterruptHomePreviewColumnAnimation();
                UpdateHomePreviewLayout();
            }
            UpdatePreviewPresentationState();
            FocusActivePage();
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.IsPreviewing))
        {
            return;
        }

        bool wasPreviewing = previousPreviewingState;
        bool bringSelectedRoomIntoView = wasPreviewing != ViewModel.IsPreviewing;
        previousPreviewingState = ViewModel.IsPreviewing;
        InterruptHomePreviewColumnAnimation();
        if (ViewModel.IsPreviewing)
        {
            isPreviewRoomCardBaseWidthCaptured = false;
            isPreviewClosingTransitionActive = false;
            IsPreviewSurfaceVisible = true;
        }
        else if (wasPreviewing)
        {
            isPreviewClosingTransitionActive = true;
            IsPreviewSurfaceVisible = true;
        }

        UpdatePreviewPresentationState();
        int layoutUpdateGeneration = ++homePreviewLayoutUpdateGeneration;
        Dispatcher.BeginInvoke(() =>
        {
            if (homePreviewLayoutUpdateGeneration != layoutUpdateGeneration)
            {
                return;
            }

            if (!ViewModel.IsPreviewing && IsPreviewFullScreenActive)
            {
                CompletePreviewFullScreenExit();
            }

            bool layoutWillRepositionSelection = ShouldAnimateHomePreviewColumns(true);
            UpdateHomePreviewLayout(true);
            if (!layoutWillRepositionSelection)
            {
                QueueRoomCardMetricsRefresh();
                if (bringSelectedRoomIntoView)
                {
                    BringSelectedRoomCardIntoView();
                }
                if (!ViewModel.IsPreviewing && ViewModel.IsHomePageSelected)
                {
                    FocusRoomCardList();
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void QueueRoomCardMetricsRefresh()
    {
        Dispatcher.BeginInvoke(() => UpdateRoomCardMetrics(RoomCardList.ActualWidth), DispatcherPriority.Render);
    }

    private void BringSelectedRoomCardIntoView()
    {
        RoomStatusReactive? selectedRoom = ViewModel.IsPreviewing
            ? ViewModel.PreviewingRoom ?? ViewModel.SelectedItem
            : ViewModel.SelectedItem;
        if (selectedRoom == null)
        {
            return;
        }

        RoomCardList.SelectedItem = selectedRoom;
        RoomCardList.ScrollIntoView(selectedRoom);
        Dispatcher.BeginInvoke(() =>
        {
            RoomCardList.UpdateLayout();
            if (RoomCardList.ItemContainerGenerator.ContainerFromItem(selectedRoom) is FrameworkElement container)
            {
                container.BringIntoView();
                RoomCardList.UpdateLayout();
                if (FindVisualChild<ScrollViewer>(RoomCardList, "RoomCardScrollViewer") is ScrollViewer scrollViewer)
                {
                    Point itemPosition = container.TransformToAncestor(scrollViewer).Transform(new Point(0d, 0d));
                    double targetOffset = CalculateScrollOffsetToReveal(
                        scrollViewer.VerticalOffset,
                        scrollViewer.ViewportHeight,
                        itemPosition.Y,
                        container.ActualHeight);
                    scrollViewer.ScrollToVerticalOffset(targetOffset);
                }
            }
        }, DispatcherPriority.ContextIdle);
    }

    internal static double CalculateScrollOffsetToReveal(
        double currentOffset,
        double viewportHeight,
        double itemTop,
        double itemHeight)
    {
        if (itemTop < 0d)
        {
            return Math.Max(0d, currentOffset + itemTop);
        }

        double itemBottom = itemTop + itemHeight;
        if (itemBottom > viewportHeight)
        {
            return Math.Max(0d, currentOffset + itemBottom - viewportHeight);
        }

        return Math.Max(0d, currentOffset);
    }

    private void UpdatePreviewPresentationState()
    {
        int updateGeneration = ++previewPresentationUpdateGeneration;
        bool isSuspended = ShouldSuspendPreviewPresentation(
            ViewModel.IsPreviewing,
            isPreviewClosingTransitionActive,
            ViewModel.IsHomePageSelected,
            isPreviewFullScreen,
            IsVisible,
            WindowState == WindowState.Minimized);
        HomePreviewPanel.SetVideoPresentationState(isSuspended, isPreviewClosingTransitionActive);

        if (!isSuspended && ViewModel.IsPreviewing)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (previewPresentationUpdateGeneration == updateGeneration)
                {
                    HomePreviewPanel.RefreshVideoLayout();
                }
            }, DispatcherPriority.Render);
        }
    }

    internal static bool ShouldSuspendPreviewPresentation(
        bool isPreviewing,
        bool isClosingTransitionActive,
        bool isHomePageSelected,
        bool isFullScreen,
        bool isWindowVisible,
        bool isWindowMinimized)
    {
        if (!isWindowVisible || isWindowMinimized || (!isHomePageSelected && !isFullScreen))
        {
            return true;
        }

        return !isPreviewing && !isClosingTransitionActive;
    }

    private void UpdateHomePreviewLayout(bool animate = false)
    {
        if (isPreviewFullScreen)
        {
            ApplyPreviewFullScreenColumns();
            return;
        }

        if (ViewModel.IsPreviewing)
        {
            (double roomListWidth, double detailWidth) = CalculatePreviewPaneWidths(HomePreviewLayoutRoot.ActualWidth);
            ApplyHomePreviewColumns(
                new GridLength(roomListWidth),
                new GridLength(1, GridUnitType.Star),
                new GridLength(detailWidth),
                detailWidth > 0d,
                animate);
            return;
        }

        ApplyHomePreviewColumns(
            new GridLength(7, GridUnitType.Star),
            new GridLength(0),
            new GridLength(3, GridUnitType.Star),
            true,
            animate);
    }

    private void ApplyHomePreviewColumns(
        GridLength roomListWidth,
        GridLength previewWidth,
        GridLength detailWidth,
        bool showDetailPanel,
        bool animate)
    {
        if (!ShouldAnimateHomePreviewColumns(animate))
        {
            homePreviewLayoutAnimationGeneration++;
            isHomePreviewColumnAnimationActive = false;
            ClearHomePreviewColumnAnimations();
            HomeRoomCardColumn.Width = roomListWidth;
            HomePreviewColumn.Width = previewWidth;
            HomeDetailColumn.Width = detailWidth;
            RoomDetailPanel.Visibility = showDetailPanel ? Visibility.Visible : Visibility.Collapsed;
            CompletePreviewClosingTransition();
            return;
        }

        int generation = ++homePreviewLayoutAnimationGeneration;
        isHomePreviewColumnAnimationActive = true;
        double totalWidth = Math.Max(1d, HomePreviewLayoutRoot.ActualWidth);
        (double targetRoomListWidth, double targetPreviewWidth, double targetDetailWidth) = ResolveAnimatedHomePreviewWidths(
            totalWidth,
            roomListWidth,
            previewWidth,
            detailWidth,
            HomeDetailColumn.MaxWidth);

        RoomDetailPanel.Visibility = Visibility.Visible;
        AnimateHomePreviewColumn(HomeRoomCardColumn, HomeRoomCardColumn.ActualWidth, targetRoomListWidth);
        AnimateHomePreviewColumn(HomePreviewColumn, HomePreviewColumn.ActualWidth, targetPreviewWidth);
        System.Windows.Media.Animation.AnimationTimeline detailAnimation = CreateHomePreviewColumnAnimation(HomeDetailColumn.ActualWidth, targetDetailWidth);
        detailAnimation.Completed += (_, _) =>
        {
            if (homePreviewLayoutAnimationGeneration != generation)
            {
                return;
            }

            isHomePreviewColumnAnimationActive = false;
            HomeRoomCardColumn.Width = roomListWidth;
            HomePreviewColumn.Width = previewWidth;
            HomeDetailColumn.Width = detailWidth;
            ClearHomePreviewColumnAnimations();
            RoomDetailPanel.Visibility = showDetailPanel ? Visibility.Visible : Visibility.Collapsed;
            CompletePreviewClosingTransition();
            UpdateRoomCardMetrics(RoomCardList.ActualWidth);
            BringSelectedRoomCardIntoView();
            if (!ViewModel.IsPreviewing && ViewModel.IsHomePageSelected)
            {
                FocusRoomCardList();
            }
        };
        HomeDetailColumn.BeginAnimation(ColumnDefinition.WidthProperty, detailAnimation);
    }

    private void CompletePreviewClosingTransition()
    {
        if (!isPreviewClosingTransitionActive || ViewModel.IsPreviewing)
        {
            return;
        }

        isPreviewClosingTransitionActive = false;
        IsPreviewSurfaceVisible = false;
        UpdatePreviewPresentationState();
    }

    private void InterruptHomePreviewColumnAnimation()
    {
        if (!isHomePreviewColumnAnimationActive)
        {
            return;
        }

        double roomListWidth = NormalizeAnimatedWidth(HomeRoomCardColumn.ActualWidth);
        double previewWidth = NormalizeAnimatedWidth(HomePreviewColumn.ActualWidth);
        double detailWidth = NormalizeAnimatedWidth(HomeDetailColumn.ActualWidth);
        homePreviewLayoutAnimationGeneration++;
        isHomePreviewColumnAnimationActive = false;
        ClearHomePreviewColumnAnimations();
        HomeRoomCardColumn.Width = new GridLength(roomListWidth);
        HomePreviewColumn.Width = new GridLength(previewWidth);
        HomeDetailColumn.Width = new GridLength(detailWidth);
    }

    private void ClearHomePreviewColumnAnimations()
    {
        HomeRoomCardColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        HomePreviewColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        HomeDetailColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
    }

    private bool ShouldAnimateHomePreviewColumns(bool animate)
    {
        return animate
            && IsLoaded
            && SystemParameters.ClientAreaAnimation
            && ViewModel.IsHomePageSelected
            && HomePageRoot.IsVisible
            && HomePreviewLayoutRoot.ActualWidth > 0d
            && !isPreviewFullScreen;
    }

    internal static (double RoomListWidth, double PreviewWidth, double DetailWidth) ResolveAnimatedHomePreviewWidths(
        double totalWidth,
        GridLength roomListWidth,
        GridLength previewWidth,
        GridLength detailWidth,
        double detailMaxWidth = double.PositiveInfinity)
    {
        totalWidth = NormalizeAnimatedWidth(totalWidth);
        detailMaxWidth = double.IsNaN(detailMaxWidth)
            ? 0d
            : NormalizeAnimatedWidth(detailMaxWidth);
        bool isPreviewClosed = previewWidth.Value <= 0d;
        if (isPreviewClosed)
        {
            double roomWeight = roomListWidth.IsStar ? NormalizeAnimatedWidth(roomListWidth.Value) : 0d;
            double detailWeight = detailWidth.IsStar ? NormalizeAnimatedWidth(detailWidth.Value) : 0d;
            double totalWeight = roomWeight + detailWeight;
            double closedDetailPixels = totalWeight > 0d
                ? totalWidth * detailWeight / totalWeight
                : ResolveAnimatedHomePreviewColumnWidth(totalWidth, detailWidth);
            closedDetailPixels = Math.Min(closedDetailPixels, detailMaxWidth);
            return (Math.Max(0d, totalWidth - closedDetailPixels), 0d, closedDetailPixels);
        }

        double roomListPixels = ResolveAnimatedHomePreviewColumnWidth(totalWidth, roomListWidth);
        double remainingWidth = Math.Max(0d, totalWidth - roomListPixels);
        double detailPixels = Math.Min(
            ResolveAnimatedHomePreviewColumnWidth(totalWidth, detailWidth),
            Math.Min(detailMaxWidth, remainingWidth));
        double previewPixels = Math.Max(0d, remainingWidth - detailPixels);
        return (roomListPixels, previewPixels, detailPixels);
    }

    private static double ResolveAnimatedHomePreviewColumnWidth(double totalWidth, GridLength width)
    {
        if (width.IsAbsolute)
        {
            return Math.Min(totalWidth, NormalizeAnimatedWidth(width.Value));
        }

        if (width.IsStar)
        {
            return totalWidth;
        }

        return 0d;
    }

    private static double NormalizeAnimatedWidth(double value)
    {
        return double.IsFinite(value) ? Math.Max(0d, value) : value > 0d ? double.MaxValue : 0d;
    }

    private static void AnimateHomePreviewColumn(ColumnDefinition column, double from, double to)
    {
        column.BeginAnimation(ColumnDefinition.WidthProperty, CreateHomePreviewColumnAnimation(from, to));
    }

    private static System.Windows.Media.Animation.AnimationTimeline CreateHomePreviewColumnAnimation(double from, double to)
    {
        return new Emerde.Controls.GridLengthAnimation
        {
            From = NormalizeAnimatedWidth(from),
            To = NormalizeAnimatedWidth(to),
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd,
        };
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

    private void RoomCardPanelContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RoundedPanelContentSizeChanged(sender, e);
        UpdateRoomCardMetrics(e.NewSize.Width);
    }

    private void HomePreviewLayoutRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (isPreviewFullScreen || (!ViewModel.IsPreviewing && !isPreviewClosingTransitionActive))
        {
            return;
        }

        UpdateHomePreviewLayout(isHomePreviewColumnAnimationActive);
        UpdateRoomCardMetrics(RoomCardList.ActualWidth);
    }

    private void CloseActiveToolTips()
    {
        CloseActiveToolTips(this, []);
    }

    private static void CloseActiveToolTips(DependencyObject root, HashSet<DependencyObject> visited)
    {
        foreach (FrameworkElement owner in EnumerateToolTipOwners(root, visited))
        {
            if (owner.ToolTip is System.Windows.Controls.ToolTip toolTip)
            {
                toolTip.IsOpen = false;
            }

            if (!ToolTipService.GetIsEnabled(owner))
            {
                continue;
            }

            ToolTipService.SetIsEnabled(owner, false);
            _ = owner.Dispatcher.BeginInvoke(
                () => ToolTipService.SetIsEnabled(owner, true),
                DispatcherPriority.Background);
        }
    }

    private static IEnumerable<FrameworkElement> EnumerateToolTipOwners(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root))
        {
            yield break;
        }

        if (root is FrameworkElement { ToolTip: not null } element)
        {
            yield return element;
        }

        if (CanEnumerateVisualChildren(root))
        {
            int visualChildren = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < visualChildren; index++)
            {
                foreach (FrameworkElement child in EnumerateToolTipOwners(VisualTreeHelper.GetChild(root, index), visited))
                {
                    yield return child;
                }
            }
        }

        foreach (object logicalChild in LogicalTreeHelper.GetChildren(root))
        {
            if (logicalChild is DependencyObject dependencyObject)
            {
                foreach (FrameworkElement child in EnumerateToolTipOwners(dependencyObject, visited))
                {
                    yield return child;
                }
            }
        }
    }

    internal static bool CanEnumerateVisualChildren(DependencyObject root)
    {
        return root is Visual or System.Windows.Media.Media3D.Visual3D;
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
        (int candidateColumns, _, _) = CalculateRoomCardLayout(availableWidth, baseWidth, effectivePreference, horizontalGap);
        int columns = StabilizeRoomCardColumns(
            RoomCardColumnCount,
            candidateColumns,
            availableWidth,
            baseWidth * effectivePreference,
            horizontalGap);
        double cardWidth = GetCardWidthForColumns(availableWidth, columns, horizontalGap);
        double cardHeight = Math.Floor(cardWidth * 2d / 3d);

        RoomCardColumnCount = columns;
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
        double preferredSlotWidth = targetWidth + horizontalGap;
        int upperColumns = Math.Max(1, (int)Math.Ceiling(availableWidth / preferredSlotWidth));
        int lowerColumns = Math.Max(1, upperColumns - 1);
        double upperCardWidth = GetCardWidthForColumns(availableWidth, upperColumns, horizontalGap);
        double lowerCardWidth = GetCardWidthForColumns(availableWidth, lowerColumns, horizontalGap);
        int columns = lowerColumns < upperColumns && Math.Abs(lowerCardWidth - targetWidth) < Math.Abs(upperCardWidth - targetWidth)
            ? lowerColumns
            : upperColumns;
        double normalSlotWidth = availableWidth / columns;
        double normalCardWidth = GetCardWidthForColumns(availableWidth, columns, horizontalGap);

        return (columns, normalSlotWidth, normalCardWidth);
    }

    internal static int StabilizeRoomCardColumns(
        int currentColumns,
        int candidateColumns,
        double availableWidth,
        double targetCardWidth,
        double horizontalGap)
    {
        if (currentColumns <= 0 || currentColumns == candidateColumns)
        {
            return Math.Max(1, candidateColumns);
        }

        double currentCardWidth = GetCardWidthForColumns(availableWidth, currentColumns, horizontalGap);
        if (candidateColumns > currentColumns && currentCardWidth <= targetCardWidth * 1.1d)
        {
            return currentColumns;
        }
        if (candidateColumns < currentColumns && currentCardWidth >= targetCardWidth * 0.9d)
        {
            return currentColumns;
        }

        return Math.Max(1, candidateColumns);
    }

    private static double GetCardWidthForColumns(double availableWidth, int columns, double horizontalGap)
    {
        return Math.Max(1d, availableWidth / columns - horizontalGap);
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
        if (isPreviewFullScreenTransitionActive)
        {
            return;
        }

        if (IsPreviewFullScreenActive)
        {
            BeginPreviewFullScreenExit();
            return;
        }

        EnterPreviewFullScreen();
    }

    internal bool IsPreviewFullScreenActive => isPreviewFullScreen;

    internal void PrepareForTrayHide()
    {
        if (isPreviewFullScreen)
        {
            CompletePreviewFullScreenExit();
        }

        HomePreviewPanel.HidePreviewControlsImmediately();
    }

    private void EnterPreviewFullScreen()
    {
        if (!ViewModel.IsPreviewing || isPreviewFullScreen || isPreviewFullScreenTransitionActive)
        {
            return;
        }

        SavePreviewFullScreenLayout();
        SavePreviewWindowPlacement();
        SavePreviewPanelScreenBounds();
        previewWindowFrameRestoreGeneration++;
        int transitionGeneration = ++previewFullScreenTransitionGeneration;
        isPreviewFullScreenTransitionActive = true;

        try
        {
            isPreviewFullScreen = true;
            ViewModel.IsPreviewDetached = true;
            ApplyPreviewFullScreenLayout();
            HomePreviewPanel.IsFullScreen = true;
            ApplyPreviewFullScreenWindowBounds();
            Activate();
            Focus();
            UpdateLayout();
            BeginPreviewFullScreenTransform(true, transitionGeneration);
            AppSessionLogger.Event("info", "preview", "preview_full_screen_entered", "preview entered full screen", new
            {
                room = ViewModel.PreviewingRoom == null
                    ? null
                    : new { ViewModel.PreviewingRoom.RoomUrl, ViewModel.PreviewingRoom.NickName },
            });
        }
        catch
        {
            previewFullScreenTransitionGeneration++;
            isPreviewFullScreenTransitionActive = false;
            ResetPreviewFullScreenTransform();
            isPreviewFullScreen = false;
            HomePreviewPanel.IsFullScreen = false;
            RestorePreviewFullScreenLayout();
            RestorePreviewWindowPlacement();
            RestorePreviewWindowFrameAttributes();
            ViewModel.IsPreviewDetached = false;
            throw;
        }
    }

    private void ExitPreviewFullScreen()
    {
        BeginPreviewFullScreenExit();
    }

    private void BeginPreviewFullScreenExit()
    {
        if (!isPreviewFullScreen || isPreviewFullScreenTransitionActive)
        {
            return;
        }

        HomePreviewPanel.HidePreviewControlsImmediately();
        int transitionGeneration = ++previewFullScreenTransitionGeneration;
        isPreviewFullScreenTransitionActive = true;
        BeginPreviewFullScreenTransform(false, transitionGeneration);
    }

    private void CompletePreviewFullScreenExit()
    {
        if (!isPreviewFullScreen && !isPreviewFullScreenTransitionActive)
        {
            return;
        }

        previewFullScreenTransitionGeneration++;
        isPreviewFullScreenTransitionActive = false;
        ResetPreviewFullScreenTransform();
        SetPreviewSystemTransitionsDisabled(true);
        isPreviewFullScreen = false;
        HomePreviewPanel.IsFullScreen = false;
        RestorePreviewFullScreenLayout();
        RestorePreviewWindowPlacement();
        ViewModel.IsPreviewDetached = false;
        UpdatePreviewPresentationState();
        QueuePreviewLayoutRefreshAfterFullScreen();
        Activate();
        Focus();
        FocusRoomCardList();
        QueuePreviewWindowFrameAttributesRestore();
        AppSessionLogger.Event("info", "preview", "preview_full_screen_exited", "preview exited full screen", new
        {
            room = ViewModel.PreviewingRoom == null
                ? null
                : new { ViewModel.PreviewingRoom.RoomUrl, ViewModel.PreviewingRoom.NickName },
        });
    }

    private void SavePreviewPanelScreenBounds()
    {
        if (!HomePreviewPanel.IsLoaded || HomePreviewPanel.ActualWidth <= 0d || HomePreviewPanel.ActualHeight <= 0d)
        {
            previewPanelScreenBounds = Rect.Empty;
            return;
        }

        Point topLeft = HomePreviewPanel.PointToScreen(new Point(0d, 0d));
        Point bottomRight = HomePreviewPanel.PointToScreen(new Point(HomePreviewPanel.ActualWidth, HomePreviewPanel.ActualHeight));
        previewPanelScreenBounds = new Rect(topLeft, bottomRight);
    }

    private void BeginPreviewFullScreenTransform(bool entering, int transitionGeneration)
    {
        if (!TryGetPreviewFullScreenTransform(out double scaleX, out double scaleY, out double offsetX, out double offsetY)
            || !SystemParameters.ClientAreaAnimation)
        {
            CompletePreviewFullScreenTransform(entering, transitionGeneration);
            return;
        }

        double fromScaleX = entering ? scaleX : 1d;
        double fromScaleY = entering ? scaleY : 1d;
        double fromOffsetX = entering ? offsetX : 0d;
        double fromOffsetY = entering ? offsetY : 0d;
        double toScaleX = entering ? 1d : scaleX;
        double toScaleY = entering ? 1d : scaleY;
        double toOffsetX = entering ? 0d : offsetX;
        double toOffsetY = entering ? 0d : offsetY;
        System.Windows.Media.Animation.EasingMode easingMode = entering
            ? System.Windows.Media.Animation.EasingMode.EaseOut
            : System.Windows.Media.Animation.EasingMode.EaseInOut;
        System.Windows.Media.Animation.IEasingFunction easing = new System.Windows.Media.Animation.CubicEase { EasingMode = easingMode };

        ResetPreviewFullScreenTransform();
        previewFullScreenScaleTransform.ScaleX = fromScaleX;
        previewFullScreenScaleTransform.ScaleY = fromScaleY;
        previewFullScreenTranslateTransform.X = fromOffsetX;
        previewFullScreenTranslateTransform.Y = fromOffsetY;

        previewFullScreenScaleTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            CreatePreviewFullScreenAnimation(fromScaleX, toScaleX, easing));
        previewFullScreenScaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreatePreviewFullScreenAnimation(fromScaleY, toScaleY, easing));
        previewFullScreenTranslateTransform.BeginAnimation(
            TranslateTransform.XProperty,
            CreatePreviewFullScreenAnimation(fromOffsetX, toOffsetX, easing));
        System.Windows.Media.Animation.DoubleAnimation completionAnimation = CreatePreviewFullScreenAnimation(fromOffsetY, toOffsetY, easing);
        completionAnimation.Completed += (_, _) => CompletePreviewFullScreenTransform(entering, transitionGeneration);
        previewFullScreenTranslateTransform.BeginAnimation(TranslateTransform.YProperty, completionAnimation);
    }

    private bool TryGetPreviewFullScreenTransform(out double scaleX, out double scaleY, out double offsetX, out double offsetY)
    {
        scaleX = 1d;
        scaleY = 1d;
        offsetX = 0d;
        offsetY = 0d;

        if (previewPanelScreenBounds.IsEmpty || HomePreviewPanel.ActualWidth <= 0d || HomePreviewPanel.ActualHeight <= 0d)
        {
            return false;
        }

        Point targetTopLeft = HomePreviewPanel.PointFromScreen(previewPanelScreenBounds.TopLeft);
        Point targetBottomRight = HomePreviewPanel.PointFromScreen(previewPanelScreenBounds.BottomRight);
        (scaleX, scaleY, offsetX, offsetY) = CalculatePreviewFullScreenTransform(
            new Rect(targetTopLeft, targetBottomRight),
            new Size(HomePreviewPanel.ActualWidth, HomePreviewPanel.ActualHeight));
        return scaleX > 0d && scaleY > 0d;
    }

    internal static (double ScaleX, double ScaleY, double OffsetX, double OffsetY) CalculatePreviewFullScreenTransform(Rect targetBounds, Size fullScreenSize)
    {
        if (targetBounds.IsEmpty
            || !double.IsFinite(targetBounds.X)
            || !double.IsFinite(targetBounds.Y)
            || !double.IsFinite(targetBounds.Width)
            || !double.IsFinite(targetBounds.Height)
            || !double.IsFinite(fullScreenSize.Width)
            || !double.IsFinite(fullScreenSize.Height)
            || targetBounds.Width <= 0d
            || targetBounds.Height <= 0d
            || fullScreenSize.Width <= 0d
            || fullScreenSize.Height <= 0d)
        {
            return (1d, 1d, 0d, 0d);
        }

        return (
            targetBounds.Width / fullScreenSize.Width,
            targetBounds.Height / fullScreenSize.Height,
            targetBounds.X,
            targetBounds.Y);
    }

    private static System.Windows.Media.Animation.DoubleAnimation CreatePreviewFullScreenAnimation(
        double from,
        double to,
        System.Windows.Media.Animation.IEasingFunction easing)
    {
        return new System.Windows.Media.Animation.DoubleAnimation(from, to, TimeSpan.FromMilliseconds(PreviewFullScreenTransitionMilliseconds))
        {
            EasingFunction = easing,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd,
        };
    }

    private void CompletePreviewFullScreenTransform(bool entering, int transitionGeneration)
    {
        if (previewFullScreenTransitionGeneration != transitionGeneration)
        {
            return;
        }

        ResetPreviewFullScreenTransform();
        isPreviewFullScreenTransitionActive = false;
        if (!entering)
        {
            CompletePreviewFullScreenExit();
            return;
        }

        RestorePreviewSystemTransitions();
        HomePreviewPanel.RefreshVideoLayout();
        HomePreviewPanel.InvalidateVisual();
    }

    private void ResetPreviewFullScreenTransform()
    {
        previewFullScreenScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        previewFullScreenScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        previewFullScreenTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
        previewFullScreenTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        previewFullScreenScaleTransform.ScaleX = 1d;
        previewFullScreenScaleTransform.ScaleY = 1d;
        previewFullScreenTranslateTransform.X = 0d;
        previewFullScreenTranslateTransform.Y = 0d;
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
        homePreviewLayoutAnimationGeneration++;
        isHomePreviewColumnAnimationActive = false;
        ClearHomePreviewColumnAnimations();
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
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        System.Drawing.Rectangle screenBounds = System.Windows.Forms.Screen.FromHandle(handle).Bounds;
        System.Drawing.Rectangle bounds = ExpandPreviewFullScreenBounds(
            screenBounds,
            System.Windows.Forms.Screen.AllScreens.Select(screen => screen.Bounds));
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        CapturePreviewWindowFrameAttributes(handle);
        SetPreviewSystemTransitionsDisabled(true);
        WindowState = WindowState.Normal;
        SetPreviewWindowFrameAttributes(handle, true);
        Left = bounds.Left / dpi.DpiScaleX;
        Top = bounds.Top / dpi.DpiScaleY;
        Width = bounds.Width / dpi.DpiScaleX;
        Height = bounds.Height / dpi.DpiScaleY;
        _ = User32.SetWindowPos(
            handle,
            new IntPtr(-2),
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            User32.SetWindowPosFlags.SWP_SHOWWINDOW
                | User32.SetWindowPosFlags.SWP_NOOWNERZORDER);
    }

    internal static System.Drawing.Rectangle ExpandPreviewFullScreenBounds(
        System.Drawing.Rectangle bounds,
        IEnumerable<System.Drawing.Rectangle>? screenBounds = null)
    {
        System.Drawing.Rectangle[] otherScreens = (screenBounds ?? [])
            .Where(screen => screen != bounds)
            .ToArray();
        int leftOverscan = PreviewFullScreenOverscanPixels;
        int topOverscan = PreviewFullScreenOverscanPixels;
        int rightOverscan = PreviewFullScreenOverscanPixels;
        int bottomOverscan = PreviewFullScreenOverscanPixels;

        foreach (System.Drawing.Rectangle otherScreen in otherScreens)
        {
            if (RangesOverlap(bounds.Top, bounds.Bottom, otherScreen.Top, otherScreen.Bottom))
            {
                if (otherScreen.Left < bounds.Left)
                {
                    leftOverscan = Math.Min(leftOverscan, Math.Clamp(bounds.Left - otherScreen.Right, 0, PreviewFullScreenOverscanPixels));
                }

                if (otherScreen.Right > bounds.Right)
                {
                    rightOverscan = Math.Min(rightOverscan, Math.Clamp(otherScreen.Left - bounds.Right, 0, PreviewFullScreenOverscanPixels));
                }
            }

            if (RangesOverlap(bounds.Left, bounds.Right, otherScreen.Left, otherScreen.Right))
            {
                if (otherScreen.Top < bounds.Top)
                {
                    topOverscan = Math.Min(topOverscan, Math.Clamp(bounds.Top - otherScreen.Bottom, 0, PreviewFullScreenOverscanPixels));
                }

                if (otherScreen.Bottom > bounds.Bottom)
                {
                    bottomOverscan = Math.Min(bottomOverscan, Math.Clamp(otherScreen.Top - bounds.Bottom, 0, PreviewFullScreenOverscanPixels));
                }
            }
        }

        return new System.Drawing.Rectangle(
            bounds.Left - leftOverscan,
            bounds.Top - topOverscan,
            bounds.Width + leftOverscan + rightOverscan,
            bounds.Height + topOverscan + bottomOverscan);
    }

    private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        return firstStart < secondEnd && secondStart < firstEnd;
    }

    private void RestorePreviewWindowPlacement()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        WindowState = WindowState.Normal;
        Left = previewLeft;
        Top = previewTop;
        Width = previewWidth;
        Height = previewHeight;
        WindowState = previewWindowState;
        if (handle != IntPtr.Zero)
        {
            SetPreviewWindowFrameAttributes(handle, false);
        }
    }

    private void CapturePreviewWindowFrameAttributes(IntPtr handle)
    {
        if (isPreviewWindowFrameAttributesCaptured)
        {
            return;
        }

        if (Interop.DwmGetWindowAttribute(handle, Interop.DwmWindowAttribute.TransitionsForceDisabled, out previewDwmTransitionsForcedDisabled, sizeof(int)) < 0)
        {
            previewDwmTransitionsForcedDisabled = 0;
        }
        isPreviewWindowFrameAttributesCaptured = true;
    }

    private void QueuePreviewWindowFrameAttributesRestore()
    {
        int restoreGeneration = ++previewWindowFrameRestoreGeneration;
        _ = Dispatcher.BeginInvoke(
            () => RestorePreviewWindowFrameAttributes(restoreGeneration),
            DispatcherPriority.Render);
    }

    private void RestorePreviewWindowFrameAttributes(int restoreGeneration)
    {
        if (!ShouldRestorePreviewWindowFrameAttributes(
                restoreGeneration,
                previewWindowFrameRestoreGeneration,
                isPreviewFullScreen,
                isPreviewWindowFrameAttributesCaptured))
        {
            return;
        }

        RestorePreviewWindowFrameAttributes();
    }

    private void RestorePreviewWindowFrameAttributes()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            RecalculatePreviewWindowFrame(handle);
            SetPreviewWindowFrameAttributes(handle, false);
            RestorePreviewSystemTransitions();
        }

        isPreviewWindowFrameAttributesCaptured = false;
    }

    internal static bool ShouldRestorePreviewWindowFrameAttributes(
        int restoreGeneration,
        int currentGeneration,
        bool isFullScreen,
        bool attributesCaptured)
    {
        return restoreGeneration == currentGeneration
            && !isFullScreen
            && attributesCaptured;
    }

    private void SetPreviewWindowFrameAttributes(IntPtr handle, bool isFullScreen)
    {
        int borderColor = DwmColorNone;
        int cornerPreference = isFullScreen
            ? (int)Interop.DwmWindowCornerPreference.DWMWCP_DONOTROUND
            : (int)Interop.DwmWindowCornerPreference.DWMWCP_DEFAULT;

        _ = Interop.DwmSetWindowAttribute(handle, Interop.DwmWindowAttribute.BorderColor, ref borderColor, sizeof(int));
        _ = Interop.DwmSetWindowAttribute(handle, Interop.DwmWindowAttribute.WindowCornerPreference, ref cornerPreference, sizeof(int));
    }

    private static void RecalculatePreviewWindowFrame(IntPtr handle)
    {
        _ = User32.SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            User32.SetWindowPosFlags.SWP_NOMOVE
                | User32.SetWindowPosFlags.SWP_NOSIZE
                | User32.SetWindowPosFlags.SWP_NOZORDER
                | User32.SetWindowPosFlags.SWP_NOACTIVATE
                | User32.SetWindowPosFlags.SWP_FRAMECHANGED);
    }

    private void SetPreviewSystemTransitionsDisabled(bool disabled)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int value = disabled ? 1 : 0;
        _ = Interop.DwmSetWindowAttribute(handle, Interop.DwmWindowAttribute.TransitionsForceDisabled, ref value, sizeof(int));
    }

    private void RestorePreviewSystemTransitions()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !isPreviewWindowFrameAttributesCaptured)
        {
            return;
        }

        int value = previewDwmTransitionsForcedDisabled;
        _ = Interop.DwmSetWindowAttribute(handle, Interop.DwmWindowAttribute.TransitionsForceDisabled, ref value, sizeof(int));
    }

    private void QueuePreviewLayoutRefreshAfterFullScreen()
    {
        _ = Dispatcher.BeginInvoke(RefreshPreviewLayoutAfterFullScreen, DispatcherPriority.Render);
    }

    private void RefreshPreviewLayoutAfterFullScreen()
    {
        if (isPreviewFullScreen)
        {
            return;
        }

        UpdateHomePreviewLayout();
        MainContentRoot.InvalidateMeasure();
        MainContentRoot.InvalidateArrange();
        HomePreviewLayoutRoot.InvalidateMeasure();
        HomePreviewLayoutRoot.InvalidateArrange();
        RoomCardPanel.InvalidateMeasure();
        RoomCardPanel.InvalidateArrange();
        RoomDetailPanel.InvalidateMeasure();
        RoomDetailPanel.InvalidateArrange();
        HomePreviewPanel.RefreshVideoLayout();
        MainContentRoot.InvalidateVisual();
        HomePreviewPanel.InvalidateVisual();
        InvalidateVisual();
        UpdateLayout();
        UpdateRoomCardMetrics(RoomCardList.ActualWidth);
    }

    private void QueueStartupAboutNotice()
    {
        if (isStartupAboutNoticeQueued || Configurations.IsStartupAboutNoticeShown.Get())
        {
            return;
        }

        isStartupAboutNoticeQueued = true;
        _ = Dispatcher.BeginInvoke(async () =>
        {
            isStartupAboutNoticeQueued = false;
            await ShowStartupAboutNoticeIfNeededAsync();
        }, DispatcherPriority.ContextIdle);
    }

    private Task ShowStartupAboutNoticeIfNeededAsync()
    {
        if (isStartupAboutNoticeShowing
            || Configurations.IsStartupAboutNoticeShown.Get()
            || !IsLoaded
            || !IsVisible
            || WindowState == WindowState.Minimized)
        {
            return Task.CompletedTask;
        }

        isStartupAboutNoticeShowing = true;
        try
        {
            using DialogBlurScope blurScope = DialogBlurScope.ForMessageBox(this);
            System.Windows.MessageBoxResult result = MessageBox.Information(
                $"{AppResources.StartupAboutNoticeTitle}{Environment.NewLine}{Environment.NewLine}{AppResources.StartupAboutNoticeDescription}");
            if (ShouldPersistStartupAboutNoticeAcknowledgement(result))
            {
                Configurations.IsStartupAboutNoticeShown.Set(true);
                ConfigurationSaveScheduler.Request();
            }
        }
        finally
        {
            isStartupAboutNoticeShowing = false;
        }

        return Task.CompletedTask;
    }

    internal static bool ShouldPersistStartupAboutNoticeAcknowledgement(System.Windows.MessageBoxResult result)
    {
        return result == System.Windows.MessageBoxResult.OK;
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

        if (item == null)
        {
            draggedRoom = null;
            draggedRoomItem = null;
            ViewModel.IsRoomCardSelectionVisible = false;
            StartRoomCardBlankPress(roomCardDragStart);
            return;
        }

        ViewModel.IsRoomCardSelectionVisible = true;
        draggedRoom = item.DataContext as RoomStatusReactive;
        draggedRoomItem = draggedRoom == null ? null : item;
        roomCardDragOffset = e.GetPosition(item);
    }

    private void RoomCardListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        Point currentPosition = e.GetPosition(RoomCardList);

        if (isRoomCardMarqueeSelecting)
        {
            UpdateRoomCardMarquee(currentPosition);
            e.Handled = true;
            return;
        }

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

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelRoomCardBlankPress();
                return;
            }

            if (movedBlank)
            {
                Point start = roomCardBlankPressStart;
                CancelRoomCardBlankPress();
                StartRoomCardMarquee(start);
                UpdateRoomCardMarquee(currentPosition);
                e.Handled = true;
            }

            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed || draggedRoom == null || draggedRoomItem == null)
        {
            return;
        }

        bool isHorizontalDrag = Math.Abs(currentPosition.X - roomCardDragStart.X) >= SystemParameters.MinimumHorizontalDragDistance;
        bool isVerticalDrag = Math.Abs(currentPosition.Y - roomCardDragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;

        if (!isHorizontalDrag && !isVerticalDrag)
        {
            return;
        }

        StartRoomCardDrag(currentPosition);
        e.Handled = true;
    }

    private void RoomCardListPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (isRoomCardMarqueeSelecting)
        {
            FinishRoomCardMarquee(true);
            e.Handled = true;
            return;
        }

        if (isRoomCardDragging)
        {
            FinishRoomCardDrag(true);
            e.Handled = true;
            return;
        }

        if (roomCardBlankPressCandidate)
        {
            CancelRoomCardBlankPress();
            if (ViewModel.IsRoomMultiSelectMode)
            {
                ViewModel.CancelRoomMultiSelect();
            }
            e.Handled = true;
            return;
        }

        if (draggedRoom != null)
        {
            bool toggleSelection = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool selectRange = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            ViewModel.SelectRoom(draggedRoom, toggleSelection, selectRange);
            RoomCardList.SelectedItem = draggedRoom;
            ViewModel.SelectedItem = draggedRoom;
            e.Handled = true;
        }

        CancelRoomCardBlankPress();
        draggedRoom = null;
        draggedRoomItem = null;
    }

    private void RoomCardListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!isRoomCardDragging || FindVisualChild<ScrollViewer>(RoomCardList, "RoomCardScrollViewer") is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (SystemParameters.WheelScrollLines < 0)
        {
            if (e.Delta > 0)
            {
                scrollViewer.PageUp();
            }
            else
            {
                scrollViewer.PageDown();
            }
        }
        else
        {
            int lines = Math.Max(1, SystemParameters.WheelScrollLines);
            for (int index = 0; index < lines; index++)
            {
                if (e.Delta > 0)
                {
                    scrollViewer.LineUp();
                }
                else
                {
                    scrollViewer.LineDown();
                }
            }
        }

        UpdateRoomCardDrag(Mouse.GetPosition(RoomCardList));
        e.Handled = true;
    }

    private void RoomCardListPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelRoomCardBlankPress();

        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item &&
            item.DataContext is RoomStatusReactive room)
        {
            if (!room.IsSelected)
            {
                ViewModel.SelectRoom(room, false, false);
            }
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
        if (isRoomCardMarqueeSelecting)
        {
            FinishRoomCardMarquee(false);
        }

        if (isRoomCardDragging)
        {
            FinishRoomCardDrag(false);
        }
    }

    private void StartRoomCardBlankPress(Point position)
    {
        roomCardBlankPressCandidate = true;
        roomCardBlankPressStart = position;
    }

    private void CancelRoomCardBlankPress()
    {
        roomCardBlankPressCandidate = false;
    }

    private void StartRoomCardMarquee(Point position)
    {
        ViewModel.BeginRoomMultiSelect();
        isRoomCardMarqueeSelecting = true;
        roomCardMarqueeStart = position;
        RoomCardSelectionRectangle.Visibility = Visibility.Visible;
        RoomCardList.CaptureMouse();
    }

    private void UpdateRoomCardMarquee(Point position)
    {
        Rect selection = CreateSelectionRect(roomCardMarqueeStart, position);
        Canvas.SetLeft(RoomCardSelectionRectangle, selection.Left);
        Canvas.SetTop(RoomCardSelectionRectangle, selection.Top);
        RoomCardSelectionRectangle.Width = selection.Width;
        RoomCardSelectionRectangle.Height = selection.Height;
    }

    private void FinishRoomCardMarquee(bool commit)
    {
        Rect selection = CreateSelectionRect(roomCardMarqueeStart, Mouse.GetPosition(RoomCardList));
        isRoomCardMarqueeSelecting = false;
        RoomCardSelectionRectangle.Visibility = Visibility.Collapsed;
        if (RoomCardList.IsMouseCaptured)
        {
            RoomCardList.ReleaseMouseCapture();
        }

        if (!commit || selection.Width < 1d || selection.Height < 1d)
        {
            return;
        }

        List<RoomStatusReactive> selectedRooms = [];
        for (int index = 0; index < RoomCardList.Items.Count; index++)
        {
            if (RoomCardList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item
                && FindVisualChild<FrameworkElement>(item, "RoomCardShell") is FrameworkElement card
                && selection.IntersectsWith(GetElementBounds(card, RoomCardList))
                && item.DataContext is RoomStatusReactive room)
            {
                selectedRooms.Add(room);
            }
        }
        ViewModel.SelectRooms(selectedRooms);
    }

    private static Rect CreateSelectionRect(Point start, Point end)
    {
        return new Rect(
            new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
            new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));
    }

    private void StartRoomCardDrag(Point position)
    {
        if (draggedRoom == null || draggedRoomItem == null)
        {
            return;
        }

        if (!draggedRoom.IsSelected)
        {
            ViewModel.SelectRoom(draggedRoom, false, false);
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
        try
        {
            return element.TransformToVisual(relativeTo).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty;
        }
    }

    private void FinishRoomCardDrag(bool commit)
    {
        if (commit && draggedRoom != null && roomCardInsertionIndex >= 0)
        {
            ViewModel.MoveRooms(ViewModel.GetRoomsForMove(draggedRoom), roomCardInsertionIndex);
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

    private void PlatformFilterMenuSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem menu)
        {
            return;
        }

        ViewModel.EnsureSelectedPlatformFilterAvailable();
        menu.Items.Clear();
        foreach (string option in ViewModel.PlatformFilterOptions)
        {
            System.Windows.Controls.MenuItem item = new()
            {
                Header = ViewModel.GetPlatformFilterDisplayName(option),
                Tag = option,
                IsCheckable = true,
                IsChecked = string.Equals(option, ViewModel.SelectedPlatformFilter, StringComparison.OrdinalIgnoreCase),
                Style = TryFindResource("SelectedContextMenuOptionStyle") as Style,
            };
            item.Click += PlatformFilterMenuItemClick;
            menu.Items.Add(item);
        }
    }

    private void RoomSortMenuSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem sortMenu)
        {
            UpdateRoomSortMenuChecks(sortMenu);
        }
    }

    private void SortRoomsByNameMenuItemClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SortRoomsByNameCommand.Execute(null);
        UpdateRoomSortMenuChecksFromItem(sender);
    }

    private void SortRoomsByAddedAtMenuItemClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SortRoomsByAddedAtCommand.Execute(null);
        UpdateRoomSortMenuChecksFromItem(sender);
    }

    private void UpdateRoomSortMenuChecksFromItem(object sender)
    {
        if (sender is System.Windows.Controls.MenuItem item
            && System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(item) is System.Windows.Controls.MenuItem sortMenu)
        {
            UpdateRoomSortMenuChecks(sortMenu);
        }
    }

    private void UpdateRoomSortMenuChecks(System.Windows.Controls.MenuItem sortMenu)
    {
        if (sortMenu.Items.Count < 2
            || sortMenu.Items[0] is not System.Windows.Controls.MenuItem byName
            || sortMenu.Items[1] is not System.Windows.Controls.MenuItem byAddedOrder)
        {
            return;
        }

        byName.IsChecked = ViewModel.IsRoomSortByName;
        byAddedOrder.IsChecked = !ViewModel.IsRoomSortByName;
    }

    private void PlatformFilterMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string platform })
        {
            ViewModel.SelectedPlatformFilter = platform;
        }
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

            child = GetShortcutParent(child);
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        if (parent is not Visual and not System.Windows.Media.Media3D.Visual3D)
        {
            return null;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            T? nested = FindVisualChild<T>(child, name);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
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

        base.OnClosing(e);
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

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;

    public NativePoint(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMinMaxInfo
{
    public NativePoint Reserved;
    public NativePoint MaxSize;
    public NativePoint MaxPosition;
    public NativePoint MinTrackSize;
    public NativePoint MaxTrackSize;
}

internal readonly record struct MaximizedWindowBounds(
    int X,
    int Y,
    int Width,
    int Height,
    int MaxTrackWidth,
    int MaxTrackHeight);
