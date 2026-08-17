using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emerde.Controls;
using Emerde.Core;
using System.Windows.Threading;
using Windows.System;
using AppResources = Emerde.Properties.Resources;

namespace Emerde.Views;

[ObservableObject]
public partial class AboutContentDialog : System.Windows.Controls.UserControl
{
    private DispatcherOperation? navigationIndicatorUpdateOperation;
    private bool pendingNavigationIndicatorAnimation;

    [ObservableProperty]
    private bool isReleaseNotesSelected;

    [ObservableProperty]
    private ReleaseNoteEntry selectedReleaseNote;

    public AboutContentDialog()
    {
        ReleaseNotes = ReleaseNotesCatalog.Entries;
        selectedReleaseNote = ReleaseNotesCatalog.GetEntry(UpgradeNoticeService.GetCurrentVersion());
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) => QueueAboutNavigationIndicatorUpdate(false);
        AboutNavigationPanel.SizeChanged += AboutNavigationPanelSizeChanged;
    }

    public IReadOnlyList<ReleaseNoteEntry> ReleaseNotes { get; }

    public string OverviewNavigationLabel => GetText("AboutOverviewNavigation", "Overview");

    public string ReleaseNotesNavigationLabel => GetText("AboutReleaseNotesNavigation", "Release notes");

    public string ReleaseNotesDescription => GetText("AboutReleaseNotesDescription", "Review changes and improvements from recent versions.");

    private void AboutNavigationPanelSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (e.HeightChanged)
        {
            QueueAboutNavigationIndicatorUpdate(false);
        }
    }

    private void AboutNavigationButtonClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { CommandParameter: string parameter })
        {
            bool selectReleaseNotes = string.Equals(parameter, "ReleaseNotes", StringComparison.Ordinal);
            if (IsReleaseNotesSelected == selectReleaseNotes)
            {
                return;
            }

            System.Windows.FrameworkElement target = selectReleaseNotes
                ? AboutReleaseNotesScrollViewer
                : AboutOverviewScrollViewer;
            MotionAssist.PrepareEntrance(target);
            IsReleaseNotesSelected = selectReleaseNotes;
            MoveAboutNavigationIndicator((System.Windows.Controls.RadioButton)sender, true);
            Dispatcher.BeginInvoke(
                () => MotionAssist.PlayEntrance(target),
                DispatcherPriority.DataBind);
        }
    }

    private void QueueAboutNavigationIndicatorUpdate(bool animate)
    {
        pendingNavigationIndicatorAnimation |= animate;
        if (navigationIndicatorUpdateOperation is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        navigationIndicatorUpdateOperation = Dispatcher.BeginInvoke(() =>
        {
            bool shouldAnimate = pendingNavigationIndicatorAnimation;
            pendingNavigationIndicatorAnimation = false;
            navigationIndicatorUpdateOperation = null;
            if (!IsLoaded)
            {
                return;
            }
            System.Windows.Controls.RadioButton? selectedButton = AboutNavigationPanel.Children
                .OfType<System.Windows.Controls.RadioButton>()
                .FirstOrDefault(button => button.IsChecked == true);
            if (selectedButton != null)
            {
                MoveAboutNavigationIndicator(selectedButton, shouldAnimate);
            }
        }, DispatcherPriority.Render);
    }

    private void MoveAboutNavigationIndicator(System.Windows.Controls.RadioButton button, bool animate)
    {
        if (System.Windows.Window.GetWindow(this) is not MainWindow { ViewModel.StatusOfIsUiXEnabled: true }
            || !button.IsLoaded
            || button.ActualWidth <= 0d)
        {
            return;
        }

        System.Windows.Point position = button.TransformToAncestor(AboutNavigationRoot).Transform(new System.Windows.Point(0d, 0d));
        double targetX = WindowSizing.RoundLayoutValue(position.X + (button.ActualWidth - AboutNavigationSelectionIndicator.Width) / 2d);
        double targetY = WindowSizing.RoundLayoutValue(position.Y + button.ActualHeight - 5d);
        MotionAssist.MoveNavigationIndicator(AboutNavigationSelectionIndicator, targetX, targetY, animate);
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
