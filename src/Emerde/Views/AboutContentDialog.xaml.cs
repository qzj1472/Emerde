using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;
using Windows.System;

namespace Emerde.Views;

[ObservableObject]
public partial class AboutContentDialog : System.Windows.Controls.UserControl
{
    public AboutContentDialog()
    {
        DataContext = this;
        InitializeComponent();
    }

    [RelayCommand]
    private async Task OpenHyperlink(string? url)
    {
        _ = await Launcher.LaunchUriAsync(new Uri(string.IsNullOrWhiteSpace(url) ? AppConfig.Url : url));
    }
}
