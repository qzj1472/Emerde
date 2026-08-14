using Emerde.Core;
using Emerde.Extensions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Emerde.Views;

public partial class TrayMenuWindow : Window
{
    private const double TrayAnchorGap = 0d;
    private const double WorkAreaMargin = 0d;

    private readonly Action<TrayMenuAction> actionRequested;
    private bool closeRequested;
    private TrayMenuAction? pendingAction;

    public string ShowMainWindowText { get; } = StripAccessKeySuffix("TrayMenuShowMainWindow".Tr());
    public string SettingsText { get; } = "Settings".Tr();
    public string MonitorText { get; } = Translate("TrayMenuMonitor");
    public string RecordText { get; } = "EnableRecord".Tr();
    public string AutoRunText { get; } = StripAccessKeySuffix("TrayMenuAutoRun".Tr());
    public string RestartText { get; } = StripAccessKeySuffix("TrayMenuRestart".Tr());
    public string ExitText { get; } = StripAccessKeySuffix("TrayMenuExit".Tr());
    public bool IsMonitorRunning { get; }
    public bool IsRecordEnabled { get; }
    public bool IsAutoRun { get; }
    public Visibility MonitorIndicatorVisibility => IsMonitorRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RecordIndicatorVisibility => IsRecordEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AutoRunIndicatorVisibility => IsAutoRun ? Visibility.Visible : Visibility.Collapsed;

    internal TrayMenuWindow(TrayMenuState state, Action<TrayMenuAction> actionRequested)
    {
        this.actionRequested = actionRequested;
        IsMonitorRunning = state.IsMonitorRunning;
        IsRecordEnabled = state.IsRecordEnabled;
        IsAutoRun = state.IsAutoRun;
        DataContext = this;
        InitializeComponent();
        Loaded += TrayMenuWindowLoaded;
        SourceInitialized += TrayMenuWindowSourceInitialized;
        Closed += TrayMenuWindowClosed;
    }

    internal static string StripAccessKeySuffix(string text)
    {
        int suffixIndex = text.LastIndexOf(" (&", StringComparison.Ordinal);
        return suffixIndex >= 0 && text.EndsWith(')') ? text[..suffixIndex] : text;
    }

    internal static string BuildStatusText(TrayMenuState state)
    {
        if (state.RecordingCount > 0)
        {
            return string.Format(Translate("TrayMenuRecordingSummary"), state.RecordingCount);
        }
        if (state.StreamingCount > 0)
        {
            return string.Format(Translate("TrayMenuLiveSummary"), state.StreamingCount);
        }
        return Translate(state.IsMonitorRunning ? "TrayMenuMonitorRunning" : "TrayMenuMonitorPaused");
    }

    private static string Translate(string key)
    {
        return Emerde.Properties.Resources.ResourceManager.GetString(key, Locale.Culture) ?? key;
    }

    private void TrayMenuWindowLoaded(object sender, RoutedEventArgs e)
    {
        PositionMenuWindow();
        Activate();
        Focus();
    }

    private void TrayMenuWindowSourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessageHook);
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int wmActivate = 0x0006;
        const int waInactive = 0;
        if (message == wmActivate && (wParam.ToInt64() & 0xFFFF) == waInactive)
        {
            Dispatcher.BeginInvoke(CloseSafely, DispatcherPriority.Input);
        }
        return nint.Zero;
    }

    internal static System.Windows.Point CalculateTrayMenuPosition(
        System.Windows.Point cursor,
        Rect workArea,
        System.Windows.Size menuSize,
        double anchorGap = TrayAnchorGap,
        double workAreaMargin = WorkAreaMargin)
    {
        double minimumX = workArea.Left + workAreaMargin;
        double maximumX = Math.Max(minimumX, workArea.Right - menuSize.Width - workAreaMargin);
        double minimumY = workArea.Top + workAreaMargin;
        double maximumY = Math.Max(minimumY, workArea.Bottom - menuSize.Height - workAreaMargin);
        double x = cursor.X + menuSize.Width <= workArea.Right - workAreaMargin
            ? cursor.X
            : maximumX;
        double y = cursor.Y - menuSize.Height - anchorGap >= minimumY
            ? cursor.Y - menuSize.Height - anchorGap
            : cursor.Y + anchorGap;
        return new System.Windows.Point(Math.Clamp(x, minimumX, maximumX), Math.Clamp(y, minimumY, maximumY));
    }

    private void PositionMenuWindow()
    {
        System.Drawing.Point cursorPixels = System.Windows.Forms.Cursor.Position;
        System.Drawing.Rectangle workingAreaPixels = System.Windows.Forms.Screen.FromPoint(cursorPixels).WorkingArea;
        System.Windows.Point cursor = PixelsToDeviceIndependent(new System.Windows.Point(cursorPixels.X, cursorPixels.Y));
        System.Windows.Point workAreaTopLeft = PixelsToDeviceIndependent(new System.Windows.Point(workingAreaPixels.Left, workingAreaPixels.Top));
        System.Windows.Point workAreaBottomRight = PixelsToDeviceIndependent(new System.Windows.Point(workingAreaPixels.Right, workingAreaPixels.Bottom));
        Rect workArea = new(workAreaTopLeft, workAreaBottomRight);
        System.Windows.Size menuSize = new(ActualWidth, ActualHeight);
        System.Windows.Point position = CalculateTrayMenuPosition(cursor, workArea, menuSize);
        Left = position.X;
        Top = position.Y;
    }

    private System.Windows.Point PixelsToDeviceIndependent(System.Windows.Point point)
    {
        PresentationSource? source = PresentationSource.FromVisual(this);
        Matrix transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(point);
    }

    private void TrayMenuWindowDeactivated(object? sender, EventArgs e)
    {
        CloseSafely();
    }

    private void TrayMenuWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseSafely();
            e.Handled = true;
        }
    }

    private void InvokeAction(TrayMenuAction action)
    {
        if (closeRequested)
        {
            return;
        }

        pendingAction = action;
        CloseSafely();
    }

    private void CloseSafely()
    {
        if (closeRequested)
        {
            return;
        }

        closeRequested = true;
        Close();
    }

    private void TrayMenuWindowClosed(object? sender, EventArgs e)
    {
        TrayMenuAction? action = pendingAction;
        pendingAction = null;
        if (action.HasValue)
        {
            Dispatcher.BeginInvoke(() => actionRequested(action.Value), DispatcherPriority.Input);
        }
    }

    internal void RequestClose()
    {
        CloseSafely();
    }

    private void ShowMainWindowClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.ShowMainWindow);
    private void OpenSettingsClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.OpenSettings);
    private void ToggleMonitorClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.ToggleMonitor);
    private void ToggleRecordClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.ToggleRecord);
    private void ToggleAutoRunClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.ToggleAutoRun);
    private void RestartClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.Restart);
    private void ExitClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.Exit);

}

internal readonly record struct TrayMenuState(
    string VersionText,
    int StreamingCount,
    int RecordingCount,
    bool IsMonitorRunning,
    bool IsRecordEnabled,
    bool IsAutoRun);

internal enum TrayMenuAction
{
    ShowMainWindow,
    OpenSettings,
    ToggleMonitor,
    ToggleRecord,
    ToggleAutoRun,
    Restart,
    Exit,
}
