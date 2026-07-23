using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Emerde.Extensions;

internal static class RuntimeHelper
{
    private const string RestartParentArgumentPrefix = "--emerde-restart-parent=";

    public static void CheckSingleInstance(string instanceName, Action<bool> callback = null!)
    {
        EventWaitHandle? handle;

        try
        {
            handle = EventWaitHandle.OpenExisting(instanceName);
            handle.Set();
            callback?.Invoke(false);
            Environment.Exit(0xFFFF);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            callback?.Invoke(true);
            handle = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName);
        }

        _ = Task.Factory.StartNew(() =>
        {
            while (handle.WaitOne())
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    Application.Current.MainWindow?.Activate();
                    Application.Current.MainWindow?.Show();
                    Interop.RestoreWindow(new WindowInteropHelper(Application.Current.MainWindow).Handle);
                });
            }
        }, TaskCreationOptions.LongRunning).ConfigureAwait(false);
    }

    public static string ReArguments()
    {
        string[] args = Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(arg => !arg.StartsWith(RestartParentArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        for (int i = default; i < args.Length; i++)
        {
            args[i] = $@"""{args[i]}""";
        }
        return string.Join(" ", args);
    }

    public static void WaitForRestartParent(IEnumerable<string>? args)
    {
        int? parentProcessId = GetRestartParentProcessId(args);
        if (!parentProcessId.HasValue || parentProcessId.Value == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using Process parent = Process.GetProcessById(parentProcessId.Value);
            _ = parent.WaitForExit(5000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
        }
    }

    internal static int? GetRestartParentProcessId(IEnumerable<string>? args)
    {
        string? argument = args?.LastOrDefault(arg => arg.StartsWith(RestartParentArgumentPrefix, StringComparison.OrdinalIgnoreCase));
        return int.TryParse(argument?[RestartParentArgumentPrefix.Length..], out int processId) && processId > 0
            ? processId
            : null;
    }

    internal static string BuildRestartArguments(string? args, int parentProcessId)
    {
        string restartArgument = $"{RestartParentArgumentPrefix}{parentProcessId}";
        return string.IsNullOrWhiteSpace(args) ? restartArgument : $"{args} {restartArgument}";
    }

    public static bool Restart(string fileName = null!, string dir = null!, string args = null!, int? exitCode = null, bool forced = false, Action? beforeExit = null)
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = fileName ?? GetExecutablePath(),
                    WorkingDirectory = dir ?? Environment.CurrentDirectory,
                    Arguments = BuildRestartArguments(args ?? ReArguments(), Environment.ProcessId),
                    UseShellExecute = true,
                },
            };
            if (!process.Start())
            {
                return false;
            }
        }
        catch (Win32Exception)
        {
            return false;
        }

        CompleteRestart(beforeExit, () =>
        {
            if (forced)
            {
                Process.GetCurrentProcess().Kill();
            }
            Environment.Exit(exitCode ?? 'r' + 'e' + 's' + 't' + 'a' + 'r' + 't');
        });
        return true;

        static string GetExecutablePath()
        {
            string fileName = AppDomain.CurrentDomain.FriendlyName;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fileName += ".exe";
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }
    }

    internal static void CompleteRestart(Action? beforeExit, Action exit)
    {
        try
        {
            beforeExit?.Invoke();
        }
        finally
        {
            exit();
        }
    }
}
