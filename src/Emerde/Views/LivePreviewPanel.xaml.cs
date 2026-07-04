namespace Emerde.Views;

public partial class LivePreviewPanel : System.Windows.Controls.UserControl
{
    private readonly System.Windows.Threading.DispatcherTimer pointerTrackingTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(120),
    };

    private readonly System.Windows.Threading.DispatcherTimer controlsIdleTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2),
    };

    private int pendingVideoLayoutRefreshes;
    private System.Windows.Point lastScreenMousePosition = new(double.NaN, double.NaN);
    private System.Windows.Point lastPopupPosition = new(double.NaN, double.NaN);
    private System.Windows.Window? attachedWindow;
    private bool isFullScreen;
    private System.Windows.Media.Brush normalPanelBackground = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Thickness normalPanelPadding;
    private System.Windows.Thickness normalPanelBorderThickness;
    private System.Windows.CornerRadius normalPanelCornerRadius;
    private System.Windows.GridLength normalRoomHeaderHeight;
    private System.Windows.GridLength normalPreviewHeaderHeight;

    public bool IsFullScreen
    {
        get => isFullScreen;
        set
        {
            if (isFullScreen == value)
            {
                return;
            }

            isFullScreen = value;
            ApplyFullScreenState();
        }
    }

    public LivePreviewPanel()
    {
        InitializeComponent();
        normalPanelBackground = PanelChrome.Background;
        normalPanelPadding = PanelChrome.Padding;
        normalPanelBorderThickness = PanelChrome.BorderThickness;
        normalPanelCornerRadius = PanelChrome.CornerRadius;
        normalRoomHeaderHeight = ((System.Windows.Controls.Grid)PanelChrome.Child).RowDefinitions[0].Height;
        normalPreviewHeaderHeight = ((System.Windows.Controls.Grid)PanelChrome.Child).RowDefinitions[1].Height;
        Loaded += (_, _) =>
        {
            UpdateVideoSurfaceSize();
            AttachWindowEvents();
            pointerTrackingTimer.Start();
            ShowPreviewControls();
        };
        SizeChanged += (_, _) =>
        {
            UpdateVideoSurfaceSize();
            UpdatePreviewControlsPlacement();
        };
        DataContextChanged += (_, _) => AttachMediaPlayerEvents();
        pointerTrackingTimer.Tick += (_, _) =>
        {
            UpdatePreviewControlsPlacement();
            TrackPreviewPointer();
        };
        controlsIdleTimer.Tick += (_, _) => HidePreviewControls();
        Unloaded += (_, _) =>
        {
            pointerTrackingTimer.Stop();
            controlsIdleTimer.Stop();
            PreviewControlsPopup.IsOpen = false;
            DetachWindowEvents();
        };
    }

    private LibVLCSharp.Shared.MediaPlayer? attachedMediaPlayer;

    private void AttachMediaPlayerEvents()
    {
        if (attachedMediaPlayer != null)
        {
            attachedMediaPlayer.Vout -= OnMediaPlayerVout;
            attachedMediaPlayer.Playing -= OnMediaPlayerPlaying;
        }

        attachedMediaPlayer = (DataContext as ViewModels.MainViewModel)?.LivePreviewMediaPlayer;

        if (attachedMediaPlayer != null)
        {
            attachedMediaPlayer.Vout += OnMediaPlayerVout;
            attachedMediaPlayer.Playing += OnMediaPlayerPlaying;
        }

        UpdateVideoSurfaceSize();
    }

    private void OnMediaPlayerVout(object? sender, LibVLCSharp.Shared.MediaPlayerVoutEventArgs e)
    {
        ScheduleVideoLayoutRefresh();
    }

    private void OnMediaPlayerPlaying(object? sender, EventArgs e)
    {
        ScheduleVideoLayoutRefresh();
    }

    private void ScheduleVideoLayoutRefresh()
    {
        pendingVideoLayoutRefreshes = 12;
        _ = Dispatcher.BeginInvoke(RefreshVideoSurfaceSize);
    }

    private async void RefreshVideoSurfaceSize()
    {
        while (pendingVideoLayoutRefreshes > 0)
        {
            pendingVideoLayoutRefreshes--;
            UpdateVideoSurfaceSize();

            if (TryGetVideoAspectRatio(out _))
            {
                pendingVideoLayoutRefreshes = 0;
                return;
            }

            await Task.Delay(250);
        }
    }

    private void UpdateVideoSurfaceSize()
    {
        double viewportWidth = PreviewViewport.ActualWidth;
        double viewportHeight = PreviewViewport.ActualHeight;

        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        double aspectRatio = TryGetVideoAspectRatio(out double videoAspectRatio) ? videoAspectRatio : 16d / 9d;
        double viewportRatio = viewportWidth / viewportHeight;

        if (viewportRatio > aspectRatio)
        {
            VideoSurface.Height = viewportHeight;
            VideoSurface.Width = viewportHeight * aspectRatio;
        }
        else
        {
            VideoSurface.Width = viewportWidth;
            VideoSurface.Height = viewportWidth / aspectRatio;
        }
    }

    private bool TryGetVideoAspectRatio(out double aspectRatio)
    {
        aspectRatio = 0;
        uint width = 0;
        uint height = 0;

        if (attachedMediaPlayer != null
         && attachedMediaPlayer.VoutCount > 0
         && attachedMediaPlayer.Size(0, ref width, ref height)
         && width > 0
         && height > 0)
        {
            aspectRatio = (double)width / height;
            return true;
        }

        return false;
    }

    private void PreviewViewport_OnMouseActivity(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ShowPreviewControls();
    }

    private void PreviewControls_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        controlsIdleTimer.Stop();
        PreviewControls.Opacity = 1;
        PreviewControls.IsHitTestVisible = true;
    }

    private void PreviewControls_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        RestartControlsIdleTimer();
    }

    private void ShowPreviewControls()
    {
        UpdatePreviewControlsPlacement();
        PreviewControlsPopup.IsOpen = true;
        PreviewControls.Opacity = 1;
        PreviewControls.IsHitTestVisible = true;
        RestartControlsIdleTimer();
    }

    private void RestartControlsIdleTimer()
    {
        controlsIdleTimer.Stop();
        controlsIdleTimer.Start();
    }

    private void HidePreviewControls()
    {
        controlsIdleTimer.Stop();

        if (PreviewControls.IsMouseOver)
        {
            RestartControlsIdleTimer();
            return;
        }

        PreviewControlsPopup.IsOpen = false;
        PreviewControls.Opacity = 0;
        PreviewControls.IsHitTestVisible = false;
    }

    private void ToggleWindowSize_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        System.Windows.Window? window = System.Windows.Window.GetWindow(this);

        if (window == null)
        {
            return;
        }

        if (window is LivePreviewWindow livePreviewWindow)
        {
            livePreviewWindow.TogglePreviewFullScreen();
        }

        ShowPreviewControls();
        UpdateVideoSurfaceSize();
        UpdateWindowSizeIcon();
    }

    private void UpdatePreviewControlsPlacement()
    {
        PreviewControls.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

        if (!VideoSurface.IsVisible || VideoSurface.ActualWidth <= 0 || VideoSurface.ActualHeight <= 0)
        {
            return;
        }

        System.Windows.Point videoTopLeft = TransformScreenPointFromDevice(VideoSurface.PointToScreen(new System.Windows.Point(0, 0)));
        double popupWidth = PreviewControls.DesiredSize.Width;
        double popupHeight = PreviewControls.DesiredSize.Height;
        double x = videoTopLeft.X + Math.Max(0, (VideoSurface.ActualWidth - popupWidth) / 2);
        double y = videoTopLeft.Y + Math.Max(0, VideoSurface.ActualHeight - popupHeight - 16);

        System.Windows.Point popupPosition = new(Math.Round(x), Math.Round(y));

        if (double.IsNaN(lastPopupPosition.X)
         || Math.Abs(popupPosition.X - lastPopupPosition.X) >= 1
         || Math.Abs(popupPosition.Y - lastPopupPosition.Y) >= 1)
        {
            lastPopupPosition = popupPosition;
            PreviewControlsPopup.HorizontalOffset = popupPosition.X;
            PreviewControlsPopup.VerticalOffset = popupPosition.Y;
        }

        UpdateWindowSizeIcon();
    }

    private void TrackPreviewPointer()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (!IsMouseOverPreviewArea())
        {
            return;
        }

        System.Windows.Point screenMousePosition = GetScreenMousePosition();

        if (double.IsNaN(lastScreenMousePosition.X)
         || Math.Abs(screenMousePosition.X - lastScreenMousePosition.X) >= 1
         || Math.Abs(screenMousePosition.Y - lastScreenMousePosition.Y) >= 1)
        {
            lastScreenMousePosition = screenMousePosition;
            ShowPreviewControls();
        }
    }

    private bool IsMouseOverPreviewArea()
    {
        if (!PreviewViewport.IsVisible)
        {
            return false;
        }

        System.Windows.Point screenMousePosition = GetScreenMousePosition();
        System.Windows.Point previewTopLeft = PreviewViewport.PointToScreen(new System.Windows.Point(0, 0));
        System.Windows.Size previewSize = TransformSizeToDevice(new System.Windows.Size(PreviewViewport.ActualWidth, PreviewViewport.ActualHeight));
        System.Windows.Rect previewBounds = new(previewTopLeft, previewSize);

        return previewBounds.Contains(screenMousePosition);
    }

    private static System.Windows.Point GetScreenMousePosition()
    {
        _ = GetCursorPos(out NativePoint point);
        return new System.Windows.Point(point.X, point.Y);
    }

    private System.Windows.Point TransformScreenPointFromDevice(System.Windows.Point point)
    {
        System.Windows.Media.CompositionTarget? compositionTarget = System.Windows.PresentationSource.FromVisual(this)?.CompositionTarget;
        return compositionTarget?.TransformFromDevice.Transform(point) ?? point;
    }

    private System.Windows.Size TransformSizeToDevice(System.Windows.Size size)
    {
        System.Windows.Media.CompositionTarget? compositionTarget = System.Windows.PresentationSource.FromVisual(this)?.CompositionTarget;

        if (compositionTarget == null)
        {
            return size;
        }

        System.Windows.Vector vector = compositionTarget.TransformToDevice.Transform(new System.Windows.Vector(size.Width, size.Height));
        return new System.Windows.Size(vector.X, vector.Y);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }

    private void UpdateWindowSizeIcon()
    {
        System.Windows.Window? window = System.Windows.Window.GetWindow(this);
        bool isMaximized = window is LivePreviewWindow { IsPreviewFullScreen: true };

        MaximizeIcon.Visibility = isMaximized ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        RestoreIcon.Visibility = isMaximized ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void AttachWindowEvents()
    {
        System.Windows.Window? window = System.Windows.Window.GetWindow(this);

        if (window == null || ReferenceEquals(attachedWindow, window))
        {
            return;
        }

        DetachWindowEvents();
        attachedWindow = window;
        attachedWindow.LocationChanged += OnWindowLayoutChanged;
        attachedWindow.SizeChanged += OnWindowLayoutChanged;
        attachedWindow.StateChanged += OnWindowLayoutChanged;
    }

    private void DetachWindowEvents()
    {
        if (attachedWindow == null)
        {
            return;
        }

        attachedWindow.LocationChanged -= OnWindowLayoutChanged;
        attachedWindow.SizeChanged -= OnWindowLayoutChanged;
        attachedWindow.StateChanged -= OnWindowLayoutChanged;
        attachedWindow = null;
    }

    private void OnWindowLayoutChanged(object? sender, EventArgs e)
    {
        UpdateVideoSurfaceSize();
        UpdatePreviewControlsPlacement();
    }

    private void ApplyFullScreenState()
    {
        System.Windows.Controls.Grid rootGrid = (System.Windows.Controls.Grid)PanelChrome.Child;

        if (isFullScreen)
        {
            PanelChrome.Padding = new System.Windows.Thickness(0);
            PanelChrome.Background = System.Windows.Media.Brushes.Black;
            PanelChrome.BorderThickness = new System.Windows.Thickness(0);
            PanelChrome.CornerRadius = new System.Windows.CornerRadius(0);
            RoomHeader.Visibility = System.Windows.Visibility.Collapsed;
            PreviewHeader.Visibility = System.Windows.Visibility.Collapsed;
            rootGrid.RowDefinitions[0].Height = new System.Windows.GridLength(0);
            rootGrid.RowDefinitions[1].Height = new System.Windows.GridLength(0);
        }
        else
        {
            PanelChrome.Padding = normalPanelPadding;
            PanelChrome.Background = normalPanelBackground;
            PanelChrome.BorderThickness = normalPanelBorderThickness;
            PanelChrome.CornerRadius = normalPanelCornerRadius;
            RoomHeader.Visibility = System.Windows.Visibility.Visible;
            PreviewHeader.Visibility = System.Windows.Visibility.Visible;
            rootGrid.RowDefinitions[0].Height = normalRoomHeaderHeight;
            rootGrid.RowDefinitions[1].Height = normalPreviewHeaderHeight;
        }

        UpdateVideoSurfaceSize();
        UpdatePreviewControlsPlacement();
        UpdateWindowSizeIcon();
    }
}
