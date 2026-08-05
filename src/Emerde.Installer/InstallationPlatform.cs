using System.Diagnostics;
using System.IO;

namespace Emerde.Installer;

internal interface IInstallationPlatform
{
    string UserDataDirectory { get; }

    void ApplyShortcuts(string installRoot, bool createExternalShortcuts);

    void RemoveShortcuts(string installRoot);

    void SetAutoStart(string installRoot, bool enabled);

    void WriteInstallationInfo(InstallationState state, long estimatedSizeBytes);

    void RemoveInstallationInfo();

    void EnsureAvailableSpace(string installRoot, long payloadSizeBytes);

    void ApplyTransparentCompression(string installRoot);
}

internal sealed class WindowsInstallationPlatform : IInstallationPlatform
{
    public string UserDataDirectory => InstallationPaths.UserDataDirectory;

    public void ApplyShortcuts(string installRoot, bool createExternalShortcuts)
    {
        ShortcutService.Apply(installRoot, createExternalShortcuts);
    }

    public void RemoveShortcuts(string installRoot)
    {
        ShortcutService.Remove(installRoot);
    }

    public void SetAutoStart(string installRoot, bool enabled)
    {
        InstallationRegistry.SetAutoStart(installRoot, enabled);
    }

    public void WriteInstallationInfo(InstallationState state, long estimatedSizeBytes)
    {
        InstallationRegistry.WriteInstallationInfo(state, estimatedSizeBytes);
    }

    public void RemoveInstallationInfo()
    {
        InstallationRegistry.RemoveInstallationInfo();
    }

    public void EnsureAvailableSpace(string installRoot, long payloadSizeBytes)
    {
        const long minimumWorkingSpace = 600L * 1024 * 1024;
        const long operationReserve = 128L * 1024 * 1024;
        string? driveRoot = Path.GetPathRoot(Path.GetFullPath(installRoot));
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            throw new IOException("无法确定安装位置所在磁盘。");
        }

        DriveInfo drive = new(driveRoot);
        long requiredBytes = Math.Max(
            minimumWorkingSpace,
            checked(payloadSizeBytes * 2 + operationReserve));
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            long requiredMiB = (requiredBytes + 1024 * 1024 - 1) / (1024 * 1024);
            throw new IOException($"安装位置至少需要 {requiredMiB} MiB 可用空间。");
        }
    }

    public void ApplyTransparentCompression(string installRoot)
    {
        try
        {
            string? driveRoot = Path.GetPathRoot(Path.GetFullPath(installRoot));
            if (string.IsNullOrWhiteSpace(driveRoot)
                || !string.Equals(new DriveInfo(driveRoot).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (string directoryName in new[]
            {
                InstallationPaths.BinaryDirectoryName,
                InstallationPaths.NativeDirectoryName,
                InstallationPaths.ResourcesDirectoryName,
                InstallationPaths.LicensesDirectoryName,
                InstallationPaths.MaintenanceDirectoryName,
                InstallationPaths.RuntimeDirectoryName,
            })
            {
                string directory = Path.Combine(installRoot, directoryName);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                using Process process = Process.Start(new ProcessStartInfo(
                    Path.Combine(Environment.SystemDirectory, "compact.exe"))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = directory,
                    ArgumentList =
                    {
                        "/C",
                        $"/S:{directory}",
                        "/A",
                        "/I",
                        "/Q",
                        "/EXE:LZX",
                        "*",
                    },
                })!;
                process.WaitForExit();
            }
        }
        catch (Exception)
        {
        }
    }
}
