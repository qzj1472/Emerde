using System.Windows;

namespace Emerde.Uninstaller;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!Program.Initialize(e.Args))
        {
            Shutdown();
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Program.ScheduleDeferredCleanup();
        base.OnExit(e);
    }
}
