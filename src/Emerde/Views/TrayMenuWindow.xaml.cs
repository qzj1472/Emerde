using Emerde.Core;
using Emerde.Extensions;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Emerde.Views;

public partial class TrayMenuWindow : Window
{
    private const int WhMouseLl = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    private readonly Action<TrayMenuAction> actionRequested;
    private readonly LowLevelMouseProc mouseHookCallback;
    private nint mouseHook;
    private bool openAbove;

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

    internal TrayMenuWindow(TrayMenuState state, Action<TrayMenuAction> actionRequested)
    {
        this.actionRequested = actionRequested;
        mouseHookCallback = MouseHookCallback;
        IsMonitorRunning = state.IsMonitorRunning;
        IsRecordEnabled = state.IsRecordEnabled;
        IsAutoRun = state.IsAutoRun;
        DataContext = this;
        InitializeComponent();
        TrayContextMenu.CustomPopupPlacementCallback = PlaceContextMenu;
        Loaded += TrayMenuWindowLoaded;
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
        PositionPlacementTarget();
        TrayContextMenu.PlacementTarget = PlacementTarget;
        TrayContextMenu.IsOpen = true;
    }

    private void TrayContextMenuOpened(object sender, RoutedEventArgs e)
    {
        StartMouseHook();
        TrayContextMenu.Focus();
    }

    private void TrayContextMenuClosed(object sender, RoutedEventArgs e)
    {
        StopMouseHook();
        Close();
    }

    internal static System.Windows.Point GetTrayMenuPlacementOffset(
        System.Windows.Size popupSize,
        System.Windows.Size targetSize,
        bool openAbove)
    {
        return new System.Windows.Point(
            targetSize.Width,
            openAbove ? -popupSize.Height : targetSize.Height);
    }

    private void PositionPlacementTarget()
    {
        System.Drawing.Point cursorPixels = System.Windows.Forms.Cursor.Position;
        System.Drawing.Rectangle workingAreaPixels = System.Windows.Forms.Screen.FromPoint(cursorPixels).WorkingArea;
        int anchorX = Math.Clamp(cursorPixels.X, workingAreaPixels.Left, workingAreaPixels.Right - 1);
        int anchorY = Math.Clamp(cursorPixels.Y, workingAreaPixels.Top, workingAreaPixels.Bottom - 1);
        openAbove = cursorPixels.Y >= workingAreaPixels.Bottom
            || anchorY - workingAreaPixels.Top > workingAreaPixels.Bottom - anchorY;
        nint handle = new WindowInteropHelper(this).Handle;
        _ = SetWindowPos(handle, nint.Zero, anchorX, anchorY, 1, 1, 0x0014);
    }

    private CustomPopupPlacement[] PlaceContextMenu(
        System.Windows.Size popupSize,
        System.Windows.Size targetSize,
        System.Windows.Point offset)
    {
        System.Windows.Point placement = GetTrayMenuPlacementOffset(popupSize, targetSize, openAbove);
        return [new CustomPopupPlacement(placement, PopupPrimaryAxis.Vertical)];
    }

    private void StartMouseHook()
    {
        if (mouseHook != nint.Zero)
        {
            return;
        }

        mouseHook = SetWindowsHookEx(WhMouseLl, mouseHookCallback, GetModuleHandle(null), 0);
        if (mouseHook == nint.Zero)
        {
            AppSessionLogger.WriteException(new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private void StopMouseHook()
    {
        if (mouseHook == nint.Zero)
        {
            return;
        }

        if (!UnhookWindowsHookEx(mouseHook))
        {
            AppSessionLogger.WriteException(new Win32Exception(Marshal.GetLastWin32Error()));
        }
        mouseHook = nint.Zero;
    }

    private nint MouseHookCallback(int code, nint message, nint data)
    {
        try
        {
            if (code >= 0
                && message is WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown or WmXButtonDown
                && PresentationSource.FromVisual(TrayContextMenu) is HwndSource source
                && GetWindowRect(source.Handle, out NativeRect bounds))
            {
                NativePoint point = Marshal.PtrToStructure<MouseHookData>(data).Point;
                if (point.X < bounds.Left || point.X >= bounds.Right || point.Y < bounds.Top || point.Y >= bounds.Bottom)
                {
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () => TrayContextMenu.IsOpen = false);
                }
            }
        }
        catch (Exception e)
        {
            AppSessionLogger.WriteException(e);
        }

        return CallNextHookEx(mouseHook, code, message, data);
    }

    private void TrayMenuWindowClosed(object? sender, EventArgs e)
    {
        StopMouseHook();
    }

    private void InvokeAction(TrayMenuAction action)
    {
        Close();
        actionRequested(action);
    }

    private void ShowMainWindowClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.ShowMainWindow);
    private void OpenSettingsClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.OpenSettings);
    private void ToggleMonitorClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.ToggleMonitor);
    private void ToggleRecordClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.ToggleRecord);
    private void ToggleAutoRunClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.ToggleAutoRun);
    private void RestartClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.Restart);
    private void ExitClick(object sender, RoutedEventArgs e) => InvokeAction(TrayMenuAction.Exit);

    private delegate nint LowLevelMouseProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseHookData
    {
        public readonly NativePoint Point;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, LowLevelMouseProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
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
