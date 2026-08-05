using System.Windows;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Emerde.Uninstaller;

public partial class MainWindow : FluentWindow
{
    private bool operationCompleted;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindowLoaded;
    }

    private void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (Program.IsEmerdeRunning())
        {
            RunningDialogLayer.Visibility = Visibility.Visible;
        }
    }

    private void CancelButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UninstallButtonClick(object sender, RoutedEventArgs e)
    {
        if (operationCompleted)
        {
            Close();
            return;
        }

        if (Program.IsEmerdeRunning())
        {
            RunningDialogLayer.Visibility = Visibility.Visible;
            return;
        }

        UninstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        KeepUserDataCheckBox.IsEnabled = false;

        if (Program.Uninstall(KeepUserDataCheckBox.IsChecked == true))
        {
            operationCompleted = true;
            TitleText.Text = "Emerde 已卸载";
            DescriptionText.Text = KeepUserDataCheckBox.IsChecked == true
                ? "程序文件已移除，用户数据已保留。"
                : "程序文件和用户数据已移除。";
            UninstallButton.Content = "完成";
            UninstallButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        ErrorText.Text = Program.LastError?.Message ?? "卸载失败，请重试。";
        ErrorText.Visibility = Visibility.Visible;
        UninstallButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
        KeepUserDataCheckBox.IsEnabled = true;
    }

    private void CancelStopButtonClick(object sender, RoutedEventArgs e)
    {
        RunningDialogLayer.Visibility = Visibility.Collapsed;
    }

    private void StopAndContinueButtonClick(object sender, RoutedEventArgs e)
    {
        if (!Program.StopRunningApplication())
        {
            return;
        }

        RunningDialogLayer.Visibility = Visibility.Collapsed;
    }
}
