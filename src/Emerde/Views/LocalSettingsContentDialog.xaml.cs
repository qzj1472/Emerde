using CommunityToolkit.Mvvm.ComponentModel;
using Emerde.ViewModels;
using Wpf.Ui.Violeta.Controls;

namespace Emerde.Views;

[ObservableObject]
public sealed partial class LocalSettingsContentDialog : ContentDialog
{
    [ObservableProperty]
    private string nickName = string.Empty;

    [ObservableProperty]
    private string roomUrl = string.Empty;

    [ObservableProperty]
    private bool isFollowGlobalSettings = true;

    [ObservableProperty]
    private bool isToNotify = true;

    [ObservableProperty]
    private bool isToMonitor = true;

    [ObservableProperty]
    private bool isToRecord = true;

    public bool IsSaved { get; private set; }

    public LocalSettingsContentDialog(RoomStatusReactive room)
    {
        NickName = room.NickName;
        RoomUrl = room.RoomUrl;
        IsFollowGlobalSettings = room.IsFollowGlobalSettings;
        IsToNotify = room.IsToNotify;
        IsToMonitor = room.IsToMonitor;
        IsToRecord = room.IsToRecord;
        DataContext = this;
        InitializeComponent();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        IsSaved = true;
    }
}
