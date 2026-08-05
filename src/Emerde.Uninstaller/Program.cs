using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Emerde.Uninstaller;

internal static class Program
{
    private const uint MoveFileDelayUntilReboot = 0x4;
    private const int KeySetValue = 0x0002;
    private const int KeyWow64_64Key = 0x0100;
    private const string ShutdownEventName = "Emerde.Shutdown";
    private static readonly nint HkeyLocalMachine = unchecked((nint)0x80000002);
    private static readonly nint HkeyCurrentUser = unchecked((nint)0x80000001);
    private static string installRoot = string.Empty;
    private static string? deferredCleanupRoot;

    public static string InstallRoot => installRoot;
    public static Exception? LastError { get; private set; }

    public static bool Initialize(string[] args)
    {
        installRoot = GetArgument(args, "--installed-root") ?? ResolveInstallRoot();
        installRoot = Path.GetFullPath(installRoot.Trim().Trim('"'));

        return true;
    }

    public static bool IsEmerdeRunning()
    {
        Process[] processes = Process.GetProcessesByName("Emerde");
        try
        {
            return processes.Any(process => process.Id != Environment.ProcessId && !process.HasExited);
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    public static bool StopRunningApplication()
    {
        Process[] processes = Process.GetProcessesByName("Emerde");
        try
        {
            bool gracefulShutdownRequested = RequestApplicationShutdown(ShutdownEventName);
            foreach (Process process in processes)
            {
                try
                {
                    if (!process.HasExited && gracefulShutdownRequested)
                    {
                        process.WaitForExit(10000);
                    }

                    if (!process.HasExited && !gracefulShutdownRequested && process.CloseMainWindow())
                    {
                        process.WaitForExit(4000);
                    }

                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        process.WaitForExit(4000);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (Win32Exception)
                {
                }
            }

            return processes.All(process => process.HasExited);
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    internal static bool RequestApplicationShutdown(string eventName)
    {
        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(eventName);
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

    public static bool Uninstall(bool keepData)
    {
        LastError = null;
        try
        {
            RemoveShortcuts();
            RemoveRegistry();
            foreach (string directoryName in new[] { "bin", "native", "resources", "licenses" })
            {
                DeleteOrSchedule(Path.Combine(installRoot, directoryName));
            }

            foreach (string fileName in new[] { "Emerde.lnk", "Uninstall.lnk", "Uninstall Emerde.lnk", "卸载 Emerde.lnk" })
            {
                DeleteOrSchedule(Path.Combine(installRoot, fileName));
            }

            if (!keepData)
            {
                DeleteOrSchedule(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Emerde"));
            }

            deferredCleanupRoot = installRoot;
            return true;
        }
        catch (Exception exception)
        {
            LastError = exception;
            return false;
        }
    }

    public static void ScheduleDeferredCleanup()
    {
        if (deferredCleanupRoot is null)
        {
            return;
        }

        if (!StartDeferredCleanup(deferredCleanupRoot, Environment.ProcessId))
        {
            ScheduleCleanupAtRestart(deferredCleanupRoot);
        }
    }

    internal static bool StartDeferredCleanup(string root, int processId)
    {
        string normalizedRoot = Path.GetFullPath(root);
        string cleanerSource = Path.Combine(normalizedRoot, "maintenance", "Emerde.Cleanup.exe");
        if (!File.Exists(cleanerSource))
        {
            return false;
        }

        string cleanerDirectory = Path.Combine(Path.GetTempPath(), "Emerde", "cleanup");
        Directory.CreateDirectory(cleanerDirectory);
        string cleanerPath = Path.Combine(cleanerDirectory, "Emerde.Cleanup.exe");

        try
        {
            File.Copy(cleanerSource, cleanerPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        ProcessStartInfo startInfo = new(cleanerPath)
        {
            UseShellExecute = false,
            WorkingDirectory = cleanerDirectory,
        };
        startInfo.ArgumentList.Add("--root");
        startInfo.ArgumentList.Add(normalizedRoot);
        startInfo.ArgumentList.Add("--parent");
        startInfo.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            using Process? cleaner = Process.Start(startInfo);
            return cleaner is not null;
        }
        catch (Win32Exception)
        {
            try
            {
                File.Delete(cleanerPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

            return false;
        }
    }

    private static void ScheduleCleanupAtRestart(string root)
    {
        string maintenanceDirectory = Path.Combine(root, "maintenance");
        string runtimeDirectory = Path.Combine(root, "runtime");
        ScheduleDirectoryDeletion(maintenanceDirectory);
        ScheduleDirectoryDeletion(runtimeDirectory);
        MoveFileEx(root, null, MoveFileDelayUntilReboot);
    }

    private static void ScheduleDirectoryDeletion(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            MoveFileEx(file, null, MoveFileDelayUntilReboot);
        }

        foreach (string directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                     .OrderByDescending(value => value.Length))
        {
            MoveFileEx(directory, null, MoveFileDelayUntilReboot);
        }

        MoveFileEx(path, null, MoveFileDelayUntilReboot);
    }

    private static void RemoveShortcuts()
    {
        string startMenuDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Emerde");
        foreach (string path in new[]
        {
            Path.Combine(installRoot, "Emerde.lnk"),
            Path.Combine(installRoot, "Uninstall.lnk"),
            Path.Combine(installRoot, "Uninstall Emerde.lnk"),
            Path.Combine(installRoot, "卸载 Emerde.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Emerde.lnk"),
            Path.Combine(startMenuDirectory, "Emerde.lnk"),
            Path.Combine(startMenuDirectory, "Uninstall.lnk"),
            Path.Combine(startMenuDirectory, "Uninstall Emerde.lnk"),
            Path.Combine(startMenuDirectory, "卸载 Emerde.lnk"),
        })
        {
            DeleteOrSchedule(path);
        }

        TryDeleteEmptyDirectory(startMenuDirectory);
    }

    private static void RemoveRegistry()
    {
        DeleteRegistryTree(HkeyLocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Emerde");
        DeleteRegistryValue(HkeyCurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Emerde");
    }

    private static void DeleteRegistryTree(nint root, string subKey, string childKey)
    {
        if (RegOpenKeyEx(root, subKey, 0, KeySetValue | KeyWow64_64Key, out nint key) != 0)
        {
            return;
        }

        try
        {
            RegDeleteTree(key, childKey);
        }
        finally
        {
            RegCloseKey(key);
        }
    }

    private static void DeleteRegistryValue(nint root, string subKey, string valueName)
    {
        if (RegOpenKeyEx(root, subKey, 0, KeySetValue, out nint key) != 0)
        {
            return;
        }

        try
        {
            RegDeleteValue(key, valueName);
        }
        finally
        {
            RegCloseKey(key);
        }
    }

    private static void DeleteOrSchedule(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            MoveFileEx(path, null, MoveFileDelayUntilReboot);
        }
        catch (UnauthorizedAccessException)
        {
            MoveFileEx(path, null, MoveFileDelayUntilReboot);
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }

    private static string? GetArgument(string[] args, string name)
    {
        int index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string ResolveInstallRoot()
    {
        string? executablePath = Environment.ProcessPath;
        string? maintenancePath = executablePath is null ? null : Path.GetDirectoryName(executablePath);
        DirectoryInfo? maintenanceDirectory = maintenancePath is null ? null : new DirectoryInfo(maintenancePath);

        if (maintenanceDirectory?.Parent is not null
            && string.Equals(maintenanceDirectory.Name, "maintenance", StringComparison.OrdinalIgnoreCase))
        {
            return maintenanceDirectory.Parent.FullName;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Emerde");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyEx(nint root, string subKey, uint options, int access, out nint result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegDeleteTree(nint key, string subKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegDeleteValue(nint key, string valueName);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(nint key);
}
