using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Emerde.Core;
using Emerde.Extensions;
using Emerde.Models;
using Emerde.Threading;
using Emerde.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Violeta.Appearance;
using Wpf.Ui.Violeta.Resources;
using Wpf.Ui.Violeta.Win32;

namespace Emerde;

internal sealed class TrayIconManager : IDisposable
{
    private static TrayIconManager? _instance;

    private readonly TrayIconHost _icon = null!;

    private Icon? currentIcon;

    private TrayMenuWindow? trayMenuWindow;

    private bool isDisposed;

    private int shutdownInProgress;

    public bool IsShutdownTriggered { get; private set; } = false;

    private TrayIconManager()
    {
        _icon = new EmerdeTrayIconHost(ShowTrayMenu)
        {
            ToolTipText = "Emerde",
        };
        UpdateTrayIcon();

        _icon.LeftDoubleClick += (_, _) =>
        {
            if (Application.Current.MainWindow is not MainWindow mainWindow)
            {
                return;
            }

            if (mainWindow.IsVisible)
            {
                mainWindow.PrepareForTrayHide();
                mainWindow.Hide();
            }
            else
            {
                ActivateMainWindow();
            }
        };

        Locale.CultureChanged += OnCultureChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static TrayIconManager GetInstance()
    {
        return _instance ??= new TrayIconManager();
    }

    public static void Start()
    {
        _ = GetInstance();
    }

    public static void Stop()
    {
        _instance?.Dispose();
        _instance = null;
    }

    public async void ShutdownApplication()
    {
        await ShutdownApplicationAsync(confirmRecording: true);
    }

    public async void ShutdownApplication(bool confirmRecording)
    {
        await ShutdownApplicationAsync(confirmRecording);
    }

    private async Task ShutdownApplicationAsync(bool confirmRecording)
    {
        if (confirmRecording && !ConfirmRecordingInterruption())
        {
            return;
        }

        if (Interlocked.CompareExchange(ref shutdownInProgress, 1, 0) != 0)
        {
            return;
        }

        IsShutdownTriggered = true;
        Dispose();
        Application.Current.MainWindow?.Hide();
        try
        {
            GlobalMonitor.Stop();
            GlobalMonitor.StopAllRecorders(deferPostProcessing: true);
            MediaOperationRegistry.CancelAll();
            await Task.WhenAll(
                GlobalMonitor.WaitForRecordersAsync(TimeSpan.FromSeconds(5)),
                MediaOperationRegistry.WaitForCompletionAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            CompleteApplicationShutdown(Application.Current.Shutdown, () => Environment.Exit(0));
        }
    }

    internal static void CompleteApplicationShutdown(Action shutdown, Action exit)
    {
        try
        {
            shutdown();
        }
        finally
        {
            exit();
        }
    }

    public async Task RestartApplicationAsync(bool confirmRecording = true)
    {
        if (confirmRecording && !ConfirmRecordingInterruption())
        {
            return;
        }

        if (Interlocked.CompareExchange(ref shutdownInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Application.Current.MainWindow?.Hide();
            GlobalMonitor.Stop();
            GlobalMonitor.StopAllRecorders(deferPostProcessing: true);
            MediaOperationRegistry.CancelAll();
            await Task.WhenAll(
                GlobalMonitor.WaitForRecordersAsync(TimeSpan.FromSeconds(5)),
                MediaOperationRegistry.WaitForCompletionAsync(TimeSpan.FromSeconds(5)));

            bool restarted = RuntimeHelper.Restart(forced: true, beforeExit: () =>
            {
                IsShutdownTriggered = true;
                Dispose();
                _ = ConfigurationSaveScheduler.TrySaveNow();
                ChildProcessTracerPeriodicTimer.Default.Stop(killChildren: true);
                RuntimeResourceLogger.Stop();
                AppSessionLogger.Stop();
            });
            if (!restarted)
            {
                ActivateMainWindow();
            }
        }
        finally
        {
            Interlocked.Exchange(ref shutdownInProgress, 0);
        }
    }

    public void UpdateTrayIcon()
    {
        if (isDisposed)
        {
            return;
        }

        Icon icon = GetTrayIcon();
        _icon.Icon = icon.Handle;
        currentIcon?.Dispose();
        currentIcon = icon;

        static Icon GetTrayIcon()
        {
            try
            {
                if (Configurations.IsUseStatusTray.Get())
                {
                    string status = GlobalMonitor.RoomStatus.Values.ToArray().Any(roomStatus => roomStatus.RecordStatus == RecordStatus.Recording) ? "Recording" : "Unrecording";
                    string theme = GetTraySystemTheme() switch
                    {
                        SystemTheme.Dark or SystemTheme.HCBlack or SystemTheme.Glow or SystemTheme.CapturedMotion => "Dark",
                        _ => "Light",
                    };

                    return new Icon(ResourcesProvider.GetStream($"pack://application:,,,/Emerde;component/Assets/{status}{theme}.ico"));
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone();
        }
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        trayMenuWindow?.Close();
        _icon.ToolTipText = "Emerde";
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(ApplyUserPreferenceChange);
            return;
        }

        ApplyUserPreferenceChange();
    }

    private void ApplyUserPreferenceChange()
    {
        if (isDisposed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Configurations.Theme.Get()))
        {
            ThemeManager.Apply(ApplicationTheme.Unknown);
        }
        UpdateTrayIcon();
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        Locale.CultureChanged -= OnCultureChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        trayMenuWindow?.Close();
        trayMenuWindow = null;
        _icon.Dispose();
        currentIcon?.Dispose();
        currentIcon = null;
    }

    private static bool ConfirmRecordingInterruption()
    {
        if (!HasShutdownSensitiveWork(GlobalMonitor.HasActiveRecorders, Converter.HasActiveConversions, MediaOperationRegistry.HasActiveOperations))
        {
            return true;
        }

        using DialogBlurScope blurScope = new(Application.Current.MainWindow);
        return MessageBox.Question("SureOnRecording".Tr()) == MessageBoxResult.Yes;
    }

    internal static bool HasShutdownSensitiveWork(bool hasActiveRecorders, bool hasActiveConversions)
    {
        return hasActiveRecorders || hasActiveConversions;
    }

    internal static bool HasShutdownSensitiveWork(bool hasActiveRecorders, bool hasActiveConversions, bool hasActiveMediaOperations)
    {
        return hasActiveRecorders || hasActiveConversions || hasActiveMediaOperations;
    }

    private void ShowTrayMenu()
    {
        if (isDisposed)
        {
            return;
        }

        trayMenuWindow?.Close();
        TrayMenuState state = CreateTrayMenuState();
        _icon.ToolTipText = $"Emerde - {TrayMenuWindow.BuildStatusText(state)}";
        TrayMenuWindow window = new(state, HandleTrayMenuAction);
        trayMenuWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(trayMenuWindow, window))
            {
                trayMenuWindow = null;
            }
        };
        try
        {
            window.Show();
        }
        catch (InvalidOperationException e)
        {
            AppSessionLogger.Event("error", "tray", "tray_menu_show_failed", e.Message, new
            {
                type = e.GetType().FullName,
                stackTrace = e.ToString(),
            });
            if (ReferenceEquals(trayMenuWindow, window))
            {
                trayMenuWindow = null;
            }
        }
    }

    private static TrayMenuState CreateTrayMenuState()
    {
        RoomStatus[] rooms = GlobalMonitor.RoomStatus.Values.ToArray();
        return new TrayMenuState(
            $"v{Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}",
            rooms.Count(room => room.StreamStatus == StreamStatus.Streaming),
            rooms.Count(room => room.RecordStatus == RecordStatus.Recording),
            Configurations.IsMonitorRunning.Get(),
            Configurations.IsToRecord.Get(),
            AutoStartupHelper.IsAutorun());
    }

    private void HandleTrayMenuAction(TrayMenuAction action)
    {
        switch (action)
        {
            case TrayMenuAction.ShowMainWindow:
                ActivateMainWindow(0);
                break;
            case TrayMenuAction.OpenSettings:
                ActivateMainWindow(2);
                break;
            case TrayMenuAction.ToggleMonitor:
                if (Application.Current.MainWindow is MainWindow monitorWindow)
                {
                    monitorWindow.ViewModel.ToggleMonitorCommand.Execute(null);
                }
                break;
            case TrayMenuAction.ToggleRecord:
                if (Application.Current.MainWindow is MainWindow recordWindow)
                {
                    recordWindow.ViewModel.ToggleStatusRecordCommand.Execute(null);
                }
                break;
            case TrayMenuAction.ToggleAutoRun:
                ToggleAutoRun();
                break;
            case TrayMenuAction.Restart:
                _ = RestartApplicationAsync();
                break;
            case TrayMenuAction.Exit:
                ShutdownApplication();
                break;
        }
    }

    private static void ActivateMainWindow(int? pageIndex = null)
    {
        if (Application.Current.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        try
        {
            if (pageIndex.HasValue)
            {
                mainWindow.ViewModel.SelectedMainPageIndex = pageIndex.Value;
            }
            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
            }
            mainWindow.Activate();
            Interop.RestoreWindow(new WindowInteropHelper(mainWindow).Handle);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            AppSessionLogger.WriteException(e);
        }
    }

    private static void ToggleAutoRun()
    {
        if (AutoStartupHelper.IsAutorun())
        {
            AutoStartupHelper.RemoveAutorunShortcut();
        }
        else
        {
            AutoStartupHelper.CreateAutorunShortcut();
        }
    }

    private sealed class EmerdeTrayIconHost(Action showContextMenu) : TrayIconHost
    {
        public override void ShowContextMenu()
        {
            showContextMenu();
        }
    }

    /// <summary>
    /// <seealso cref="SystemThemeManager.GetCachedSystemTheme"/>
    /// </summary>
    private static SystemTheme GetTraySystemTheme()
    {
        var currentTheme =
            Registry.GetValue(
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes",
                "CurrentTheme",
                "aero.theme"
            ) as string
            ?? string.Empty;

        if (!string.IsNullOrEmpty(currentTheme))
        {
            currentTheme = currentTheme.ToLower().Trim();

            // This may be changed in the next versions, check the Insider previews
            if (currentTheme.Contains("basic.theme"))
            {
                return SystemTheme.Light;
            }

            if (currentTheme.Contains("aero.theme"))
            {
                return SystemTheme.Light;
            }

            if (currentTheme.Contains("dark.theme"))
            {
                return SystemTheme.Dark;
            }

            if (currentTheme.Contains("hcblack.theme"))
            {
                return SystemTheme.HCBlack;
            }

            if (currentTheme.Contains("hcwhite.theme"))
            {
                return SystemTheme.HCWhite;
            }

            if (currentTheme.Contains("hc1.theme"))
            {
                return SystemTheme.HC1;
            }

            if (currentTheme.Contains("hc2.theme"))
            {
                return SystemTheme.HC2;
            }

            if (currentTheme.Contains("themea.theme"))
            {
                return SystemTheme.Glow;
            }

            if (currentTheme.Contains("themeb.theme"))
            {
                return SystemTheme.CapturedMotion;
            }

            if (currentTheme.Contains("themec.theme"))
            {
                return SystemTheme.Sunrise;
            }

            if (currentTheme.Contains("themed.theme"))
            {
                return SystemTheme.Flow;
            }
        }

        /*if (currentTheme.Contains("custom.theme"))
            return ; custom can be light or dark*/
        var rawSystemUsesLightTheme =
            Registry.GetValue(
                "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                "SystemUsesLightTheme",
                1
            ) ?? 1;

        return rawSystemUsesLightTheme is 0 ? SystemTheme.Dark : SystemTheme.Light;
    }
}
