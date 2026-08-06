namespace Emerde.Views;

public partial class LivePreviewPanel : System.Windows.Controls.UserControl
{
    private const int PreviewRoomTransitionDurationMilliseconds = 240;

    public static readonly System.Windows.DependencyProperty IsEmbeddedModeProperty = System.Windows.DependencyProperty.Register(
        nameof(IsEmbeddedMode),
        typeof(bool),
        typeof(LivePreviewPanel),
        new System.Windows.PropertyMetadata(false, OnIsEmbeddedModeChanged));

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

    private readonly System.Windows.Threading.DispatcherTimer previewRoomTransitionTimeoutTimer = new()
    {
        Interval = TimeSpan.FromSeconds(3),
    };

    private int pendingVideoLayoutRefreshes;
    private bool isVideoLayoutRefreshRunning;
    private CancellationTokenSource? videoLayoutRefreshCancellation;
    private System.Windows.Window? attachedWindow;
    private ViewModels.MainViewModel? attachedViewModel;
    private bool isVideoPresentationSuspended;
    private bool isPreviewClosingTransitionActive;
    private bool isFullScreen;
    private bool suppressNextPreviewPointerUp;
    private System.Windows.Thickness normalPanelPadding;
    private System.Windows.Thickness normalPanelBorderThickness;
    private System.Windows.CornerRadius normalPanelCornerRadius;
    private Core.LivePreviewFrameSource? attachedFrameSource;
    private string? displayedPreviewRoomUrl;
    private Core.LivePreviewFrameSource? previewRoomTransitionPreviousFrameSource;
    private Core.LivePreviewFrameSource? previewRoomTransitionTargetFrameSource;
    private int previewRoomTransitionAnimationGeneration;
    private bool isPreviewRoomTransitionPending;
    private bool hasPreviewRoomTransitionFrame;
    private bool isPreviewCursorHidden;
    private System.Windows.FrameworkElement? previewCursorScope;
    private object? previewCursorLocalValue;

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
                ShowTopFeedback("ExitFullScreenHint".Tr());
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
        controlsIdleTimer.Tick += (_, _) => HidePreviewControls();
        topFeedbackTimer.Tick += (_, _) => HideFeedback(TopFeedback, topFeedbackTimer);
        bottomFeedbackTimer.Tick += (_, _) => HideFeedback(BottomFeedback, bottomFeedbackTimer);
        previewClickTimer.Tick += (_, _) =>
        {
            previewClickTimer.Stop();
            TogglePreviewPlayback();
        };
        previewRoomTransitionTimeoutTimer.Tick += (_, _) =>
        {
            previewRoomTransitionTimeoutTimer.Stop();
            if (isPreviewRoomTransitionPending)
            {
                BeginPreviewRoomTransitionFadeOut();
            }
        };
        Unloaded += (_, _) =>
        {
            controlsIdleTimer.Stop();
            topFeedbackTimer.Stop();
            bottomFeedbackTimer.Stop();
            previewClickTimer.Stop();
            previewRoomTransitionTimeoutTimer.Stop();
            suppressNextPreviewPointerUp = false;
            CancelVideoLayoutRefresh();
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
        displayedPreviewRoomUrl = viewModel?.PreviewingRoom?.RoomUrl;
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
            attachedFrameSource.FirstFramePresented += OnFirstFramePresented;
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
            attachedFrameSource.FirstFramePresented -= OnFirstFramePresented;
            attachedFrameSource = null;
        }

        displayedPreviewRoomUrl = null;

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

    private void OnFirstFramePresented(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => OnFirstFramePresented(sender, e), System.Windows.Threading.DispatcherPriority.Render);
            return;
        }

        if (!isPreviewRoomTransitionPending
            || sender is not Core.LivePreviewFrameSource frameSource
            || !ReferenceEquals(frameSource, previewRoomTransitionTargetFrameSource))
        {
            return;
        }

        hasPreviewRoomTransitionFrame = true;
        CompletePreviewRoomTransitionIfReady();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.LivePreviewMediaPlayer)
            || e.PropertyName == nameof(ViewModels.MainViewModel.LivePreviewFrameSource))
        {
            _ = Dispatcher.BeginInvoke(RefreshAttachedPlaybackSources, System.Windows.Threading.DispatcherPriority.Render);
        }

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

        if (e.PropertyName == nameof(ViewModels.MainViewModel.PreviewingRoom))
        {
            if (Dispatcher.CheckAccess())
            {
                UpdatePreviewRoomTransition();
            }
            else
            {
                _ = Dispatcher.BeginInvoke(UpdatePreviewRoomTransition);
            }
        }

        if (e.PropertyName == nameof(ViewModels.MainViewModel.IsPreviewPaused))
        {
            _ = Dispatcher.BeginInvoke(UpdatePausedIndicator);
        }
    }

    private void RefreshAttachedPlaybackSources()
    {
        if (!IsLoaded)
        {
            return;
        }

        ViewModels.MainViewModel? viewModel = attachedViewModel;
        if (viewModel == null)
        {
            AttachMediaPlayerEvents();
            return;
        }

        LibVLCSharp.Shared.MediaPlayer mediaPlayer = viewModel.LivePreviewMediaPlayer;
        Core.LivePreviewFrameSource frameSource = viewModel.LivePreviewFrameSource;
        if (ReferenceEquals(attachedMediaPlayer, mediaPlayer)
            && ReferenceEquals(attachedFrameSource, frameSource))
        {
            PreviewVideoFrame.Source = frameSource.Source;
            UpdateVideoPresentationState();
            return;
        }

        if (attachedFrameSource != null)
        {
            attachedFrameSource.SourceChanged -= OnFrameSourceChanged;
            attachedFrameSource.FirstFramePresented -= OnFirstFramePresented;
        }
        if (attachedMediaPlayer != null)
        {
            attachedMediaPlayer.Vout -= OnMediaPlayerVout;
            attachedMediaPlayer.Playing -= OnMediaPlayerPlaying;
        }

        attachedMediaPlayer = mediaPlayer;
        attachedFrameSource = frameSource;
        attachedMediaPlayer.Vout += OnMediaPlayerVout;
        attachedMediaPlayer.Playing += OnMediaPlayerPlaying;
        attachedFrameSource.SourceChanged += OnFrameSourceChanged;
        attachedFrameSource.FirstFramePresented += OnFirstFramePresented;
        AttachPreviewRoomTransitionTarget(attachedFrameSource);
        PreviewVideoFrame.Source = attachedFrameSource.Source;
        ScheduleVideoLayoutRefresh();
        UpdateVideoPresentationState();
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
        BottomFeedbackText.Text = "VolumeFormat".Tr(normalizedVolume);
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
            int sampleX = (int)Math.Round(center.X - sampleWidth / 2d, MidpointRounding.AwayFromZero);
            int sampleY = (int)Math.Round(center.Y - sampleHeight / 2d, MidpointRounding.AwayFromZero);
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

    public void SetVideoPresentationState(bool isSuspended, bool isClosingTransitionActive)
    {
        if (isVideoPresentationSuspended == isSuspended
            && isPreviewClosingTransitionActive == isClosingTransitionActive)
        {
            return;
        }

        isVideoPresentationSuspended = isSuspended;
        isPreviewClosingTransitionActive = isClosingTransitionActive;
        UpdateVideoPresentationState();
    }

    private void UpdateVideoPresentationState()
    {
        if (isVideoPresentationSuspended)
        {
            ClearVideoPresentation();
            return;
        }

        if (isPreviewClosingTransitionActive)
        {
            return;
        }

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
        CancelPreviewRoomTransition();
        CancelVideoLayoutRefresh();
        HidePreviewControlsImmediately();
        HideFeedback(TopFeedback, topFeedbackTimer);
        HideFeedback(BottomFeedback, bottomFeedbackTimer);
        PausedIndicator.Visibility = System.Windows.Visibility.Collapsed;

        PreviewVideoFrame.Visibility = System.Windows.Visibility.Collapsed;
        PreviewOverlayRoot.Visibility = System.Windows.Visibility.Collapsed;
        VideoSurface.UpdateLayout();
        PreviewViewport.InvalidateVisual();
    }

    private void UpdatePreviewRoomTransition()
    {
        string? nextRoomUrl = attachedViewModel?.PreviewingRoom?.RoomUrl;
        string? previousRoomUrl = displayedPreviewRoomUrl;
        displayedPreviewRoomUrl = nextRoomUrl;

        if (!ShouldAnimatePreviewRoomSwitch(previousRoomUrl, nextRoomUrl, attachedFrameSource?.Source != null))
        {
            CancelPreviewRoomTransition();
            return;
        }

        System.Windows.Media.Imaging.BitmapSource? snapshot = CreatePreviewRoomSnapshot(attachedFrameSource?.Source);
        if (snapshot == null || attachedFrameSource == null)
        {
            CancelPreviewRoomTransition();
            return;
        }

        previewRoomTransitionAnimationGeneration++;
        PreviewRoomTransitionFrame.BeginAnimation(OpacityProperty, null);
        PreviewRoomTransitionFrame.Source = snapshot;
        PreviewRoomTransitionFrame.Opacity = 1d;
        PreviewRoomTransitionFrame.Visibility = System.Windows.Visibility.Collapsed;
        previewRoomTransitionPreviousFrameSource = attachedFrameSource;
        previewRoomTransitionTargetFrameSource = null;
        hasPreviewRoomTransitionFrame = false;
        isPreviewRoomTransitionPending = true;
        previewRoomTransitionTimeoutTimer.Stop();
    }

    private void CompletePreviewRoomTransitionIfReady()
    {
        if (!isPreviewRoomTransitionPending
            || !hasPreviewRoomTransitionFrame
            || attachedViewModel is not { IsPreviewing: true })
        {
            return;
        }

        BeginPreviewRoomTransitionFadeOut();
    }

    private void AttachPreviewRoomTransitionTarget(Core.LivePreviewFrameSource frameSource)
    {
        if (!isPreviewRoomTransitionPending
            || ReferenceEquals(frameSource, previewRoomTransitionPreviousFrameSource))
        {
            return;
        }

        previewRoomTransitionTargetFrameSource = frameSource;
        PreviewRoomTransitionFrame.BeginAnimation(OpacityProperty, null);
        PreviewRoomTransitionFrame.Opacity = 1d;
        PreviewRoomTransitionFrame.Visibility = System.Windows.Visibility.Visible;
        previewRoomTransitionTimeoutTimer.Stop();
        previewRoomTransitionTimeoutTimer.Start();
        if (frameSource.HasPresentedFrame)
        {
            hasPreviewRoomTransitionFrame = true;
            CompletePreviewRoomTransitionIfReady();
        }
    }

    private void BeginPreviewRoomTransitionFadeOut()
    {
        isPreviewRoomTransitionPending = false;
        previewRoomTransitionTimeoutTimer.Stop();
        int animationGeneration = ++previewRoomTransitionAnimationGeneration;
        System.Windows.Media.Animation.DoubleAnimation animation = new(1d, 0d, TimeSpan.FromMilliseconds(PreviewRoomTransitionDurationMilliseconds))
        {
            EasingFunction = new System.Windows.Media.Animation.SineEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut,
            },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            if (previewRoomTransitionAnimationGeneration != animationGeneration)
            {
                return;
            }

            ClearPreviewRoomTransitionFrame();
        };
        PreviewRoomTransitionFrame.BeginAnimation(OpacityProperty, animation, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    private void CancelPreviewRoomTransition()
    {
        isPreviewRoomTransitionPending = false;
        hasPreviewRoomTransitionFrame = false;
        previewRoomTransitionPreviousFrameSource = null;
        previewRoomTransitionTargetFrameSource = null;
        previewRoomTransitionTimeoutTimer.Stop();
        previewRoomTransitionAnimationGeneration++;
        ClearPreviewRoomTransitionFrame();
    }

    private void ClearPreviewRoomTransitionFrame()
    {
        PreviewRoomTransitionFrame.BeginAnimation(OpacityProperty, null);
        PreviewRoomTransitionFrame.Opacity = 0d;
        PreviewRoomTransitionFrame.Visibility = System.Windows.Visibility.Collapsed;
        PreviewRoomTransitionFrame.Source = null;
    }

    private static System.Windows.Media.Imaging.BitmapSource? CreatePreviewRoomSnapshot(System.Windows.Media.Imaging.BitmapSource? source)
    {
        if (source is not { PixelWidth: > 0, PixelHeight: > 0 })
        {
            return null;
        }

        try
        {
            System.Windows.Media.Imaging.WriteableBitmap snapshot = new(source);
            snapshot.Freeze();
            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    internal static bool ShouldAnimatePreviewRoomSwitch(string? previousRoomUrl, string? nextRoomUrl, bool hasCurrentFrame)
    {
        return hasCurrentFrame
            && !string.IsNullOrWhiteSpace(previousRoomUrl)
            && !string.IsNullOrWhiteSpace(nextRoomUrl)
            && !string.Equals(previousRoomUrl, nextRoomUrl, StringComparison.OrdinalIgnoreCase);
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
        CancellationTokenSource cancellation = new();
        videoLayoutRefreshCancellation = cancellation;
        _ = Dispatcher.BeginInvoke(
            () => _ = RefreshVideoSurfaceSizeAsync(cancellation),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private async Task RefreshVideoSurfaceSizeAsync(CancellationTokenSource cancellation)
    {
        try
        {
            while (pendingVideoLayoutRefreshes > 0
                   && !cancellation.IsCancellationRequested
                   && CanPresentVideo())
            {
                pendingVideoLayoutRefreshes--;
                PreviewViewport.UpdateLayout();
                UpdateVideoSurfaceSize();

                if (pendingVideoLayoutRefreshes > 0)
                {
                    await Task.Delay(250, cancellation.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(videoLayoutRefreshCancellation, cancellation))
            {
                videoLayoutRefreshCancellation = null;
            }
            cancellation.Dispose();
            isVideoLayoutRefreshRunning = false;
            if (pendingVideoLayoutRefreshes > 0 && CanPresentVideo())
            {
                ScheduleVideoLayoutRefresh();
            }
        }
    }

    private void CancelVideoLayoutRefresh()
    {
        pendingVideoLayoutRefreshes = 0;
        videoLayoutRefreshCancellation?.Cancel();
        if (!isVideoLayoutRefreshRunning)
        {
            videoLayoutRefreshCancellation?.Dispose();
            videoLayoutRefreshCancellation = null;
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
        RestorePreviewCursor();
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
        HidePreviewCursor();
    }

    public void HidePreviewControlsImmediately()
    {
        controlsIdleTimer.Stop();
        SetPreviewControlsVisible(false);
        RestorePreviewCursor();
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

    private System.Windows.FrameworkElement GetPreviewPointerScope()
    {
        return isFullScreen ? PanelChrome : VideoSurface;
    }

    private void HidePreviewCursor()
    {
        System.Windows.FrameworkElement pointerScope = GetPreviewPointerScope();
        System.Windows.Point pointerPosition = System.Windows.Input.Mouse.GetPosition(pointerScope);
        if (!IsPointerInsideElement(pointerScope, pointerPosition))
        {
            return;
        }

        if (isPreviewCursorHidden)
        {
            if (ReferenceEquals(previewCursorScope, pointerScope))
            {
                return;
            }

            RestorePreviewCursor();
        }

        previewCursorScope = pointerScope;
        previewCursorLocalValue = HideCursorForElement(pointerScope);
        isPreviewCursorHidden = true;
    }

    private void RestorePreviewCursor()
    {
        if (!isPreviewCursorHidden)
        {
            return;
        }

        if (previewCursorScope != null)
        {
            RestoreCursorForElement(previewCursorScope, previewCursorLocalValue);
        }

        previewCursorScope = null;
        previewCursorLocalValue = null;
        isPreviewCursorHidden = false;
    }

    internal static object HideCursorForElement(System.Windows.FrameworkElement element)
    {
        object localValue = element.ReadLocalValue(System.Windows.FrameworkElement.CursorProperty);
        element.Cursor = System.Windows.Input.Cursors.None;
        return localValue;
    }

    internal static void RestoreCursorForElement(System.Windows.FrameworkElement element, object? localValue)
    {
        if (!ReferenceEquals(
                element.ReadLocalValue(System.Windows.FrameworkElement.CursorProperty),
                System.Windows.Input.Cursors.None))
        {
            return;
        }

        if (localValue == null || localValue == System.Windows.DependencyProperty.UnsetValue)
        {
            element.ClearValue(System.Windows.FrameworkElement.CursorProperty);
            return;
        }

        element.SetValue(System.Windows.FrameworkElement.CursorProperty, localValue);
    }

    private static bool IsPointerInsideElement(System.Windows.FrameworkElement element, System.Windows.Point position)
    {
        if (element.ActualWidth <= 0d || element.ActualHeight <= 0d)
        {
            return false;
        }

        return position.X >= 0d
            && position.X <= element.ActualWidth
            && position.Y >= 0d
            && position.Y <= element.ActualHeight;
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
        RestorePreviewCursor();

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
