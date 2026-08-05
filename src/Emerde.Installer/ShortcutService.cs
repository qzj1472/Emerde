using System.IO;
using System.Runtime.InteropServices;

namespace Emerde.Installer;

internal static class ShortcutService
{
    private const string ProductShortcutName = "Emerde.lnk";
    private const string UninstallShortcutName = "Uninstall.lnk";
    private static readonly string[] LegacyUninstallShortcutNames = ["Uninstall Emerde.lnk", "卸载 Emerde.lnk"];

    public static void Apply(string installRoot, bool createExternalShortcuts)
    {
        string binaryExecutable = InstallationPaths.BinaryExecutable(installRoot);
        string maintenanceExecutable = InstallationPaths.MaintenanceExecutable(installRoot);

        CreateShortcut(
            Path.Combine(installRoot, ProductShortcutName),
            binaryExecutable,
            string.Empty,
            "Launch Emerde",
            binaryExecutable);
        CreateShortcut(
            Path.Combine(installRoot, UninstallShortcutName),
            maintenanceExecutable,
            "--uninstall",
            "Uninstall Emerde",
            binaryExecutable);

        string desktopShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ProductShortcutName);
        string startMenuDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            InstallationPaths.ProductName);

        if (!createExternalShortcuts)
        {
            DeleteFile(desktopShortcut);
            DeleteFile(Path.Combine(startMenuDirectory, ProductShortcutName));
            DeleteFile(Path.Combine(startMenuDirectory, UninstallShortcutName));
            DeleteDirectoryIfEmpty(startMenuDirectory);
            return;
        }

        CreateShortcut(desktopShortcut, binaryExecutable, string.Empty, "Launch Emerde", binaryExecutable);
        CreateShortcut(
            Path.Combine(startMenuDirectory, ProductShortcutName),
            binaryExecutable,
            string.Empty,
            "Launch Emerde",
            binaryExecutable);
        CreateShortcut(
            Path.Combine(startMenuDirectory, UninstallShortcutName),
            maintenanceExecutable,
            "--uninstall",
            "Uninstall Emerde",
            binaryExecutable);
    }

    public static void Remove(string installRoot)
    {
        DeleteFile(Path.Combine(installRoot, ProductShortcutName));
        DeleteFile(Path.Combine(installRoot, UninstallShortcutName));
        foreach (string legacyName in LegacyUninstallShortcutNames)
        {
            DeleteFile(Path.Combine(installRoot, legacyName));
        }
        DeleteFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ProductShortcutName));
        string startMenuDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            InstallationPaths.ProductName);
        DeleteFile(Path.Combine(startMenuDirectory, ProductShortcutName));
        DeleteFile(Path.Combine(startMenuDirectory, UninstallShortcutName));
        foreach (string legacyName in LegacyUninstallShortcutNames)
        {
            DeleteFile(Path.Combine(startMenuDirectory, legacyName));
        }
        DeleteDirectoryIfEmpty(startMenuDirectory);
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string arguments,
        string description,
        string iconLocation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        dynamic? shell = null;
        dynamic? shortcut = null;

        try
        {
            Type shellType = Type.GetTypeFromCLSID(
                new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8"),
                throwOnError: true)!;
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("无法创建 Windows 快捷方式组件。");
            shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.WindowStyle = 1;
            shortcut.Arguments = arguments;
            shortcut.Description = description;
            shortcut.IconLocation = iconLocation;
            shortcut.Save();
        }
        finally
        {
            if (shortcut is not null)
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }
}
