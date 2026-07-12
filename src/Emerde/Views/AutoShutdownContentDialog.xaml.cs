using Emerde.Core;
using Emerde.ViewModels;
using System.Windows;

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
