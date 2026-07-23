using Fischless.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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
    private static string? recoveredConfigurationPath;

    private static Exception? configurationRecoveryException;

    private static Exception? cultureRecoveryException;

    static App()
    {
        SystemMenuThemeManager.Apply();
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        _ = DpiAware.SetProcessDpiAwareness();
        ConfigurationManager.ConfigurationSerializer = new YamlConfigurationSerializer();
        ConfigurationMigrationHelper.MigrateLegacyConfiguration();
        string configurationPath = ConfigurationSpecialPath.GetPath("config.yaml", AppConfig.PackName);
        try
        {
            ConfigurationManager.Setup(configurationPath);
        }
        catch (Exception e) when (IsInvalidConfiguration(configurationPath))
        {
            configurationRecoveryException = e;
            recoveredConfigurationPath = PreserveInvalidConfiguration(configurationPath);
            ConfigurationManager.Setup(configurationPath);
        }

        string language = Configurations.Language.Get();
        try
        {
            Locale.Culture = string.IsNullOrWhiteSpace(language) ? CultureInfo.CurrentUICulture : new CultureInfo(language);
        }
        catch (CultureNotFoundException e)
        {
            cultureRecoveryException = e;
            Locale.Culture = CultureInfo.CurrentUICulture;
            Configurations.Language.Set(string.Empty);
            ConfigurationSaveScheduler.SaveNow();
        }
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

    private static bool IsInvalidConfiguration(string configurationPath)
    {
        if (!File.Exists(configurationPath))
        {
            return false;
        }

        try
        {
            ConfigFileManager.Validate(configurationPath);
            return false;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Occurs when the application is loading.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RuntimeHelper.WaitForRestartParent(e.Args);
        RuntimeHelper.CheckSingleInstance(AppConfig.PackName + (Debugger.IsAttached ? "_DEBUG" : string.Empty));
        try
        {
            AppSessionLogger.Start();
        }
        catch (Exception loggingException)
        {
            Debug.WriteLine(loggingException);
        }
        try
        {
            SecretProtector.MigrateStoredSecrets();
        }
        catch (Exception secretMigrationException) when (secretMigrationException is CryptographicException or IOException or UnauthorizedAccessException)
        {
            AppSessionLogger.WriteException(secretMigrationException);
        }
        if (cultureRecoveryException != null)
        {
            AppSessionLogger.WriteException(cultureRecoveryException);
        }
        if (configurationRecoveryException != null)
        {
            AppSessionLogger.WriteException(configurationRecoveryException);
            AppSessionLogger.Event("warn", "configuration", "startup_recovered", "invalid startup configuration was preserved and defaults were loaded", new { recoveredConfigurationPath });
            _ = MessageBox.Warning("ConfigurationRecovered".Tr(recoveredConfigurationPath ?? string.Empty));
        }
        if ((Configurations.Rooms.Get() ?? []).Any(room =>
                room != null
                && string.Equals(Spider.GetPlatformName(room.RoomUrl), "Douyin", StringComparison.OrdinalIgnoreCase))
            && !DouyinWebViewResolver.IsRuntimeAvailable())
        {
            _ = MessageBox.Warning("WebView2RuntimeMissing".Tr());
        }
        RuntimeResourceLogger.Start();
        TrayIconManager.Start();
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _ = ConfigurationSaveScheduler.TrySaveNow();
        TrayIconManager.Stop();
        GlobalMonitor.Stop();
        GlobalMonitor.StopAllRecorders(deferPostProcessing: true);
        MediaOperationRegistry.CancelAll();
        Task.WhenAll(
            GlobalMonitor.WaitForRecordersAsync(TimeSpan.FromSeconds(1)),
            MediaOperationRegistry.WaitForCompletionAsync(TimeSpan.FromSeconds(1))).GetAwaiter().GetResult();
        ChildProcessTracerPeriodicTimer.Default.Stop(killChildren: true);
        RuntimeResourceLogger.Stop();
        DouyinWebViewResolver.Shutdown();
        AppSessionLogger.Stop();
        base.OnExit(e);
    }

    private static string? PreserveInvalidConfiguration(string configurationPath)
    {
        try
        {
            if (!File.Exists(configurationPath))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(configurationPath) ?? AppContext.BaseDirectory;
            string fileName = Path.GetFileNameWithoutExtension(configurationPath);
            string extension = Path.GetExtension(configurationPath);
            string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string backupPath = Path.Combine(directory, $"{fileName}.invalid-{timestamp}{extension}");
            for (int index = 2; File.Exists(backupPath); index++)
            {
                backupPath = Path.Combine(directory, $"{fileName}.invalid-{timestamp}-{index}{extension}");
            }
            File.Move(configurationPath, backupPath, false);
            return backupPath;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
            return null;
        }
    }
}
