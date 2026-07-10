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
        GlobalMonitor.Stop();
        GlobalMonitor.StopAllRecorders();
        RuntimeResourceLogger.Stop();
        ChildProcessTracerPeriodicTimer.Default.Stop(killChildren: true);
        AppSessionLogger.Stop();
        base.OnExit(e);
    }
}
