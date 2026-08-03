using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        double availableWidth = Math.Max(0, controlWidth - 46);
        int cardColumns = availableWidth >= 760 ? 2 : 1;
        int workflowColumns = availableWidth >= 960 ? 4 : availableWidth >= 560 ? 2 : 1;
        double cardWidth = Math.Max(0, Core.WindowSizing.RoundLayoutValue((availableWidth - 12 * (cardColumns - 1)) / cardColumns));
        double workflowWidth = Math.Max(0, Core.WindowSizing.RoundLayoutValue((availableWidth - 12 * (workflowColumns - 1)) / workflowColumns));
        return (cardWidth, workflowWidth);
    }

    [RelayCommand]
    private async Task OpenHyperlink(string? url)
    {
        _ = await Launcher.LaunchUriAsync(new Uri(string.IsNullOrWhiteSpace(url) ? AppConfig.Url : url));
    }
}
