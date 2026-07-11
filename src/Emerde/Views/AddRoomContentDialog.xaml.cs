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

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            Toast.Warning("EnterRoomUrl".Tr());
            e.Cancel = true;
            return;
        }

        if (IsForcedAdd)
        {
            string? roomUrl = Spider.ParseUrl(Url);

            if (roomUrl != null)
            {
                if (Configurations.Rooms.Get().Any(room => room.RoomUrl == roomUrl))
                {
                    e.Cancel = true;
                    Toast.Warning("AddRoomErrorDuplicated".Tr(roomUrl));
                    return;
                }

                NickName = roomUrl;
                RoomUrl = roomUrl;

                Toast.Success("AddRoomSucc".Tr(RoomUrl));
            }
            else
            {
                e.Cancel = true;
                Toast.Error("ErrorRoomUrl".Tr());
            }
        }
        else
        {
            using (LoadingWindow.ShowAsync())
            {
                try
                {
                    ISpiderResult? spider = Spider.GetResult(Url, RoomRecordingSettings.GetGlobal().PreferredStreamQuality);

                    if (string.IsNullOrWhiteSpace(spider?.Nickname))
                    {
                        e.Cancel = true;
                        Toast.Error(GetRoomInfoErrorMessage(Url));
                        return;
                    }

                    if (Configurations.Rooms.Get().Any(room => room.RoomUrl == spider.RoomUrl))
                    {
                        e.Cancel = true;
                        Toast.Warning("AddRoomErrorDuplicated".Tr(spider.Nickname));
                        return;
                    }

                    NickName = spider.Nickname;
                    RoomUrl = spider.RoomUrl;
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
    }

    internal static string GetRoomInfoErrorMessage(string? roomUrl, string? fallback = null)
    {
        string error = string.IsNullOrWhiteSpace(roomUrl) ? string.Empty : StreamResolver.GetLastError(roomUrl);
        string detail = string.IsNullOrWhiteSpace(error) ? fallback ?? string.Empty : error;
        string message = "GetRoomInfoError".Tr();
        return string.IsNullOrWhiteSpace(detail) ? message : $"{message}: {detail}";
    }
}
