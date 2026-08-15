using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emerde.Core;
using Windows.System;
using AppResources = Emerde.Properties.Resources;

namespace Emerde.Views;

[ObservableObject]
public partial class AboutContentDialog : System.Windows.Controls.UserControl
{
    [ObservableProperty]
    private bool isReleaseNotesSelected;

    [ObservableProperty]
    private ReleaseNoteEntry selectedReleaseNote;

    [ObservableProperty]
    private double aboutCardWidth = 500;

    [ObservableProperty]
    private double workflowCardWidth = 250;

    public AboutContentDialog()
    {
        ReleaseNotes = ReleaseNotesCatalog.Entries;
        selectedReleaseNote = ReleaseNotesCatalog.GetEntry(UpgradeNoticeService.GetCurrentVersion());
        DataContext = this;
        InitializeComponent();
    }

    public IReadOnlyList<ReleaseNoteEntry> ReleaseNotes { get; }

    public string OverviewNavigationLabel => GetText("AboutOverviewNavigation", "Overview");

    public string ReleaseNotesNavigationLabel => GetText("AboutReleaseNotesNavigation", "Release notes");

    public string ReleaseNotesDescription => GetText("AboutReleaseNotesDescription", "Review changes and improvements from recent versions.");

    private void AboutOverviewScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (e.ViewportWidth > 0)
        {
            (AboutCardWidth, WorkflowCardWidth) = CalculateCardWidths(e.ViewportWidth);
        }
    }

    internal static (double AboutCardWidth, double WorkflowCardWidth) CalculateCardWidths(double viewportWidth)
    {
        double availableWidth = Math.Max(0, viewportWidth - 40);
        int cardColumns = availableWidth >= 760 ? 2 : 1;
        int workflowColumns = availableWidth >= 960 ? 4 : availableWidth >= 560 ? 2 : 1;
        double cardWidth = Math.Max(0, Math.Floor((availableWidth - 12 * (cardColumns - 1)) / cardColumns));
        double workflowWidth = Math.Max(0, Math.Floor((availableWidth - 12 * (workflowColumns - 1)) / workflowColumns));
        return (cardWidth, workflowWidth);
    }

    private void AboutNavigationButtonClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { CommandParameter: string parameter })
        {
            IsReleaseNotesSelected = string.Equals(parameter, "ReleaseNotes", StringComparison.Ordinal);
        }
    }

    private static string GetText(string key, string fallback)
    {
        return AppResources.ResourceManager.GetString(key, AppResources.Culture) ?? fallback;
    }

    [RelayCommand]
    private async Task OpenHyperlink(string? url)
    {
        _ = await Launcher.LaunchUriAsync(new Uri(string.IsNullOrWhiteSpace(url) ? AppConfig.Url : url));
    }
}
