using System.Windows;
using System.Windows.Threading;

namespace Emerde.Installer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        MainWindow mainWindow = new();
        MainWindow = mainWindow;
        mainWindow.Show();
        _ = mainWindow.Dispatcher.BeginInvoke(
            mainWindow.BringToForegroundOnce,
            DispatcherPriority.ApplicationIdle);
    }
}
