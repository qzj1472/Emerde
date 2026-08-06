using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Emerde.Installer;

public partial class MainWindow : FluentWindow
{
    private const double CollapsedWindowHeight = 400;
    private const double ExpandedWindowHeight = 508;
    private const int DwmWindowAttributeBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const int WindowStyleIndex = -16;
    private const long WindowBorderStyle = 0x00800000L;
    private const long WindowDialogFrameStyle = 0x00400000L;
    private const long WindowThickFrameStyle = 0x00040000L;
    private const uint SetWindowFrameChanged = 0x0020;
    private const uint SetWindowNoActivate = 0x0010;
    private const uint SetWindowNoMove = 0x0002;
    private const uint SetWindowNoSize = 0x0001;
    private const uint SetWindowNoZOrder = 0x0004;
    private const string ShutdownEventName = "Emerde.Shutdown";
    private readonly InstallationService installationService;
    private readonly bool forceRunningPreview;
    private bool appStopAcknowledged;
    private bool operationInProgress;
    private bool operationSucceeded;
    private InstallationOperation? pendingOperation;
    private InstallationOperation selectedOperation = InstallationOperation.Install;
    private InstallationInfo? installedInfo;
    private string? successfulInstallRoot;

    public MainWindow()
    {
        InitializeComponent();
        string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        installationService = new InstallationService(InstallerPayload.FromArguments(arguments));
        forceRunningPreview = arguments.Contains("--running-preview", StringComparer.OrdinalIgnoreCase);
        string? requestedInstallRoot = GetArgumentValue(arguments, "--installed-root");

        if (arguments.Contains("--maintenance-preview", StringComparer.OrdinalIgnoreCase))
        {
            string previewRoot = requestedInstallRoot ?? InstallationPaths.DefaultInstallRoot;
            InstallationState previewState = new(previewRoot, true, false, InstallationPaths.ProductVersion);
            installedInfo = new InstallationInfo(previewRoot, InstallationPaths.ProductVersion, previewState);
        }
        else
        {
            installedInfo = InstallationRegistry.Detect(requestedInstallRoot);
        }

        InstallPathTextBox.Text = installedInfo?.InstallRoot ?? InstallationPaths.DefaultInstallRoot;

        if (installedInfo is null)
        {
            ShowWelcomePage();
            return;
        }

        InstalledVersionText.Text = $"已安装版本 {installedInfo.Version}";
        ShowPage(MaintenancePage);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref uint attributeValue,
        int attributeSize);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    private void MainWindowSourceInitialized(object? sender, EventArgs e)
    {
        RemoveWindowBorder();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RemoveWindowBorder();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        RemoveWindowBorder();
    }

    internal void BringToForegroundOnce()
    {
        if (!IsVisible)
        {
            return;
        }

        IntPtr windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero || GetForegroundWindow() == windowHandle)
        {
            return;
        }

        _ = Activate();
        if (SetForegroundWindow(windowHandle) || GetForegroundWindow() == windowHandle)
        {
            return;
        }

        Topmost = true;
        try
        {
            _ = Activate();
            _ = SetForegroundWindow(windowHandle);
        }
        finally
        {
            Topmost = false;
        }
    }

    private void RemoveWindowBorder()
    {
        IntPtr windowHandle = new WindowInteropHelper(this).Handle;
        uint borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowAttributeBorderColor,
            ref borderColor,
            Marshal.SizeOf<uint>());

        long windowStyle = GetWindowLongPtr(windowHandle, WindowStyleIndex).ToInt64();
        long borderlessStyle = windowStyle
            & ~WindowBorderStyle
            & ~WindowDialogFrameStyle
            & ~WindowThickFrameStyle;

        if (borderlessStyle == windowStyle)
        {
            return;
        }

        _ = SetWindowLongPtr(windowHandle, WindowStyleIndex, new IntPtr(borderlessStyle));
        _ = SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SetWindowFrameChanged
                | SetWindowNoActivate
                | SetWindowNoMove
                | SetWindowNoSize
                | SetWindowNoZOrder);
    }

    private void InstallNowButtonClick(object sender, RoutedEventArgs e)
    {
        StartOperation(InstallationOperation.Install);
    }

    private void OptionsButtonClick(object sender, RoutedEventArgs e)
    {
        bool showOptions = InstallOptionsPanel.Visibility != Visibility.Visible;
        InstallOptionsPanel.Visibility = showOptions ? Visibility.Visible : Visibility.Collapsed;
        OptionsToggleButton.Content = showOptions ? "收起选项" : "安装选项";
        Height = showOptions ? ExpandedWindowHeight : CollapsedWindowHeight;
    }

    private void BrowseInstallFolderButtonClick(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "选择安装位置",
            InitialDirectory = InstallPathTextBox.Text,
        };

        if (dialog.ShowDialog(this) == true)
        {
            InstallPathTextBox.Text = dialog.FolderName;
        }
    }

    private void UpgradeButtonClick(object sender, RoutedEventArgs e)
    {
        StartOperation(InstallationOperation.Upgrade);
    }

    private void RepairButtonClick(object sender, RoutedEventArgs e)
    {
        StartOperation(InstallationOperation.Repair);
    }

    private void UninstallButtonClick(object sender, RoutedEventArgs e)
    {
        StartOperation(InstallationOperation.Uninstall);
    }

    private void StartOperation(InstallationOperation operation)
    {
        if (operationInProgress)
        {
            return;
        }

        if (!appStopAcknowledged && (forceRunningPreview || IsEmerdeRunning()))
        {
            pendingOperation = operation;
            RunningDialogLayer.Visibility = Visibility.Visible;
            return;
        }

        _ = ExecuteOperationAsync(operation);
    }

    private void CancelStopButtonClick(object sender, RoutedEventArgs e)
    {
        pendingOperation = null;
        RunningDialogLayer.Visibility = Visibility.Collapsed;
    }

    private async void StopAndContinueButtonClick(object sender, RoutedEventArgs e)
    {
        InstallationOperation operation = pendingOperation ?? InstallationOperation.Install;
        StopAndContinueButton.IsEnabled = false;
        StopAndContinueButton.Content = "正在停止";
        bool gracefulShutdownRequested = RequestEmerdeShutdown();

        foreach (Process process in GetRunningEmerdeProcesses())
        {
            try
            {
                if (gracefulShutdownRequested)
                {
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch (TimeoutException)
                    {
                    }
                }

                if (!process.HasExited && !gracefulShutdownRequested && process.CloseMainWindow())
                {
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
                    }
                    catch (TimeoutException)
                    {
                    }
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        appStopAcknowledged = true;
        pendingOperation = null;
        StopAndContinueButton.Content = "停止并继续";
        StopAndContinueButton.IsEnabled = true;
        RunningDialogLayer.Visibility = Visibility.Collapsed;
        await ExecuteOperationAsync(operation);
    }

    private static bool RequestEmerdeShutdown()
    {
        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(ShutdownEventName);
            return handle.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ExecuteOperationAsync(InstallationOperation operation)
    {
        operationInProgress = true;
        selectedOperation = operation;
        operationSucceeded = false;
        PrepareProgressPage(operation);
        Progress<InstallationProgress> progress = new(UpdateProgress);

        try
        {
            if (operation == InstallationOperation.Uninstall)
            {
                InstallationInfo installation = installedInfo
                    ?? throw new InvalidOperationException("没有检测到可卸载的 Emerde。");
                await installationService.UninstallAsync(
                    installation,
                    KeepUserDataCheckBox.IsChecked == true,
                    progress);
                installedInfo = null;
                successfulInstallRoot = null;
            }
            else
            {
                InstallationRequest request = CreateInstallationRequest(operation);
                installedInfo = await Task.Run(() =>
                    installationService.InstallAsync(request, operation, progress));
                successfulInstallRoot = installedInfo.InstallRoot;
            }

            operationSucceeded = true;
            ShowFinishPage();
        }
        catch (Exception exception)
        {
            ShowFailurePage(exception);
        }
        finally
        {
            operationInProgress = false;
        }
    }

    private InstallationRequest CreateInstallationRequest(InstallationOperation operation)
    {
        if (operation == InstallationOperation.Install || installedInfo is null)
        {
            return new InstallationRequest(
                InstallationPaths.NormalizeInstallRoot(InstallPathTextBox.Text),
                CreateShortcutsCheckBox.IsChecked == true,
                AutoStartCheckBox.IsChecked == true);
        }

        InstallationState state = InstallationRegistry.ReadState(installedInfo.InstallRoot);
        return new InstallationRequest(state.InstallRoot, state.CreateShortcuts, state.AutoStart);
    }

    private void PrepareProgressPage(InstallationOperation operation)
    {
        SimulationProgress.Value = 0;
        ProgressValueText.Text = "0%";
        ProgressTitleText.Text = operation switch
        {
            InstallationOperation.Upgrade => "正在升级 Emerde",
            InstallationOperation.Repair => "正在修复 Emerde",
            InstallationOperation.Uninstall => "正在卸载 Emerde",
            _ => "正在安装 Emerde",
        };
        ProgressStatusText.Text = "正在准备...";
        ShowPage(ProgressPage);
    }

    private void UpdateProgress(InstallationProgress progress)
    {
        SimulationProgress.Value = progress.Percentage;
        ProgressValueText.Text = $"{progress.Percentage}%";
        ProgressStatusText.Text = progress.Status;
    }

    private void ShowFinishPage()
    {
        bool isUninstall = selectedOperation == InstallationOperation.Uninstall;
        FinishStatusBorder.Background = new SolidColorBrush(Color.FromRgb(0x14, 0xB8, 0x6B));
        FinishStatusIcon.Text = "\u2713";
        FinishTitleText.Text = selectedOperation switch
        {
            InstallationOperation.Upgrade => "Emerde 已升级",
            InstallationOperation.Repair => "Emerde 已修复",
            InstallationOperation.Uninstall => "Emerde 已卸载",
            _ => "Emerde 已安装",
        };
        FinishMessageText.Text = isUninstall
            ? KeepUserDataCheckBox.IsChecked == true
                ? "程序文件已移除，用户数据已保留。"
                : "程序文件和用户数据已移除。"
            : "操作已完成，可以开始使用 Emerde。";
        LaunchAfterFinishCheckBox.Visibility = isUninstall ? Visibility.Collapsed : Visibility.Visible;
        UpdateFinishPrimaryButtonContent();
        ShowPage(FinishPage);
    }

    private void ShowFailurePage(Exception exception)
    {
        FinishStatusBorder.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
        FinishStatusIcon.Text = "\u00D7";
        FinishTitleText.Text = "操作未完成";
        FinishMessageText.Text = GetFailureMessage(exception);
        LaunchAfterFinishCheckBox.Visibility = Visibility.Collapsed;
        FinishPrimaryButton.Content = "关闭";
        ShowPage(FinishPage);
    }

    private void LaunchAfterFinishCheckBoxChanged(object sender, RoutedEventArgs e)
    {
        UpdateFinishPrimaryButtonContent();
    }

    private void UpdateFinishPrimaryButtonContent()
    {
        FinishPrimaryButton.Content = operationSucceeded
            && selectedOperation != InstallationOperation.Uninstall
            && LaunchAfterFinishCheckBox.IsChecked == true
                ? "运行 Emerde"
                : "完成";
    }

    private void FinishPrimaryButtonClick(object sender, RoutedEventArgs e)
    {
        if (operationSucceeded
            && selectedOperation != InstallationOperation.Uninstall
            && LaunchAfterFinishCheckBox.IsChecked == true
            && successfulInstallRoot is not null)
        {
            string executablePath = InstallationPaths.BinaryExecutable(successfulInstallRoot);
            if (File.Exists(executablePath))
            {
                Process.Start(new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = true,
                });
            }
        }

        Close();
    }

    private void ShowWelcomePage()
    {
        InstallOptionsPanel.Visibility = Visibility.Collapsed;
        OptionsToggleButton.Content = "安装选项";
        ShowPage(WelcomePage);
    }

    private void ShowPage(UIElement page)
    {
        Height = CollapsedWindowHeight;
        WelcomePage.Visibility = Visibility.Collapsed;
        MaintenancePage.Visibility = Visibility.Collapsed;
        ProgressPage.Visibility = Visibility.Collapsed;
        FinishPage.Visibility = Visibility.Collapsed;
        RunningDialogLayer.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }

    private static bool IsEmerdeRunning()
    {
        List<Process> processes = GetRunningEmerdeProcesses();
        bool isRunning = processes.Count > 0;
        processes.ForEach(process => process.Dispose());
        return isRunning;
    }

    private static List<Process> GetRunningEmerdeProcesses()
    {
        return Process.GetProcessesByName(InstallationPaths.ProductName)
            .Where(process => process.Id != Environment.ProcessId && !process.HasExited)
            .ToList();
    }

    private static string? GetArgumentValue(string[] arguments, string name)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private static string GetFailureMessage(Exception exception)
    {
        Exception rootException = exception;
        while (rootException.InnerException is not null)
        {
            rootException = rootException.InnerException;
        }

        return string.IsNullOrWhiteSpace(rootException.Message)
            ? "发生未知错误，请重新运行安装器。"
            : rootException.Message;
    }
}
