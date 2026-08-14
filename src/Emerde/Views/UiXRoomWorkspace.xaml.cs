using CommunityToolkit.Mvvm.ComponentModel;
using Emerde.Core;
using Emerde.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using MediaBrush = System.Windows.Media.Brush;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;
using WpfRadioButton = System.Windows.Controls.RadioButton;

namespace Emerde.Views;

internal sealed record UiXRoomWorkspaceResult(
    bool IsConfirmed,
    string NickName,
    string RoomUrl,
    ISpiderResult? SpiderResult,
    bool IsFollowGlobalSettings,
    bool IsToNotify,
    bool IsToMonitor,
    bool IsToRecord,
    RoomRecordingOptions RecordingOptions);

[ObservableObject]
public sealed partial class UiXRoomWorkspace : WpfUserControl, IDisposable
{
    private static readonly BooleanVisibilityConverter boolToVisibility = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly TaskCompletionSource<UiXRoomWorkspaceResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer automaticResolutionTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Stopwatch loadingAnimationClock = new();
    private readonly bool isAddMode;
    private CancellationTokenSource? resolutionCancellation;
    private bool isApplyingResolvedAddress;
    private BitmapSource[]? loadingFrames;
    private int loadingFrameIndex = -1;
    private AddRoomResolution? resolution;

    public static IValueConverter BoolToVisibility => boolToVisibility;

    public LocalSettingsContentDialog Editor { get; }

    [ObservableProperty]
    private int selectedStage = 1;

    partial void OnSelectedStageChanged(int value)
    {
        int normalized = Math.Clamp(value, 0, 3);
        if (normalized != value)
        {
            SelectedStage = normalized;
            return;
        }

        OnPropertyChanged(nameof(RunVisibility));
        OnPropertyChanged(nameof(RecordingVisibility));
        OnPropertyChanged(nameof(OutputVisibility));
        StageScrollViewer?.ScrollToTop();
        Dispatcher.BeginInvoke(UpdateSurfaceSize);
    }

    [ObservableProperty]
    private string roomUrlInput = string.Empty;

    partial void OnRoomUrlInputChanged(string value)
    {
        resolution = null;
        DetectedNickName = string.Empty;
        string platformName = string.IsNullOrWhiteSpace(value) ? string.Empty : Spider.GetPlatformName(value);
        Editor.SetPlatformName(platformName);
        DetectionText = string.IsNullOrWhiteSpace(value)
            ? "UiXWorkspaceWaitingForAddress".Tr()
            : string.IsNullOrWhiteSpace(platformName)
                ? "Unsupported".Tr()
                : PlatformDisplayName.Get(platformName);
        DetectionForeground = FindBrush(string.IsNullOrWhiteSpace(platformName) ? "UiXDangerForegroundBrush" : "UiXTextSecondaryBrush");
        OnPropertyChanged(nameof(CanResolve));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(IdentitySubtitle));
        OnPropertyChanged(nameof(WorkspaceTitle));
        QueueAutomaticResolution();
    }

    [ObservableProperty]
    private string detectedNickName = string.Empty;

    [ObservableProperty]
    private string detectionText = string.Empty;

    [ObservableProperty]
    private MediaBrush detectionForeground = System.Windows.Media.Brushes.Gray;

    [ObservableProperty]
    private bool isForcedAdd;

    partial void OnIsForcedAddChanged(bool value)
    {
        _ = value;
        resolution = null;
        OnPropertyChanged(nameof(CanConfirm));
        QueueAutomaticResolution();
    }

    [ObservableProperty]
    private bool isResolving;

    partial void OnIsResolvingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanResolve));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(LoadingVisibility));
        if (!IsLoaded)
        {
            return;
        }

        if (value)
        {
            StartLoadingAnimation();
        }
        else
        {
            StopLoadingAnimation();
        }
    }

    public bool IsCustomMode
    {
        get => !Editor.IsFollowGlobalSettings;
        set
        {
            if (value == IsCustomMode)
            {
                return;
            }
            Editor.IsFollowGlobalSettings = !value;
            OnPropertyChanged();
        }
    }

    public string TaskTitle => isAddMode ? "AddRoom".Tr() : "SingleSettings".Tr();

    public string WorkspaceTitle => isAddMode
        ? string.IsNullOrWhiteSpace(DetectedNickName) ? "UiXWorkspaceWaitingForAddress".Tr() : DetectedNickName
        : Editor.NickName;

    public string IdentitySubtitle => isAddMode ? RoomUrlInput : Editor.RoomUrl;

    public string AvatarSource { get; }

    public Visibility AddIdentityVisibility => isAddMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LoadingVisibility => IsResolving ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CustomWorkspaceVisibility => IsCustomMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RunVisibility => SelectedStage == 1 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RecordingVisibility => SelectedStage == 2 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OutputVisibility => SelectedStage == 3 ? Visibility.Visible : Visibility.Collapsed;

    public bool CanResolve => isAddMode && !IsResolving && !string.IsNullOrWhiteSpace(RoomUrlInput);

    public bool CanConfirm => !IsResolving && (!isAddMode || resolution?.IsSuccess == true || !string.IsNullOrWhiteSpace(RoomUrlInput));

    public string ConfirmButtonText => isAddMode ? "ButtonOfAdd".Tr() : "Save".Tr();

    public string CancelButtonText => isAddMode ? "ButtonOfClose".Tr() : "ButtonOfCancel".Tr();

    public string ModeSummary => Editor.IsFollowGlobalSettings ? "FollowGlobalSettings".Tr() : "Custom".Tr();

    public string RunSummary
    {
        get
        {
            string monitor = Editor.IsToMonitor ? "Monitor".Tr() : "PauseMonitor".Tr();
            string record = Editor.IsToRecord ? "EnableRecord".Tr() : "PauseRecording".Tr();
            return $"{monitor} · {record} · {GetScheduleName(Editor.RoutineScheduleModeIndex)}";
        }
    }

    public string RecordingSummary
    {
        get
        {
            string quality = Editor.QualityOptions.FirstOrDefault(option => option.Value == Editor.PreferredQuality)?.DisplayName
                ?? Editor.PreferredQuality;
            string format = Editor.RecordFormatIndex switch
            {
                1 => "MP4",
                2 => "MKV",
                _ => "TS/FLV",
            };
            string segment = Editor.IsToSegment
                ? $"{Editor.SegmentTimeValue:0.##} {Editor.SegmentUnitOptions.FirstOrDefault(item => item.Value == Editor.SegmentTimeUnitIndex)?.DisplayName}"
                : "UiXWorkspaceNoSegment".Tr();
            return $"{quality} · {format} · {segment}";
        }
    }

    public string OutputSummary => Editor.SaveFolderPathLevelIndex switch
    {
        0 => "SavePathLevelRootOnly".Tr(),
        1 => "SavePathLevelAuthor".Tr(),
        2 => "SavePathLevelAuthorYearMonth".Tr(),
        _ => "SavePathLevelAuthorDate".Tr(),
    };

    private UiXRoomWorkspace(RoomStatusReactive room, bool addMode)
    {
        isAddMode = addMode;
        Editor = new LocalSettingsContentDialog(room, false, false, addMode, false);
        Editor.PropertyChanged += EditorPropertyChanged;
        AvatarSource = string.IsNullOrWhiteSpace(room.AvatarDisplaySource)
            ? "pack://application:,,,/Assets/Favicon.png"
            : room.AvatarDisplaySource;
        RoomUrlInput = addMode ? string.Empty : room.RoomUrl;
        DetectionText = "UiXWorkspaceWaitingForAddress".Tr();
        DetectionForeground = FindBrush("UiXTextSecondaryBrush");
        DataContext = this;
        InitializeComponent();
        automaticResolutionTimer.Tick += AutomaticResolutionTimerTick;
        Loaded += WorkspaceLoaded;
        Unloaded += WorkspaceUnloaded;
    }

    internal static UiXRoomWorkspace CreateForAdd()
    {
        return new UiXRoomWorkspace(new RoomStatusReactive
        {
            IsFollowGlobalSettings = true,
            IsToNotify = true,
            IsToMonitor = Configurations.IsToMonitor.Get(),
            IsToRecord = Configurations.IsToRecord.Get(),
        }, true);
    }

    internal static UiXRoomWorkspace CreateForEdit(RoomStatusReactive room) => new(room, false);

    internal Task<UiXRoomWorkspaceResult> WaitForResultAsync() => completion.Task;

    private void WorkspaceLoaded(object sender, RoutedEventArgs e)
    {
        if (isAddMode)
        {
            AlignAddressClearButton();
            RoomUrlTextBox.Focus();
            QueueAutomaticResolution();
        }
        UpdateSurfaceSize();
        if (IsResolving)
        {
            StartLoadingAnimation();
        }
        if (Window.GetWindow(this) is Window window)
        {
            window.SizeChanged -= OwnerSizeChanged;
            window.SizeChanged += OwnerSizeChanged;
            window.Deactivated -= OwnerDeactivated;
            window.Deactivated += OwnerDeactivated;
        }
    }

    private void AlignAddressClearButton()
    {
        RoomUrlTextBox.ApplyTemplate();
        foreach (WpfButton button in FindVisualChildren<WpfButton>(RoomUrlTextBox))
        {
            button.Padding = new Thickness(0);
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
            button.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
            button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        }
    }

    private void WorkspaceUnloaded(object sender, RoutedEventArgs e)
    {
        StopLoadingAnimation();
        if (Window.GetWindow(this) is Window window)
        {
            window.SizeChanged -= OwnerSizeChanged;
            window.Deactivated -= OwnerDeactivated;
        }
    }

    private void OwnerDeactivated(object? sender, EventArgs e)
    {
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, null);
    }

    private void OwnerSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSurfaceSize();

    private void UpdateSurfaceSize()
    {
        Window? owner = Window.GetWindow(this);
        if (owner == null || owner.ActualWidth <= 0d || owner.ActualHeight <= 0d)
        {
            return;
        }

        double preferredWidth = IsCustomMode ? 832d : 680d;
        WorkspaceSurface.Width = Math.Max(1d, Math.Min(preferredWidth, owner.ActualWidth - 52d));
        WorkspaceSurface.Height = double.NaN;
        WorkspaceSurface.MaxHeight = Math.Max(1d, owner.ActualHeight - 74d);
    }

    private void EditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalSettingsContentDialog.IsFollowGlobalSettings))
        {
            OnPropertyChanged(nameof(IsCustomMode));
            OnPropertyChanged(nameof(CustomWorkspaceVisibility));
            OnPropertyChanged(nameof(ModeSummary));
            Dispatcher.BeginInvoke(UpdateSurfaceSize);
        }
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(RecordingSummary));
        OnPropertyChanged(nameof(OutputSummary));
    }

    private void QueueAutomaticResolution()
    {
        if (!isAddMode || isApplyingResolvedAddress)
        {
            return;
        }

        automaticResolutionTimer.Stop();
        resolutionCancellation?.Cancel();
        IsResolving = false;
        if (!string.IsNullOrWhiteSpace(RoomUrlInput))
        {
            automaticResolutionTimer.Start();
        }
    }

    private async void AutomaticResolutionTimerTick(object? sender, EventArgs e)
    {
        automaticResolutionTimer.Stop();
        await ResolveRoomAsync(false);
    }

    private async Task<bool> ResolveRoomAsync(bool showErrorToast = true)
    {
        if (!CanResolve)
        {
            return resolution?.IsSuccess == true;
        }

        IsResolving = true;
        resolutionCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
        CancellationTokenSource currentResolution = resolutionCancellation;
        try
        {
            AddRoomResolution resolved = await AddRoomResolutionService.ResolveAsync(RoomUrlInput, IsForcedAdd, currentResolution.Token);
            if (!resolved.IsSuccess)
            {
                resolution = null;
                DetectionText = resolved.ErrorMessage;
                DetectionForeground = FindBrush("UiXDangerForegroundBrush");
                if (showErrorToast)
                {
                    Wpf.Ui.Violeta.Controls.Toast.Error(resolved.ErrorMessage);
                }
                return false;
            }

            try
            {
                isApplyingResolvedAddress = true;
                RoomUrlInput = resolved.RoomUrl;
            }
            finally
            {
                isApplyingResolvedAddress = false;
            }
            DetectedNickName = resolved.NickName;
            resolution = resolved;
            string platformName = string.IsNullOrWhiteSpace(resolved.SpiderResult?.PlatformName)
                ? Spider.GetPlatformName(resolved.RoomUrl)
                : resolved.SpiderResult.PlatformName;
            Editor.SetPlatformName(platformName);
            DetectionText = $"{PlatformDisplayName.Get(platformName)} · {resolved.NickName}";
            DetectionForeground = FindBrush("UiXLiveFillBrush");
            OnPropertyChanged(nameof(WorkspaceTitle));
            OnPropertyChanged(nameof(IdentitySubtitle));
            OnPropertyChanged(nameof(CanConfirm));
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(resolutionCancellation, currentResolution))
            {
                resolutionCancellation = null;
                IsResolving = false;
            }
            currentResolution.Dispose();
        }
    }

    private void StageButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfRadioButton { CommandParameter: string stage } && int.TryParse(stage, out int index))
        {
            SelectedStage = index;
        }
    }

    private void CustomModeChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        SelectedStage = 1;
        SetCheckedStage(1);
    }

    private void SetCheckedStage(int index)
    {
        RunStageButton.IsChecked = index == 1;
        RecordingStageButton.IsChecked = index == 2;
        OutputStageButton.IsChecked = index == 3;
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
        if (!IsResolving || loadingFrames == null)
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

    private async void ConfirmClick(object sender, RoutedEventArgs e)
    {
        if (isAddMode && resolution?.IsSuccess != true && !await ResolveRoomAsync())
        {
            return;
        }

        AddRoomResolution confirmed = resolution ?? new AddRoomResolution(
            true,
            Editor.NickName,
            Editor.RoomUrl,
            null,
            string.Empty);
        completion.TrySetResult(new UiXRoomWorkspaceResult(
            true,
            confirmed.NickName,
            confirmed.RoomUrl,
            confirmed.SpiderResult,
            Editor.IsFollowGlobalSettings,
            Editor.IsToNotify,
            Editor.IsToMonitor,
            Editor.IsToRecord,
            Editor.GetRecordingOptions()));
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        CompleteCancellation();
    }

    private void WorkspacePreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || IsResolving)
        {
            return;
        }

        CompleteCancellation();
        e.Handled = true;
    }

    internal void CompleteCancellation()
    {
        completion.TrySetResult(new UiXRoomWorkspaceResult(
            false,
            string.Empty,
            string.Empty,
            null,
            Editor.IsFollowGlobalSettings,
            Editor.IsToNotify,
            Editor.IsToMonitor,
            Editor.IsToRecord,
            Editor.GetRecordingOptions()));
    }

    private static string GetScheduleName(int index) => index switch
    {
        1 => "ScheduleWeekdays".Tr(),
        2 => "ScheduleWeekends".Tr(),
        3 => "ScheduleNight".Tr(),
        4 => "ScheduleCustom".Tr(),
        _ => "ScheduleAlways".Tr(),
    };

    private static MediaBrush FindBrush(string resourceKey)
    {
        return Application.Current?.TryFindResource(resourceKey) as MediaBrush ?? System.Windows.Media.Brushes.Gray;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                yield return typed;
            }
            foreach (T nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    public void Dispose()
    {
        StopLoadingAnimation();
        Editor.PropertyChanged -= EditorPropertyChanged;
        automaticResolutionTimer.Stop();
        automaticResolutionTimer.Tick -= AutomaticResolutionTimerTick;
        resolutionCancellation?.Cancel();
        resolutionCancellation?.Dispose();
        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
    }

    private sealed class BooleanVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is true ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is Visibility.Visible;
        }
    }
}
