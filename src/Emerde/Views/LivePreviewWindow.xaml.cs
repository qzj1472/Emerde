using Wpf.Ui.Controls;
using System.Windows.Threading;

namespace Emerde.Views;

public partial class LivePreviewWindow : FluentWindow
{
    private System.Windows.Rect normalBounds;
    private System.Windows.WindowState normalWindowState;
    private System.Windows.WindowStyle normalWindowStyle;
    private System.Windows.ResizeMode normalResizeMode;
    private bool normalExtendsContentIntoTitleBar;
    private bool normalTopmost;
    private System.Windows.Thickness normalPreviewMargin;

    public bool IsPreviewFullScreen { get; private set; }

    public LivePreviewWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => RefreshPreviewLayout();
        SizeChanged += (_, _) => RefreshPreviewLayout();
        KeyDown += OnKeyDown;
    }

    private void RefreshPreviewLayout()
    {
        Dispatcher.BeginInvoke(() =>
        {
            PreviewContent.InvalidateMeasure();
            PreviewContent.InvalidateArrange();
            PreviewContent.UpdateLayout();
        }, DispatcherPriority.Render);
    }

    public void TogglePreviewFullScreen()
    {
        if (IsPreviewFullScreen)
        {
            ExitPreviewFullScreen();
            return;
        }

        EnterPreviewFullScreen();
    }

    private void EnterPreviewFullScreen()
    {
        if (IsPreviewFullScreen)
        {
            return;
        }

        IsPreviewFullScreen = true;
        normalWindowState = WindowState;
        normalBounds = normalWindowState == System.Windows.WindowState.Normal
            ? new System.Windows.Rect(Left, Top, Width, Height)
            : RestoreBounds;
        normalWindowStyle = WindowStyle;
        normalResizeMode = ResizeMode;
        normalExtendsContentIntoTitleBar = ExtendsContentIntoTitleBar;
        normalTopmost = Topmost;
        normalPreviewMargin = PreviewContent.Margin;

        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        System.Drawing.Rectangle bounds = screen.Bounds;

        WindowState = System.Windows.WindowState.Normal;
        WindowStyle = System.Windows.WindowStyle.None;
        ResizeMode = System.Windows.ResizeMode.NoResize;
        ExtendsContentIntoTitleBar = false;
        WindowTitleBar.Visibility = System.Windows.Visibility.Collapsed;
        PreviewContent.Margin = new System.Windows.Thickness(0);
        PreviewContent.IsFullScreen = true;
        Topmost = true;

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        RefreshPreviewLayout();
    }

    private void ExitPreviewFullScreen()
    {
        if (!IsPreviewFullScreen)
        {
            return;
        }

        IsPreviewFullScreen = false;
        Topmost = normalTopmost;
        WindowTitleBar.Visibility = System.Windows.Visibility.Visible;
        PreviewContent.Margin = normalPreviewMargin;
        PreviewContent.IsFullScreen = false;
        ExtendsContentIntoTitleBar = normalExtendsContentIntoTitleBar;
        WindowStyle = normalWindowStyle;
        ResizeMode = normalResizeMode;

        Left = normalBounds.Left;
        Top = normalBounds.Top;
        Width = normalBounds.Width;
        Height = normalBounds.Height;
        WindowState = normalWindowState;

        RefreshPreviewLayout();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && IsPreviewFullScreen)
        {
            ExitPreviewFullScreen();
        }
    }
}
