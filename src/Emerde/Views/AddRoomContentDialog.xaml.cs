using CommunityToolkit.Mvvm.ComponentModel;
using Emerde.Core;
using Emerde.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.Views;

[ObservableObject]
public sealed partial class AddRoomContentDialog : ContentDialog
{
    private const double ExpandedDialogHeightRatio = 0.95d;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly Stopwatch loadingAnimationClock = new();
    private BitmapSource[]? loadingFrames;
    private int loadingFrameIndex = -1;
    [ObservableProperty]
    private bool isSubmitting;

    partial void OnIsSubmittingChanged(bool value)
    {
        if (LoadingFrame == null)
        {
            return;
        }

        if (value)
        {
            StartLoadingAnimation();
            return;
        }

        StopLoadingAnimation();
    }

    [ObservableProperty]
    private string? url = null;

    partial void OnUrlChanged(string? value)
    {
        HasRoomUrl = !string.IsNullOrWhiteSpace(value);
        string platformName = HasRoomUrl ? Spider.GetPlatformName(value!) : string.Empty;
        IsDetectedPlatformSupported = !string.IsNullOrWhiteSpace(platformName);
        DetectedPlatformName = string.IsNullOrWhiteSpace(platformName) ? "Unsupported".Tr() : PlatformDisplayName.Get(platformName);
        SettingsEditor?.SetPlatformName(platformName);
    }

    [ObservableProperty]
    private bool isForcedAdd = false;

    [ObservableProperty]
    private string? nickName = null;

    [ObservableProperty]
    private string detectedPlatformName = "Unsupported".Tr();

    [ObservableProperty]
    private bool hasRoomUrl;

    [ObservableProperty]
    private bool isDetectedPlatformSupported;

    [ObservableProperty]
    private bool isFollowGlobalSettings = true;

    partial void OnIsFollowGlobalSettingsChanged(bool value)
    {
        IsUsingCustomSettings = !value;
        UpdateDialogSize();
    }

    [ObservableProperty]
    private bool isUsingCustomSettings;

    partial void OnIsUsingCustomSettingsChanged(bool value)
    {
        IsFollowGlobalSettings = !value;
    }

    [ObservableProperty]
    private bool isUiXEnabled;

    public string SupportedPlatformsText => string.Join(" / ", Spider.SupportedPlatformNames.Select(PlatformDisplayName.Get));

    public string SkipValidationText => "SkipValidation".Tr();

    public string FollowGlobalSettingsText => "FollowGlobalSettings".Tr();

    public LocalSettingsContentDialog SettingsEditor { get; }

    public RoomRecordingOptions RecordingOptions => SettingsEditor.GetRecordingOptions();

    public string? RoomUrl = null;

    public ISpiderResult? SpiderResult { get; private set; }

    public AddRoomContentDialog()
    {
        SettingsEditor = new LocalSettingsContentDialog(new RoomStatusReactive
        {
            IsFollowGlobalSettings = false,
            IsToNotify = true,
            IsToMonitor = Configurations.IsToMonitor.Get(),
            IsToRecord = Configurations.IsToRecord.Get(),
        }, false, false, true);
        DataContext = this;
        InitializeComponent();
        Loaded += AddRoomContentDialogLoaded;
        Unloaded += AddRoomContentDialogUnloaded;
    }

    private void AddRoomContentDialogLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        IsUiXEnabled = Application.Current?.MainWindow?.DataContext is MainViewModel { StatusOfIsUiXEnabled: true };
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                FrameworkElement input = IsUiXEnabled ? UiXRoomUrlTextBox : RoomUrlTextBox;
                input.Focus();
                Keyboard.Focus(input);
                UpdateRoomUrlInputBorder();
                UpdateDialogSize();
            }));
    }

    private void UpdateDialogSize()
    {
        if (!IsLoaded || AddRoomSurface == null)
        {
            return;
        }

        if (!IsUiXEnabled && IsFollowGlobalSettings)
        {
            LocalSettingsContentDialog.ClearWideDialogVisualSize(this);
            Width = double.NaN;
            Height = double.NaN;
            MinWidth = 0d;
            MinHeight = 0d;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            AddRoomSurface.Width = double.NaN;
            AddRoomSurface.Height = double.NaN;
            AddRoomSurface.MinWidth = 0d;
            AddRoomSurface.MinHeight = 0d;
            AddRoomSurface.MaxWidth = double.PositiveInfinity;
            AddRoomSurface.MaxHeight = double.PositiveInfinity;
            return;
        }

        double widthRatio = IsUiXEnabled
            ? IsFollowGlobalSettings ? 0.62d : 0.78d
            : LocalSettingsContentDialog.DialogWidthRatioValue;
        double heightRatio = IsUiXEnabled
            ? IsFollowGlobalSettings ? 0.58d : 0.84d
            : ExpandedDialogHeightRatio;
        if (!LocalSettingsContentDialog.TryGetDialogVisualSize(
                Application.Current?.MainWindow,
                widthRatio,
                heightRatio,
                out double targetWidth,
                out double targetHeight))
        {
            return;
        }

        if (IsUiXEnabled)
        {
            targetWidth = Math.Min(targetWidth, IsFollowGlobalSettings ? 900d : 1120d);
            targetHeight = Math.Min(targetHeight, IsFollowGlobalSettings ? 620d : 880d);
        }

        LocalSettingsContentDialog.ApplyWideDialogVisualSize(this, targetWidth, targetHeight);
        AddRoomSurface.Width = double.NaN;
        AddRoomSurface.Height = double.NaN;
        AddRoomSurface.MinWidth = 0d;
        AddRoomSurface.MinHeight = 0d;
        AddRoomSurface.MaxWidth = double.PositiveInfinity;
        AddRoomSurface.MaxHeight = double.PositiveInfinity;
    }

    private void RoomUrlTextBoxFocusWithinChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        UpdateRoomUrlInputBorder();
    }

    private void UpdateRoomUrlInputBorder()
    {
        string brushKey = (IsUiXEnabled ? UiXRoomUrlTextBox : RoomUrlTextBox).IsKeyboardFocusWithin
            ? "SystemAccentColorPrimaryBrush"
            : IsUiXEnabled ? "UiXStrongStrokeBrush" : "ControlStrokeColorDefaultBrush";
        (IsUiXEnabled ? UiXRoomUrlInputBorder : RoomUrlInputBorder)
            .SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, brushKey);
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        var deferral = e.GetDeferral();
        if (IsSubmitting)
        {
            e.Cancel = true;
            deferral.Complete();
            return;
        }

        IsSubmitting = true;
        IsPrimaryButtonEnabled = false;
        CancellationToken token = lifetimeCancellation.Token;
        try
        {
            AddRoomResolution result = await AddRoomResolutionService.ResolveAsync(Url, IsForcedAdd, token);
            if (!result.IsSuccess)
            {
                e.Cancel = true;
                if (result.IsWarning)
                {
                    Toast.Warning(result.ErrorMessage);
                }
                else
                {
                    Toast.Error(result.ErrorMessage);
                }
                return;
            }

            NickName = result.NickName;
            RoomUrl = result.RoomUrl;
            SpiderResult = result.SpiderResult;
            if (result.IsDeferred)
            {
                Toast.Warning("AddRoomSucc".Tr(NickName));
            }
            else
            {
                Toast.Success("AddRoomSucc".Tr(NickName));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            e.Cancel = true;
        }
        catch (Exception exception)
        {
            e.Cancel = true;
            AppSessionLogger.WriteException(exception);
            Toast.Error(GetRoomInfoErrorMessage(Url, exception.Message));
        }
        finally
        {
            IsSubmitting = false;
            IsPrimaryButtonEnabled = true;
            deferral.Complete();
        }
    }

    internal static string GetRoomInfoErrorMessage(string? roomUrl, string? fallback = null)
    {
        _ = roomUrl;
        _ = fallback;
        return "GetRoomInfoError".Tr();
    }

    private void AddRoomContentDialogUnloaded(object sender, RoutedEventArgs e)
    {
        lifetimeCancellation.Cancel();
        StopLoadingAnimation();
        loadingFrames = null;
        LoadingFrame.Visibility = Visibility.Visible;
        LoadingFallbackProgressBar.Visibility = Visibility.Collapsed;
    }

    private void StartLoadingAnimation()
    {
        try
        {
            loadingFrames ??= RoomLoadingAtlas.Frames;
        }
        catch (Exception exception)
        {
            AppSessionLogger.WriteException(exception);
            LoadingFrame.Source = null;
            LoadingFrame.Visibility = Visibility.Collapsed;
            LoadingFallbackProgressBar.Visibility = Visibility.Visible;
            return;
        }

        LoadingFrame.Visibility = Visibility.Visible;
        LoadingFallbackProgressBar.Visibility = Visibility.Collapsed;
        loadingAnimationClock.Restart();
        loadingFrameIndex = -1;
        CompositionTarget.Rendering -= LoadingAnimationRendering;
        CompositionTarget.Rendering += LoadingAnimationRendering;
        SetLoadingFrame(0);
    }

    private void StopLoadingAnimation()
    {
        CompositionTarget.Rendering -= LoadingAnimationRendering;
        loadingAnimationClock.Stop();
        loadingFrameIndex = -1;
        LoadingFrame.Source = null;
    }

    private void LoadingAnimationRendering(object? sender, EventArgs e)
    {
        if (!IsSubmitting || loadingFrames == null)
        {
            return;
        }

        int frameIndex = (int)(loadingAnimationClock.Elapsed.TotalSeconds * RoomLoadingAtlas.FrameRate) % RoomLoadingAtlas.FrameCount;
        SetLoadingFrame(frameIndex);
    }

    private void SetLoadingFrame(int frameIndex)
    {
        if (loadingFrames == null || loadingFrameIndex == frameIndex)
        {
            return;
        }

        LoadingFrame.Source = loadingFrames[frameIndex];
        loadingFrameIndex = frameIndex;
    }

    internal static bool HasAddableRoomInfo(ISpiderResult? spider, string? roomUrl)
    {
        return AddRoomResolutionService.HasAddableRoomInfo(spider, roomUrl);
    }

    internal static bool CanDeferRoomInfoResolution(string? roomUrl, string? error)
    {
        return AddRoomResolutionService.CanDeferRoomInfoResolution(roomUrl, error);
    }

    internal static string GetConfirmedNickName(ISpiderResult spider)
    {
        return AddRoomResolutionService.GetConfirmedNickName(spider);
    }
}
