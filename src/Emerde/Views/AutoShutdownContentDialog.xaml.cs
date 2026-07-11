using Emerde.Core;
using Emerde.ViewModels;
using System.Windows;

namespace Emerde.Views;

public partial class AutoShutdownContentDialog : ContentDialog
{
    private readonly MainViewModel viewModel;

    public string Description => AutoShutdownSchedule.ResolveCloseTarget(Configurations.IsAutoShutdownComputer.Get()) == ScheduledCloseTarget.Computer
        ? "录制已经停止。到达设定时间时会关闭电脑。需要等待全部转码完成时  可以选择转码完关闭  或在设置中长期启用该选项。"
        : "录制已经停止。到达设定时间时只会退出 Emerde  不会关闭电脑。需要等待全部转码完成时  可以选择转码完关闭  或在设置中长期启用该选项。";

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
