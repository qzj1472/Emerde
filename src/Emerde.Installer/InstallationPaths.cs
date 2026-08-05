using System.IO;
using System.Reflection;

namespace Emerde.Installer;

internal static class InstallationPaths
{
    public const string ProductName = "Emerde";
    public const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Emerde";
    public const string AutoStartRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string StateFileName = "install-state.json";
    public const string RepairStateFileName = "repair-state.json";
    public const string MaintenanceExecutableName = "Emerde.Uninstaller.exe";
    public const string ApplicationExecutableName = "Emerde.exe";
    public const string BinaryDirectoryName = "bin";
    public const string NativeDirectoryName = "native";
    public const string ResourcesDirectoryName = "resources";
    public const string LicensesDirectoryName = "licenses";
    public const string MaintenanceDirectoryName = "maintenance";
    public const string RuntimeDirectoryName = "runtime";

    public static string ProductVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "1.0.0.0";

    public static string DefaultInstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ProductName);

    public static string UserDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ProductName);

    public static string BinaryDirectory(string installRoot) => Path.Combine(installRoot, BinaryDirectoryName);

    public static string BinaryExecutable(string installRoot) =>
        Path.Combine(BinaryDirectory(installRoot), ApplicationExecutableName);

    public static string NativeDirectory(string installRoot) => Path.Combine(installRoot, NativeDirectoryName);

    public static string ResourcesDirectory(string installRoot) => Path.Combine(installRoot, ResourcesDirectoryName);

    public static string LicensesDirectory(string installRoot) => Path.Combine(installRoot, LicensesDirectoryName);

    public static string MaintenanceDirectory(string installRoot) =>
        Path.Combine(installRoot, MaintenanceDirectoryName);

    public static string RuntimeDirectory(string installRoot) =>
        Path.Combine(installRoot, RuntimeDirectoryName);

    public static string MaintenanceExecutable(string installRoot) =>
        Path.Combine(MaintenanceDirectory(installRoot), MaintenanceExecutableName);

    public static string StateFile(string installRoot) =>
        Path.Combine(MaintenanceDirectory(installRoot), StateFileName);

    public static string RepairStateFile(string installRoot) =>
        Path.Combine(MaintenanceDirectory(installRoot), RepairStateFileName);

    public static string NormalizeInstallRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("请选择安装位置。");
        }

        string expandedPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expandedPath));
        string? driveRoot = Path.GetPathRoot(fullPath);

        if (string.IsNullOrWhiteSpace(driveRoot)
            || string.Equals(fullPath, Path.TrimEndingDirectorySeparator(driveRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("不能将 Emerde 安装到磁盘根目录。");
        }

        string[] protectedPaths =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ];

        if (protectedPaths.Any(protectedPath =>
                !string.IsNullOrWhiteSpace(protectedPath)
                && string.Equals(
                    fullPath,
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedPath)),
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("请选择这些系统目录下的独立子文件夹。");
        }

        return fullPath;
    }
}
