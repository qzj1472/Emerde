using CommunityToolkit.Mvvm.ComponentModel;
using Emerde.Core;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.Views;

[ObservableObject]
public sealed partial class AddRoomContentDialog : ContentDialog
{
    [ObservableProperty]
    private string? url = null;

    partial void OnUrlChanged(string? value)
    {
        string platformName = string.IsNullOrWhiteSpace(value) ? string.Empty : Spider.GetPlatformName(value);
        DetectedPlatformName = string.IsNullOrWhiteSpace(platformName) ? "Unsupported" : PlatformDisplayName.Get(platformName);
    }

    [ObservableProperty]
    private bool isForcedAdd = false;

    [ObservableProperty]
    private string? nickName = null;

    [ObservableProperty]
    private string detectedPlatformName = "Unsupported";

    public string SupportedPlatformsText => string.Join(" / ", Spider.SupportedPlatformNames.Select(PlatformDisplayName.Get));

    public string? RoomUrl = null;

    public ISpiderResult? SpiderResult { get; private set; }

    public AddRoomContentDialog()
    {
        DataContext = this;
        InitializeComponent();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        var deferral = e.GetDeferral();
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
                string? normalizedRoomUrl = await Task.Run(() => Spider.ParseUrl(inputUrl, allowNetwork: !IsForcedAdd));
                if (string.IsNullOrWhiteSpace(normalizedRoomUrl))
                {
                    e.Cancel = true;
                    Toast.Error("ErrorRoomUrl".Tr());
                    return;
                }

                if (Configurations.Rooms.Get().Any(room => string.Equals(room.RoomUrl, normalizedRoomUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    e.Cancel = true;
                    Toast.Warning("AddRoomErrorDuplicated".Tr(normalizedRoomUrl));
                    return;
                }

                if (IsForcedAdd)
                {
                    NickName = normalizedRoomUrl;
                    RoomUrl = normalizedRoomUrl;

                    Toast.Success("AddRoomSucc".Tr(RoomUrl));
                    return;
                }

                try
                {
                    string preferredQuality = RoomRecordingSettings.GetGlobal().PreferredStreamQuality;
                    ISpiderResult? spider = await Task.Run(() => Spider.GetResult(normalizedRoomUrl, preferredQuality, bypassDouyinThrottle: true));
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

                    if (spider == null || !HasAddableRoomInfo(spider, roomUrl))
                    {
                        e.Cancel = true;
                        Toast.Error(GetRoomInfoErrorMessage(normalizedRoomUrl));
                        return;
                    }

                    if (Configurations.Rooms.Get().Any(room => string.Equals(room.RoomUrl, roomUrl, StringComparison.OrdinalIgnoreCase)))
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
                catch (Exception exception)
                {
                    e.Cancel = true;
                    Toast.Error(GetRoomInfoErrorMessage(Url, exception.Message));
                }
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    internal static string GetRoomInfoErrorMessage(string? roomUrl, string? fallback = null)
    {
        string error = string.IsNullOrWhiteSpace(roomUrl) ? string.Empty : ExternalStreamResolver.GetLastError(roomUrl);
        if (string.IsNullOrWhiteSpace(error) && !string.IsNullOrWhiteSpace(roomUrl))
        {
            error = StreamResolver.GetLastError(roomUrl);
        }

        string detail = string.IsNullOrWhiteSpace(error) ? fallback ?? string.Empty : error;
        string message = "GetRoomInfoError".Tr();
        return string.IsNullOrWhiteSpace(detail) ? message : $"{message}: {detail}";
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
            && string.Equals(Spider.GetPlatformName(roomUrl), "Douyin", StringComparison.OrdinalIgnoreCase)
            && StreamResolver.IsTransientDouyinFailure(error);
    }

    internal static string GetConfirmedNickName(ISpiderResult spider)
    {
        return string.IsNullOrWhiteSpace(spider.Nickname) ? spider.RoomUrl ?? string.Empty : spider.Nickname;
    }
}
