using Wpf.Ui.Controls;

namespace Emerde.Views;

public partial class LivePreviewWindow : FluentWindow
{
    private readonly System.Windows.Thickness previewMargin;

    public LivePreviewPanel PreviewContent { get; }

    public bool IsPreviewFullScreen { get; private set; }

    public LivePreviewWindow(LivePreviewPanel previewContent)
    {
        InitializeComponent();
        PreviewContent = previewContent;
        previewMargin = previewContent.Margin;
        PreviewHost.Content = previewContent;
        SourceInitialized += (_, _) => EnterPreviewFullScreen();
        Deactivated += (_, _) => PreviewContent.HidePreviewControlsImmediately();
        KeyDown += OnKeyDown;
    }

    public void TogglePreviewFullScreen()
    {
        if (IsPreviewFullScreen)
        {
            Close();
            return;
        }

        EnterPreviewFullScreen();
    }

    public LivePreviewPanel? ReleasePreviewContent()
    {
        if (!ReferenceEquals(PreviewHost.Content, PreviewContent))
        {
            return null;
        }

        PreviewContent.HidePreviewControlsImmediately();
        PreviewContent.IsFullScreen = false;
        PreviewContent.Margin = previewMargin;
        PreviewHost.Content = null;
        return PreviewContent;
    }

    private void EnterPreviewFullScreen()
    {
        if (IsPreviewFullScreen)
        {
            return;
        }

        IsPreviewFullScreen = true;
        System.Windows.Rect bounds = GetScreenBounds();
        PreviewContent.Margin = new System.Windows.Thickness(0);
        PreviewContent.IsFullScreen = true;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        Activate();
        Focus();
    }

    private System.Windows.Rect GetScreenBounds()
    {
        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        System.Drawing.Rectangle bounds = screen.Bounds;
        System.Windows.DpiScale dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);

        return new System.Windows.Rect(
            bounds.Left / dpi.DpiScaleX,
            bounds.Top / dpi.DpiScaleY,
            bounds.Width / dpi.DpiScaleX,
            bounds.Height / dpi.DpiScaleY);
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        IsPreviewFullScreen = false;
        PreviewContent.HidePreviewControlsImmediately();
        RestoreOwnerActivation();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        RestoreOwnerActivation();
        base.OnClosed(e);
    }

    private void RestoreOwnerActivation()
    {
        if (Owner is not { IsVisible: true } owner)
        {
            return;
        }

        if (owner.WindowState == System.Windows.WindowState.Minimized)
        {
            owner.WindowState = System.Windows.WindowState.Normal;
        }

        owner.Activate();
        owner.Focus();
    }
}
