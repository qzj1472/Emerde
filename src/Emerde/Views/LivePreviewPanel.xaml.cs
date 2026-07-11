namespace Emerde.Views;

public partial class LivePreviewPanel : System.Windows.Controls.UserControl
{
    public static readonly System.Windows.DependencyProperty IsEmbeddedModeProperty = System.Windows.DependencyProperty.Register(
        nameof(IsEmbeddedMode),
        typeof(bool),
        typeof(LivePreviewPanel),
        new System.Windows.PropertyMetadata(false, OnIsEmbeddedModeChanged));

    private readonly System.Windows.Threading.DispatcherTimer pointerTrackingTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(120),
    };

    private readonly System.Windows.Threading.DispatcherTimer controlsIdleTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2),
    };

    private int pendingVideoLayoutRefreshes;
    private System.Windows.Point? lastTrackedPointerPosition;
    private System.Windows.Window? attachedWindow;
    private ViewModels.MainViewModel? attachedViewModel;
    private bool isVideoPresentationSuspended;
    private bool isFullScreen;
    private System.Windows.Media.Brush normalPanelBackground = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Thickness normalPanelPadding;
    private System.Windows.Thickness normalPanelBorderThickness;
    private System.Windows.CornerRadius normalPanelCornerRadius;
    private System.Windows.GridLength normalPlayerHeaderHeight;
    private LibVLCSharp.WPF.VideoView? previewVideoView;

    public bool IsEmbeddedMode
    {
        get => (bool)GetValue(IsEmbeddedModeProperty);
        set => SetValue(IsEmbeddedModeProperty, value);
    }

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
        normalPlayerHeaderHeight = ((System.Windows.Controls.Grid)PanelChrome.Child).RowDefinitions[0].Height;
        Loaded += (_, _) =>
        {
            ApplyChromeState();
            AttachMediaPlayerEvents();
            UpdateVideoSurfaceSize();
            AttachWindowEvents();
            pointerTrackingTimer.Start();
            HidePreviewControlsImmediately();
        };
        SizeChanged += (_, _) =>
        {
            ApplyPanelClip();
            UpdateVideoSurfaceSize();
            UpdateWindowSizeIcon();
        };
        DataContextChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                AttachMediaPlayerEvents();
            }
        };
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
            pendingVideoLayoutRefreshes = 0;
            lastTrackedPointerPosition = null;
            HidePreviewControlsImmediately();
            DetachMediaPlayerEvents();
            DetachWindowEvents();
        };
    }

    private LibVLCSharp.Shared.MediaPlayer? attachedMediaPlayer;

    private void AttachMediaPlayerEvents()
    {
        ViewModels.MainViewModel? viewModel = DataContext as ViewModels.MainViewModel;
        LibVLCSharp.Shared.MediaPlayer? mediaPlayer = viewModel?.LivePreviewMediaPlayer;
        if (ReferenceEquals(attachedViewModel, viewModel) && ReferenceEquals(attachedMediaPlayer, mediaPlayer))
        {
            UpdateVideoPresentationState();
            return;
        }

        DetachMediaPlayerEvents();
        attachedViewModel = viewModel;
        attachedMediaPlayer = mediaPlayer;
        PreviewOverlayRoot.DataContext = attachedViewModel;

        if (attachedViewModel != null)
        {
            attachedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        if (attachedMediaPlayer != null)
        {
            attachedMediaPlayer.Vout += OnMediaPlayerVout;
            attachedMediaPlayer.Playing += OnMediaPlayerPlaying;
        }

        UpdateVideoPresentationState();
    }

    private void DetachMediaPlayerEvents()
    {
        ClearVideoPresentation();
        PreviewOverlayRoot.DataContext = null;

        if (attachedViewModel != null)
        {
            attachedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            attachedViewModel = null;
        }

        if (attachedMediaPlayer == null)
        {
            return;
        }

        attachedMediaPlayer.Vout -= OnMediaPlayerVout;
        attachedMediaPlayer.Playing -= OnMediaPlayerPlaying;
        attachedMediaPlayer = null;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.IsPreviewing))
        {
            _ = Dispatcher.BeginInvoke(UpdateVideoPresentationState);
        }
    }

    public void SetVideoPresentationSuspended(bool isSuspended)
    {
        if (isVideoPresentationSuspended == isSuspended)
        {
            return;
        }

        isVideoPresentationSuspended = isSuspended;
        UpdateVideoPresentationState();
    }

    private void UpdateVideoPresentationState()
    {
        if (!CanPresentVideo())
        {
            ClearVideoPresentation();
            return;
        }

        EnsurePreviewVideoView();
        if (previewVideoView != null)
        {
            previewVideoView.MediaPlayer = attachedMediaPlayer;
        }

        ScheduleVideoLayoutRefresh();
    }

    private void EnsurePreviewVideoView()
    {
        if (previewVideoView != null)
        {
            return;
        }

        if (PreviewOverlayRoot.Parent is System.Windows.Controls.Panel overlayOwner)
        {
            overlayOwner.Children.Remove(PreviewOverlayRoot);
        }

        previewVideoView = new LibVLCSharp.WPF.VideoView
        {
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch,
            Content = PreviewOverlayRoot,
            Visibility = System.Windows.Visibility.Visible,
        };

        PreviewOverlayRoot.Visibility = System.Windows.Visibility.Visible;
        VideoSurface.Children.Insert(0, previewVideoView);
    }

    private void ClearVideoPresentation()
    {
        pendingVideoLayoutRefreshes = 0;
        HidePreviewControlsImmediately();

        if (previewVideoView != null)
        {
            previewVideoView.MediaPlayer = null;
            previewVideoView.Content = null;
            VideoSurface.Children.Remove(previewVideoView);
            previewVideoView.Dispose();
            previewVideoView = null;
        }

        PreviewOverlayRoot.Visibility = System.Windows.Visibility.Collapsed;
        if (PreviewOverlayRoot.Parent == null)
        {
            VideoSurface.Children.Add(PreviewOverlayRoot);
        }

        VideoSurface.UpdateLayout();
        PreviewViewport.InvalidateVisual();
    }

    private bool CanPresentVideo()
    {
        return IsLoaded
            && !isVideoPresentationSuspended
            && attachedViewModel is { IsPreviewing: true }
            && attachedMediaPlayer != null;
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
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ScheduleVideoLayoutRefresh);
            return;
        }

        if (!CanPresentVideo())
        {
            return;
        }

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

        PreviewOverlayRoot.Width = VideoSurface.Width;
        PreviewOverlayRoot.Height = VideoSurface.Height;
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
        lastTrackedPointerPosition = System.Windows.Input.Mouse.GetPosition(VideoSurface);
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
        if (!CanUsePreviewControls())
        {
            HidePreviewControlsImmediately();
            return;
        }

        UpdatePreviewControlsPlacement();
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

        PreviewControls.Opacity = 0;
        PreviewControls.IsHitTestVisible = false;
    }

    public void HidePreviewControlsImmediately()
    {
        controlsIdleTimer.Stop();
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

        if (window is MainWindow mainWindow && IsEmbeddedMode)
        {
            mainWindow.TogglePreviewFullScreen();
        }

        ShowPreviewControls();
        UpdateVideoSurfaceSize();
        UpdateWindowSizeIcon();
    }

    private void UpdatePreviewControlsPlacement()
    {
        UpdateWindowSizeIcon();
    }

    private void TrackPreviewPointer()
    {
        if (!CanUsePreviewControls())
        {
            lastTrackedPointerPosition = null;
            HidePreviewControlsImmediately();
            return;
        }

        System.Windows.Point pointerPosition = System.Windows.Input.Mouse.GetPosition(VideoSurface);
        if (!IsPointerInsideVideoSurface(pointerPosition))
        {
            lastTrackedPointerPosition = null;
            return;
        }

        if (!HasPointerMoved(lastTrackedPointerPosition, pointerPosition))
        {
            return;
        }

        lastTrackedPointerPosition = pointerPosition;
        ShowPreviewControls();
    }

    internal static bool HasPointerMoved(System.Windows.Point? previousPosition, System.Windows.Point currentPosition)
    {
        return previousPosition == null
            || Math.Abs(previousPosition.Value.X - currentPosition.X) >= 1d
            || Math.Abs(previousPosition.Value.Y - currentPosition.Y) >= 1d;
    }

    private bool IsPointerInsideVideoSurface(System.Windows.Point position)
    {
        if (VideoSurface.ActualWidth <= 0d || VideoSurface.ActualHeight <= 0d)
        {
            return false;
        }

        return position.X >= 0d
            && position.X <= VideoSurface.ActualWidth
            && position.Y >= 0d
            && position.Y <= VideoSurface.ActualHeight;
    }

    private void UpdateWindowSizeIcon()
    {
        System.Windows.Window? window = System.Windows.Window.GetWindow(this);
        bool canResizePreviewWindow = window is MainWindow && IsEmbeddedMode;
        bool isMaximized = IsFullScreen || window is MainWindow { IsPreviewFullScreenActive: true };

        WindowSizeButton.Visibility = canResizePreviewWindow ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        OpenRoomButton.Visibility = IsFullScreen ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
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
        ApplyChromeState();
    }

    private static void OnIsEmbeddedModeChanged(System.Windows.DependencyObject d, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (d is LivePreviewPanel panel)
        {
            panel.ApplyChromeState();
        }
    }

    private void ApplyChromeState()
    {
        System.Windows.Controls.Grid rootGrid = (System.Windows.Controls.Grid)PanelChrome.Child;
        bool compact = isFullScreen;

        if (compact)
        {
            PanelChrome.Padding = new System.Windows.Thickness(0);
            PanelChrome.Background = System.Windows.Media.Brushes.Black;
            PanelChrome.BorderThickness = new System.Windows.Thickness(0);
            PanelChrome.CornerRadius = isFullScreen ? new System.Windows.CornerRadius(0) : normalPanelCornerRadius;
            PlayerHeader.Visibility = System.Windows.Visibility.Collapsed;
            rootGrid.RowDefinitions[0].Height = new System.Windows.GridLength(0);
        }
        else
        {
            PanelChrome.Padding = normalPanelPadding;
            PanelChrome.Background = normalPanelBackground;
            PanelChrome.BorderThickness = normalPanelBorderThickness;
            PanelChrome.CornerRadius = normalPanelCornerRadius;
            PlayerHeader.Visibility = System.Windows.Visibility.Visible;
            rootGrid.RowDefinitions[0].Height = normalPlayerHeaderHeight;
        }

        UpdateVideoSurfaceSize();
        UpdatePreviewControlsPlacement();
        UpdateWindowSizeIcon();
        ApplyPanelClip();
        if (CanUsePreviewControls())
        {
            ShowPreviewControls();
        }
        else
        {
            HidePreviewControlsImmediately();
        }
    }

    private bool CanUsePreviewControls()
    {
        return IsLoaded
            && IsVisible
            && !isVideoPresentationSuspended
            && PreviewViewport.IsVisible
            && previewVideoView != null
            && previewVideoView.Visibility == System.Windows.Visibility.Visible
            && PreviewViewport.ActualWidth > 0
            && PreviewViewport.ActualHeight > 0
            && DataContext is ViewModels.MainViewModel { IsPreviewing: true, IsPreviewTransitioning: false };
    }

    private void ApplyPanelClip()
    {
        if (PanelChrome.ActualWidth <= 0 || PanelChrome.ActualHeight <= 0)
        {
            return;
        }

        double radius = PanelChrome.CornerRadius.TopLeft;
        PanelChrome.Clip = new System.Windows.Media.RectangleGeometry(
            new System.Windows.Rect(0, 0, PanelChrome.ActualWidth, PanelChrome.ActualHeight),
            radius,
            radius);
    }
}
