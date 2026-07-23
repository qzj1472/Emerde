using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;
using Windows.System;

namespace Emerde.Views;

[ObservableObject]
public partial class AboutContentDialog : System.Windows.Controls.UserControl
{
    [ObservableProperty]
    private double aboutCardWidth = 500;

    [ObservableProperty]
    private double workflowCardWidth = 250;

    public AboutContentDialog()
    {
        DataContext = this;
        InitializeComponent();
    }

    private void AboutContentDialogSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        (AboutCardWidth, WorkflowCardWidth) = CalculateCardWidths(e.NewSize.Width);
    }

    internal static (double AboutCardWidth, double WorkflowCardWidth) CalculateCardWidths(double controlWidth)
    {
        double availableWidth = Math.Max(0, Math.Min(1120, controlWidth - 54));
        int cardColumns = availableWidth >= 760 ? 2 : 1;
        int workflowColumns = availableWidth >= 960 ? 4 : availableWidth >= 560 ? 2 : 1;
        double cardWidth = Math.Max(0, Math.Floor(availableWidth / cardColumns - 12));
        double workflowWidth = Math.Max(0, Math.Floor(availableWidth / workflowColumns - 12));
        return (cardWidth, workflowWidth);
    }

    [RelayCommand]
    private async Task OpenHyperlink(string? url)
    {
        _ = await Launcher.LaunchUriAsync(new Uri(string.IsNullOrWhiteSpace(url) ? AppConfig.Url : url));
    }
}
