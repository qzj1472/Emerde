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

    private readonly System.Windows.Threading.DispatcherTimer topFeedbackTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1400),
    };

    private readonly System.Windows.Threading.DispatcherTimer bottomFeedbackTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1200),
    };

    private readonly System.Windows.Threading.DispatcherTimer previewClickTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(Vanara.PInvoke.User32.GetDoubleClickTime()),
    };

    private int pendingVideoLayoutRefreshes;
    private bool isVideoLayoutRefreshRunning;
    private System.Windows.Point? lastTrackedPointerPosition;
    private System.Windows.Window? attachedWindow;
    private ViewModels.MainViewModel? attachedViewModel;
    private bool isVideoPresentationSuspended;
    private bool isFullScreen;
    private bool suppressNextPreviewPointerUp;
    private System.Windows.Thickness normalPanelPadding;
    private System.Windows.Thickness normalPanelBorderThickness;
    private System.Windows.CornerRadius normalPanelCornerRadius;
    private Core.LivePreviewFrameSource? attachedFrameSource;

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
            if (value && IsLoaded && attachedViewModel is { IsPreviewing: true })
            {
                ShowTopFeedback("按 V 或 Esc 退出全屏");
            }
        }
    }

    public LivePreviewPanel()
    {
        InitializeComponent();
        normalPanelPadding = PanelChrome.Padding;
        normalPanelBorderThickness = PanelChrome.BorderThickness;
        normalPanelCornerRadius = PanelChrome.CornerRadius;
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
        topFeedbackTimer.Tick += (_, _) => HideFeedback(TopFeedback, topFeedbackTimer);
        bottomFeedbackTimer.Tick += (_, _) => HideFeedback(BottomFeedback, bottomFeedbackTimer);
        previewClickTimer.Tick += (_, _) =>
        {
            previewClickTimer.Stop();
            TogglePreviewPlayback();
        };
        Unloaded += (_, _) =>
        {
            pointerTrackingTimer.Stop();
            controlsIdleTimer.Stop();
            topFeedbackTimer.Stop();
            bottomFeedbackTimer.Stop();
            previewClickTimer.Stop();
            suppressNextPreviewPointerUp = false;
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
        attachedFrameSource = viewModel?.LivePreviewFrameSource;
        PreviewOverlayRoot.DataContext = attachedViewModel;

        if (attachedViewModel != null)
        {
            attachedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            attachedViewModel.PreviewControlFeedbackRequested += OnPreviewControlFeedbackRequested;
        }

        if (attachedMediaPlayer != null)
        {
            attachedMediaPlayer.Vout += OnMediaPlayerVout;
            attachedMediaPlayer.Playing += OnMediaPlayerPlaying;
        }

        if (attachedFrameSource != null)
        {
            attachedFrameSource.SourceChanged += OnFrameSourceChanged;
            PreviewVideoFrame.Source = attachedFrameSource.Source;
        }

        UpdateVideoPresentationState();
    }

    private void DetachMediaPlayerEvents()
    {
        ClearVideoPresentation();
        PreviewOverlayRoot.DataContext = null;

        if (attachedFrameSource != null)
        {
            attachedFrameSource.SourceChanged -= OnFrameSourceChanged;
            attachedFrameSource = null;
        }

        if (attachedViewModel != null)
        {
            attachedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            attachedViewModel.PreviewControlFeedbackRequested -= OnPreviewControlFeedbackRequested;
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

    private void OnFrameSourceChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            PreviewVideoFrame.Source = attachedFrameSource?.Source;
            ScheduleVideoLayoutRefresh();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.IsPreviewing)
            || e.PropertyName == nameof(ViewModels.MainViewModel.IsPreviewTransitioning)
            || e.PropertyName == nameof(ViewModels.MainViewModel.LivePreviewStatus))
        {
            _ = Dispatcher.BeginInvoke(UpdateVideoPresentationState);
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (CanUsePreviewControls())
                {
                    ShowPreviewControls();
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        if (e.PropertyName == nameof(ViewModels.MainViewModel.IsPreviewPaused))
        {
            _ = Dispatcher.BeginInvoke(UpdatePausedIndicator);
        }
    }

    private void OnPreviewControlFeedbackRequested(object? sender, ViewModels.PreviewControlFeedbackEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => OnPreviewControlFeedbackRequested(sender, e));
            return;
        }

        switch (e.Kind)
        {
            case ViewModels.PreviewControlFeedbackKind.Volume:
                ShowVolumeFeedback(e.Volume);
                break;
        }
    }

    private void ShowTopFeedback(string text)
    {
        TopFeedbackText.Text = text;
        ShowFeedback(TopFeedback, topFeedbackTimer);
    }

    private void ShowVolumeFeedback(int volume)
    {
        int normalizedVolume = Emerde.Core.LivePreviewPlayer.NormalizeVolume(volume);
        bool muted = normalizedVolume == 0;
        BottomVolumeFeedbackIcon.Visibility = muted ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        BottomMutedFeedbackIcon.Visibility = muted ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        BottomFeedbackText.Text = $"音量 {normalizedVolume}%";
        ShowFeedback(BottomFeedback, bottomFeedbackTimer);
    }

    private static void ShowFeedback(System.Windows.Controls.Border feedback, System.Windows.Threading.DispatcherTimer timer)
    {
        timer.Stop();
        feedback.Opacity = 1d;
        feedback.Visibility = System.Windows.Visibility.Visible;
        timer.Start();
    }

    private static void HideFeedback(System.Windows.Controls.Border feedback, System.Windows.Threading.DispatcherTimer timer)
    {
        timer.Stop();
        feedback.Visibility = System.Windows.Visibility.Collapsed;
    }

    private void UpdatePausedIndicator()
    {
        bool isVisible = attachedViewModel is { IsPreviewing: true, IsPreviewPaused: true };
        PausedIndicator.Visibility = System.Windows.Visibility.Collapsed;
        if (!isVisible || attachedViewModel is not { IsHomePageSelected: true } || !IsVisible)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(ShowPausedIndicator, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void ShowPausedIndicator()
    {
        if (attachedViewModel is not { IsHomePageSelected: true, IsPreviewing: true, IsPreviewPaused: true }
            || !IsVisible)
        {
            return;
        }

        if (TryResolvePausedIndicatorBackground(out bool isLight))
        {
            PausedIndicatorIcon.Fill = isLight
                ? System.Windows.Media.Brushes.Black
                : System.Windows.Media.Brushes.White;
        }

        PausedIndicator.Visibility = System.Windows.Visibility.Visible;
    }

    private bool TryResolvePausedIndicatorBackground(out bool isLight)
    {
        isLight = false;
        if (VideoSurface.ActualWidth <= 0d || VideoSurface.ActualHeight <= 0d)
        {
            return false;
        }

        try
        {
            System.Windows.Point center = VideoSurface.PointToScreen(
                new System.Windows.Point(VideoSurface.ActualWidth / 2d, VideoSurface.ActualHeight / 2d));
            System.Windows.DpiScale dpi = System.Windows.Media.VisualTreeHelper.GetDpi(VideoSurface);
            int sampleWidth = Math.Max(24, (int)Math.Ceiling(48d * dpi.DpiScaleX));
            int sampleHeight = Math.Max(24, (int)Math.Ceiling(48d * dpi.DpiScaleY));
            int sampleX = (int)Math.Round(center.X - sampleWidth / 2d);
            int sampleY = (int)Math.Round(center.Y - sampleHeight / 2d);
            using System.Drawing.Bitmap sample = new(sampleWidth, sampleHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(sample);
            graphics.CopyFromScreen(
                sampleX,
                sampleY,
                0,
                0,
                new System.Drawing.Size(sampleWidth, sampleHeight),
                System.Drawing.CopyPixelOperation.SourceCopy);
            isLight = IsLightPreviewSample(sample);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsLightPreviewSample(System.Drawing.Bitmap sample)
    {
        double totalLuminance = 0d;
        int pixelCount = 0;
        for (int y = 0; y < sample.Height; y += 2)
        {
            for (int x = 0; x < sample.Width; x += 2)
            {
                System.Drawing.Color color = sample.GetPixel(x, y);
                totalLuminance += color.B * 0.0722d + color.G * 0.7152d + color.R * 0.2126d;
                pixelCount++;
            }
        }

        return pixelCount > 0 && totalLuminance / pixelCount >= 145d;
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

        PreviewVideoFrame.Source = attachedFrameSource?.Source;
        PreviewVideoFrame.Visibility = System.Windows.Visibility.Visible;
        PreviewOverlayRoot.Visibility = System.Windows.Visibility.Visible;
        ScheduleVideoLayoutRefresh();
    }

    private void ClearVideoPresentation()
    {
        pendingVideoLayoutRefreshes = 0;
        HidePreviewControlsImmediately();
        HideFeedback(TopFeedback, topFeedbackTimer);
        HideFeedback(BottomFeedback, bottomFeedbackTimer);
        PausedIndicator.Visibility = System.Windows.Visibility.Collapsed;

        PreviewVideoFrame.Visibility = System.Windows.Visibility.Collapsed;
        PreviewOverlayRoot.Visibility = System.Windows.Visibility.Collapsed;
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
        if (isVideoLayoutRefreshRunning)
        {
            return;
        }

        isVideoLayoutRefreshRunning = true;
        _ = Dispatcher.BeginInvoke(RefreshVideoSurfaceSize, System.Windows.Threading.DispatcherPriority.Render);
    }

    private async void RefreshVideoSurfaceSize()
    {
        try
        {
            while (pendingVideoLayoutRefreshes > 0 && CanPresentVideo())
            {
                pendingVideoLayoutRefreshes--;
                PreviewViewport.UpdateLayout();
                UpdateVideoSurfaceSize();

                if (pendingVideoLayoutRefreshes > 0)
                {
                    await Task.Delay(250);
                }
            }
        }
        finally
        {
            isVideoLayoutRefreshRunning = false;
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

        if (!TryGetVideoDimensions(out uint videoWidth, out uint videoHeight))
        {
            videoWidth = 16;
            videoHeight = 9;
        }
        System.Windows.DpiScale dpi = System.Windows.Media.VisualTreeHelper.GetDpi(PreviewViewport);
        System.Windows.Size surfaceSize = CalculateVideoSurfaceSize(
            viewportWidth,
            viewportHeight,
            videoWidth,
            videoHeight,
            dpi.DpiScaleX,
            dpi.DpiScaleY);
        VideoSurface.Width = surfaceSize.Width;
        VideoSurface.Height = surfaceSize.Height;
        VideoSurface.UpdateLayout();
        VideoSurface.Clip = new System.Windows.Media.RectangleGeometry(
            new System.Windows.Rect(0d, 0d, surfaceSize.Width, surfaceSize.Height));
    }

    internal static System.Windows.Size CalculateVideoSurfaceSize(
        double viewportWidth,
        double viewportHeight,
        uint videoWidth,
        uint videoHeight,
        double dpiScaleX,
        double dpiScaleY)
    {
        if (viewportWidth <= 0d
            || viewportHeight <= 0d
            || videoWidth == 0
            || videoHeight == 0
            || dpiScaleX <= 0d
            || dpiScaleY <= 0d)
        {
            return new System.Windows.Size(0d, 0d);
        }

        double viewportPixelWidth = Math.Max(1d, Math.Round(viewportWidth * dpiScaleX, MidpointRounding.AwayFromZero));
        double viewportPixelHeight = Math.Max(1d, Math.Round(viewportHeight * dpiScaleY, MidpointRounding.AwayFromZero));
        double surfacePixelWidth;
        double surfacePixelHeight;

        if (viewportPixelWidth / viewportPixelHeight > (double)videoWidth / videoHeight)
        {
            surfacePixelHeight = viewportPixelHeight;
            surfacePixelWidth = Math.Min(
                viewportPixelWidth,
                Math.Max(1d, Math.Round(surfacePixelHeight * videoWidth / videoHeight, MidpointRounding.AwayFromZero)));
        }
        else
        {
            surfacePixelWidth = viewportPixelWidth;
            surfacePixelHeight = Math.Min(
                viewportPixelHeight,
                Math.Max(1d, Math.Round(surfacePixelWidth * videoHeight / videoWidth, MidpointRounding.AwayFromZero)));
        }

        return new System.Windows.Size(surfacePixelWidth / dpiScaleX, surfacePixelHeight / dpiScaleY);
    }

    private bool TryGetVideoDimensions(out uint width, out uint height)
    {
        width = 0;
        height = 0;

        if (attachedMediaPlayer != null
         && attachedMediaPlayer.VoutCount > 0
         && attachedMediaPlayer.Size(0, ref width, ref height)
         && width > 0
         && height > 0)
        {
            return true;
        }

        return false;
    }

    private void PreviewViewport_OnMouseActivity(object sender, System.Windows.Input.MouseEventArgs e)
    {
        lastTrackedPointerPosition = System.Windows.Input.Mouse.GetPosition(VideoSurface);
        ShowPreviewControls();
    }

    private void PreviewTouchLayer_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left || e.ClickCount < 2)
        {
            return;
        }

        previewClickTimer.Stop();
        suppressNextPreviewPointerUp = true;
        TogglePreviewFullScreen();
        e.Handled = true;
    }

    private void PreviewTouchLayer_OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left)
        {
            return;
        }

        e.Handled = true;
        if (suppressNextPreviewPointerUp)
        {
            suppressNextPreviewPointerUp = false;
            return;
        }

        previewClickTimer.Stop();
        previewClickTimer.Start();
    }

    private void TogglePreviewPlayback()
    {
        if (attachedViewModel is not { IsPreviewing: true, IsPreviewTransitioning: false } viewModel
            || !viewModel.TogglePreviewPauseCommand.CanExecute(null))
        {
            return;
        }

        viewModel.TogglePreviewPauseCommand.Execute(null);
    }

    private void PreviewControls_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        controlsIdleTimer.Stop();
        SetPreviewControlsVisible(true);
    }

    private void PreviewControls_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        RestartControlsIdleTimer();
    }

    private void PreviewVolumeSlider_OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Slider slider || IsThumbSource(e.OriginalSource as System.Windows.DependencyObject))
        {
            return;
        }

        double width = slider.ActualWidth;
        if (width <= 0d)
        {
            return;
        }

        double ratio = Math.Clamp(e.GetPosition(slider).X / width, 0d, 1d);
        slider.Value = slider.Minimum + (slider.Maximum - slider.Minimum) * ratio;
        e.Handled = true;
        ShowPreviewControls();
    }

    private void PreviewVolume_OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        int step = GetPreviewVolumeWheelStep(e.Delta);
        if (step == 0 || attachedViewModel is not { IsPreviewing: true } viewModel)
        {
            return;
        }

        viewModel.AdjustPreviewVolume(step);
        e.Handled = true;
        ShowPreviewControls();
    }

    internal static int GetPreviewVolumeWheelStep(int wheelDelta)
    {
        return Math.Sign(wheelDelta) * 5;
    }

    private static bool IsThumbSource(System.Windows.DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ShowPreviewControls()
    {
        if (!CanUsePreviewControls())
        {
            HidePreviewControlsImmediately();
            return;
        }

        UpdatePreviewControlsPlacement();
        SetPreviewControlsVisible(true);
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

        if (PreviewControls.IsMouseOver || PreviewCloseButton.IsMouseOver)
        {
            RestartControlsIdleTimer();
            return;
        }

        SetPreviewControlsVisible(false);
    }

    public void HidePreviewControlsImmediately()
    {
        controlsIdleTimer.Stop();
        SetPreviewControlsVisible(false);
    }

    private void SetPreviewControlsVisible(bool isVisible)
    {
        double opacity = isVisible ? 1d : 0d;
        PreviewControls.Opacity = opacity;
        PreviewControls.IsHitTestVisible = isVisible;
        PreviewCloseButton.Opacity = opacity;
        PreviewCloseButton.IsHitTestVisible = isVisible;
    }

    internal void RefreshVideoLayout()
    {
        InvalidateMeasure();
        InvalidateArrange();
        PreviewViewport.InvalidateMeasure();
        PreviewViewport.InvalidateArrange();
        VideoSurface.InvalidateMeasure();
        VideoSurface.InvalidateArrange();
        UpdateLayout();
        UpdateVideoSurfaceSize();
        ScheduleVideoLayoutRefresh();
        UpdatePreviewControlsPlacement();
    }

    private void ToggleWindowSize_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        TogglePreviewFullScreen();
    }

    private void TogglePreviewFullScreen()
    {
        System.Windows.Window? window = System.Windows.Window.GetWindow(this);

        if (window is not MainWindow mainWindow || !IsEmbeddedMode)
        {
            return;
        }

        mainWindow.TogglePreviewFullScreen();

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
        WindowSizeButton.ToolTip = $"{(isMaximized ? "PreviewRestore".Tr() : "PreviewFullScreen".Tr())} (V)";
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
        bool compact = isFullScreen;

        if (compact)
        {
            PanelChrome.Padding = new System.Windows.Thickness(0);
            PanelChrome.Background = System.Windows.Media.Brushes.Black;
            PreviewViewport.Background = System.Windows.Media.Brushes.Black;
            VideoSurface.Background = System.Windows.Media.Brushes.Black;
            PanelChrome.BorderThickness = new System.Windows.Thickness(0);
            PanelChrome.CornerRadius = isFullScreen ? new System.Windows.CornerRadius(0) : normalPanelCornerRadius;
        }
        else
        {
            PanelChrome.Padding = normalPanelPadding;
            PanelChrome.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "EmerdePanelBrush");
            PreviewViewport.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 5, 5));
            VideoSurface.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 5, 5));
            PanelChrome.BorderThickness = normalPanelBorderThickness;
            PanelChrome.CornerRadius = normalPanelCornerRadius;
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
            && PreviewVideoFrame.Visibility == System.Windows.Visibility.Visible
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
