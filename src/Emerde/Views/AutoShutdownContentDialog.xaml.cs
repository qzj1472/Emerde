using Emerde.Core;
using Emerde.ViewModels;
using System.Windows;
using WpfButton = System.Windows.Controls.Button;
using WpfColumnDefinition = System.Windows.Controls.ColumnDefinition;
using WpfGrid = System.Windows.Controls.Grid;
using WpfStackPanel = System.Windows.Controls.StackPanel;

namespace Emerde.Views;

public partial class AutoShutdownContentDialog : ContentDialog
{
    private readonly MainViewModel viewModel;

    public string Description => AutoShutdownSchedule.ResolveCloseTarget(Configurations.IsAutoShutdownComputer.Get()) == ScheduledCloseTarget.Computer
        ? "AutoShutdownComputerDescription".Tr()
        : "AutoShutdownApplicationDescription".Tr();

    public AutoShutdownContentDialog(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        if (UiXDialogContent.IsEnabled)
        {
            Content = CreateUiXContent();
        }
    }

    private FrameworkElement CreateUiXContent()
    {
        WpfStackPanel content = new()
        {
            MinWidth = 500d,
            Margin = new Thickness(0, 4, 0, 2),
        };
        content.Children.Add(UiXDialogContent.CreateMessage(
            Description,
            Wpf.Ui.Controls.FontSymbols.PowerButton,
            UiXDialogTone.Warning,
            minimumWidth: 0d));

        WpfGrid actions = new()
        {
            Margin = new Thickness(0, 18, 0, 0),
        };
        actions.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new GridLength(10d) });
        actions.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        WpfButton shutdownNow = new()
        {
            Height = 42d,
            Content = "AutoShutdownNow".Tr(),
        };
        shutdownNow.Click += ShutdownNowClick;
        actions.Children.Add(shutdownNow);
        WpfButton shutdownAfterTranscode = new()
        {
            Height = 42d,
            Content = "AutoShutdownAfterTranscodeNow".Tr(),
        };
        shutdownAfterTranscode.Click += ShutdownAfterTranscodeClick;
        WpfGrid.SetColumn(shutdownAfterTranscode, 2);
        actions.Children.Add(shutdownAfterTranscode);
        content.Children.Add(actions);

        WpfGrid secondaryActions = new()
        {
            Margin = new Thickness(0, 10, 0, 0),
        };
        secondaryActions.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        secondaryActions.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new GridLength(10d) });
        secondaryActions.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        WpfButton cancelSchedule = new()
        {
            Height = 38d,
            Content = "AutoShutdownCancel".Tr(),
        };
        cancelSchedule.Click += CancelClick;
        secondaryActions.Children.Add(cancelSchedule);
        WpfButton acknowledge = new()
        {
            Height = 38d,
            Content = "ButtonOfAcknowledge".Tr(),
        };
        acknowledge.Click += AcknowledgeClick;
        WpfGrid.SetColumn(acknowledge, 2);
        secondaryActions.Children.Add(acknowledge);
        content.Children.Add(secondaryActions);
        return content;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        viewModel.CancelAutoShutdownFromPrompt();
        Hide();
    }

    private void ShutdownNowClick(object sender, RoutedEventArgs e)
    {
        Hide();
        viewModel.ShutdownNowFromPrompt();
    }

    private void ShutdownAfterTranscodeClick(object sender, RoutedEventArgs e)
    {
        Hide();
        viewModel.ShutdownAfterTranscodeFromPrompt();
    }

    private void AcknowledgeClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
