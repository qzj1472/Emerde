using CommunityToolkit.Mvvm.ComponentModel;
using Emerde.Core;
using Emerde.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.Views;

[ObservableObject]
public sealed partial class AddRoomContentDialog : ContentDialog
{
    private const double ExpandedDialogHeightRatio = 0.95d;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool isSubmitting;

    [ObservableProperty]
    private string? url = null;

    partial void OnUrlChanged(string? value)
    {
        string platformName = string.IsNullOrWhiteSpace(value) ? string.Empty : Spider.GetPlatformName(value);
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
    private bool isFollowGlobalSettings = true;

    partial void OnIsFollowGlobalSettingsChanged(bool value)
    {
        UpdateDialogSize();
    }

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
        Unloaded += (_, _) => lifetimeCancellation.Cancel();
    }

    private void AddRoomContentDialogLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                RoomUrlTextBox.Focus();
                Keyboard.Focus(RoomUrlTextBox);
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

        if (IsFollowGlobalSettings)
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
            AddRoomSurface.MinWidth = 420d;
            AddRoomSurface.MinHeight = 0d;
            AddRoomSurface.MaxWidth = double.PositiveInfinity;
            AddRoomSurface.MaxHeight = double.PositiveInfinity;
            WindowSizing.ApplyContentDialogSizeLimit(this, Application.Current?.MainWindow);
            return;
        }

        if (!LocalSettingsContentDialog.TryGetDialogVisualSize(
                Application.Current?.MainWindow,
                ExpandedDialogHeightRatio,
                out double targetWidth,
                out double targetHeight))
        {
            return;
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
        string brushKey = RoomUrlTextBox.IsKeyboardFocusWithin
            ? "SystemAccentColorPrimaryBrush"
            : "ControlStrokeColorDefaultBrush";
        RoomUrlInputBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, brushKey);
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        var deferral = e.GetDeferral();
        if (isSubmitting)
        {
            e.Cancel = true;
            deferral.Complete();
            return;
        }

        isSubmitting = true;
        IsPrimaryButtonEnabled = false;
        CancellationToken token = lifetimeCancellation.Token;
        try
        {
            if (string.IsNullOrWhiteSpace(Url))
            {
                Toast.Warning("EnterRoomUrl".Tr());
                e.Cancel = true;
                return;
            }

            string inputUrl = Url;
            using (LoadingWindow.ShowAsync())
            {
                string? normalizedRoomUrl = await Task.Run(() => Spider.ParseUrl(inputUrl, allowNetwork: !IsForcedAdd, token), token);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(normalizedRoomUrl))
                {
                    e.Cancel = true;
                    Toast.Error("ErrorRoomUrl".Tr());
                    return;
                }

                if (HasDuplicateRoom(normalizedRoomUrl))
                {
                    e.Cancel = true;
                    Toast.Warning("AddRoomErrorDuplicated".Tr(normalizedRoomUrl));
                    return;
                }

                if (IsForcedAdd)
                {
                    if (!ExternalStreamResolver.IsPersistableRoomUrl(normalizedRoomUrl))
                    {
                        e.Cancel = true;
                        Toast.Error("ErrorRoomUrl".Tr());
                        return;
                    }

                    NickName = normalizedRoomUrl;
                    RoomUrl = normalizedRoomUrl;

                    Toast.Success("AddRoomSucc".Tr(RoomUrl));
                    return;
                }

                try
                {
                    string preferredQuality = RoomRecordingSettings.GetGlobal().PreferredStreamQuality;
                    ISpiderResult? spider = await GlobalMonitor.GetManualSpiderResultAsync(normalizedRoomUrl, preferredQuality);
                    token.ThrowIfCancellationRequested();
                    string roomUrl = string.IsNullOrWhiteSpace(spider?.RoomUrl)
                        ? normalizedRoomUrl
                        : Spider.ParseUrl(spider.RoomUrl!) ?? spider.RoomUrl!;

                    if (spider == null && CanDeferRoomInfoResolution(normalizedRoomUrl, ExternalStreamResolver.GetLastError(normalizedRoomUrl)))
                    {
                        NickName = normalizedRoomUrl;
                        RoomUrl = normalizedRoomUrl;
                        Toast.Warning("AddRoomSucc".Tr(RoomUrl));
                        return;
                    }

                    if (spider == null
                        || !HasAddableRoomInfo(spider, roomUrl)
                        || !ExternalStreamResolver.IsPersistableRoomUrl(roomUrl))
                    {
                        e.Cancel = true;
                        Toast.Error(GetRoomInfoErrorMessage(normalizedRoomUrl));
                        return;
                    }

                    if (HasDuplicateRoom(roomUrl, spider.PlatformName, spider.Uid))
                    {
                        e.Cancel = true;
                        Toast.Warning("AddRoomErrorDuplicated".Tr(GetConfirmedNickName(spider)));
                        return;
                    }

                    NickName = GetConfirmedNickName(spider);
                    RoomUrl = roomUrl;
                    spider.RoomUrl = roomUrl;
                    SpiderResult = spider;

                    Toast.Success("AddRoomSucc".Tr(NickName));
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
            isSubmitting = false;
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

    internal static bool HasAddableRoomInfo(ISpiderResult? spider, string? roomUrl)
    {
        if (spider == null || string.IsNullOrWhiteSpace(roomUrl))
        {
            return false;
        }

        string platformName = string.IsNullOrWhiteSpace(spider.PlatformName)
            ? Spider.GetPlatformName(roomUrl)
            : spider.PlatformName;
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(spider.Nickname)
            || !string.IsNullOrWhiteSpace(spider.Uid)
            || spider.IsLiveStreaming == true
            || !string.IsNullOrWhiteSpace(spider.FlvUrl)
            || !string.IsNullOrWhiteSpace(spider.HlsUrl)
            || !string.IsNullOrWhiteSpace(spider.RecordUrl);
    }

    internal static bool CanDeferRoomInfoResolution(string? roomUrl, string? error)
    {
        return !string.IsNullOrWhiteSpace(roomUrl)
            && ExternalStreamResolver.IsPersistableRoomUrl(roomUrl)
            && string.Equals(Spider.GetPlatformName(roomUrl), "Douyin", StringComparison.OrdinalIgnoreCase)
            && StreamResolver.IsTransientDouyinFailure(error);
    }

    internal static string GetConfirmedNickName(ISpiderResult spider)
    {
        return string.IsNullOrWhiteSpace(spider.Nickname) ? spider.RoomUrl ?? string.Empty : spider.Nickname;
    }

    private static bool HasDuplicateRoom(string roomUrl, string? platformName = null, string? uid = null)
    {
        return (Configurations.Rooms.Get() ?? []).Any(room => ExternalStreamResolver.IsSameRoom(
            room.RoomUrl,
            room.PlatformName,
            room.Uid,
            roomUrl,
            platformName,
            uid));
    }
}
