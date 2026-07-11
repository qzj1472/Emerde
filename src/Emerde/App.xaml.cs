using Fischless.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Violeta.Appearance;
using Wpf.Ui.Violeta.Controls;
using Wpf.Ui.Violeta.Win32;

namespace Emerde;

public partial class App : Application
{
    static App()
    {
        SystemMenuThemeManager.Apply();
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        _ = DpiAware.SetProcessDpiAwareness();
        ConfigurationManager.ConfigurationSerializer = new YamlConfigurationSerializer();
        ConfigurationMigrationHelper.MigrateLegacyConfiguration();
        ConfigurationManager.Setup(ConfigurationSpecialPath.GetPath("config.yaml", AppConfig.PackName));
        Locale.Culture = string.IsNullOrWhiteSpace(Configurations.Language.Get()) ? CultureInfo.CurrentUICulture : new CultureInfo(Configurations.Language.Get());
    }

    public App()
    {
        InitializeComponent();

        DispatcherUnhandledException += (object s, DispatcherUnhandledExceptionEventArgs e) =>
        {
            e.Handled = true;
            AppSessionLogger.WriteException(e.Exception);
            ExceptionReport.Show(e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                AppSessionLogger.Event("fatal", "exception", exception.GetType().Name, exception.Message, new
                {
                    type = exception.GetType().FullName,
                    exception.Message,
                    stackTrace = exception.ToString(),
                    e.IsTerminating,
                });
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppSessionLogger.Event("error", "exception", "unobserved_task_exception", e.Exception.Message, new
            {
                type = e.Exception.GetType().FullName,
                e.Exception.Message,
                stackTrace = e.Exception.ToString(),
            });
            e.SetObserved();
        };

        if (Enum.TryParse(Configurations.Theme.Get(), out ApplicationTheme applicationTheme))
        {
            ThemeManager.Apply(applicationTheme);
        }
        else
        {
            ThemeManager.Apply(ApplicationTheme.Unknown);
        }

        AppThemeBrushes.Apply();
    }

    /// <summary>
    /// Occurs when the application is loading.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RuntimeHelper.CheckSingleInstance(AppConfig.PackName + (Debugger.IsAttached ? "_DEBUG" : string.Empty));
        AppSessionLogger.Start();
        RuntimeResourceLogger.Start();
        TrayIconManager.Start();
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        TrayIconManager.Stop();
        GlobalMonitor.Stop();
        GlobalMonitor.StopAllRecorders();
        RuntimeResourceLogger.Stop();
        ChildProcessTracerPeriodicTimer.Default.Stop(killChildren: true);
        AppSessionLogger.Stop();
        base.OnExit(e);
    }
}
