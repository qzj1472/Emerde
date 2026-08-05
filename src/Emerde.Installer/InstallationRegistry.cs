using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace Emerde.Installer;

internal static class InstallationRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static InstallationInfo? Detect(string? requestedInstallRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedInstallRoot))
        {
            InstallationInfo? requestedInstallation = DetectAtPath(requestedInstallRoot);
            if (requestedInstallation is not null)
            {
                return requestedInstallation;
            }
        }

        try
        {
            using RegistryKey localMachine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64);
            using RegistryKey? key = localMachine.OpenSubKey(InstallationPaths.UninstallRegistryPath);
            string? installRoot = key?.GetValue("InstallLocation") as string;
            string? version = key?.GetValue("DisplayVersion") as string;

            if (!string.IsNullOrWhiteSpace(installRoot))
            {
                InstallationInfo? registeredInstallation = DetectAtPath(installRoot, version);
                if (registeredInstallation is not null)
                {
                    return registeredInstallation;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Security.SecurityException)
        {
        }

        return DetectAtPath(InstallationPaths.DefaultInstallRoot);
    }

    public static InstallationState ReadState(string installRoot)
    {
        string normalizedRoot = InstallationPaths.NormalizeInstallRoot(installRoot);
        string statePath = InstallationPaths.StateFile(normalizedRoot);

        try
        {
            if (File.Exists(statePath))
            {
                InstallationState? state = JsonSerializer.Deserialize<InstallationState>(
                    File.ReadAllText(statePath),
                    JsonOptions);

                if (state is not null)
                {
                    return state with { InstallRoot = normalizedRoot };
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new InstallationState(normalizedRoot, true, false, GetInstalledVersion(normalizedRoot));
    }

    public static void WriteState(InstallationState state)
    {
        Directory.CreateDirectory(InstallationPaths.MaintenanceDirectory(state.InstallRoot));
        File.WriteAllText(
            InstallationPaths.StateFile(state.InstallRoot),
            JsonSerializer.Serialize(state, JsonOptions));
    }

    public static RepairState? ReadRepairState(string installRoot)
    {
        string normalizedRoot = InstallationPaths.NormalizeInstallRoot(installRoot);
        string statePath = InstallationPaths.RepairStateFile(normalizedRoot);

        try
        {
            if (!File.Exists(statePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<RepairState>(File.ReadAllText(statePath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void WriteRepairState(string installRoot, string version)
    {
        string normalizedRoot = InstallationPaths.NormalizeInstallRoot(installRoot);
        List<RepairFileState> files = [];
        string[] components =
        [
            InstallationPaths.BinaryDirectoryName,
            InstallationPaths.NativeDirectoryName,
            InstallationPaths.ResourcesDirectoryName,
            InstallationPaths.LicensesDirectoryName,
            InstallationPaths.MaintenanceDirectoryName,
            InstallationPaths.RuntimeDirectoryName,
        ];

        foreach (string component in components)
        {
            string componentPath = Path.Combine(normalizedRoot, component);
            if (!Directory.Exists(componentPath))
            {
                continue;
            }

            foreach (string filePath in Directory.EnumerateFiles(componentPath, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                if (string.Equals(fileName, InstallationPaths.StateFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, InstallationPaths.RepairStateFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using FileStream stream = File.OpenRead(filePath);
                string relativePath = Path.GetRelativePath(normalizedRoot, filePath)
                    .Replace(Path.DirectorySeparatorChar, '/');
                files.Add(new RepairFileState(
                    relativePath,
                    stream.Length,
                    Convert.ToHexString(SHA256.HashData(stream)),
                    component));
            }
        }

        Directory.CreateDirectory(InstallationPaths.MaintenanceDirectory(normalizedRoot));
        RepairState state = new(version, files);
        File.WriteAllText(
            InstallationPaths.RepairStateFile(normalizedRoot),
            JsonSerializer.Serialize(state, JsonOptions));
    }

    public static void WriteInstallationInfo(InstallationState state, long estimatedSizeBytes)
    {
        string binaryExecutable = InstallationPaths.BinaryExecutable(state.InstallRoot);
        string maintenanceExecutable = InstallationPaths.MaintenanceExecutable(state.InstallRoot);
        using RegistryKey localMachine = RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine,
            RegistryView.Registry64);
        using RegistryKey key = localMachine.CreateSubKey(InstallationPaths.UninstallRegistryPath, writable: true);
        key.SetValue("DisplayName", InstallationPaths.ProductName);
        key.SetValue("DisplayVersion", state.Version);
        key.SetValue("DisplayIcon", binaryExecutable);
        key.SetValue("Publisher", "Emerde");
        key.SetValue("InstallLocation", state.InstallRoot);
        key.SetValue("UninstallString", $"\"{maintenanceExecutable}\" --uninstall");
        key.SetValue("ModifyPath", $"\"{maintenanceExecutable}\" --maintenance");
        key.SetValue("EstimatedSize", Math.Min(int.MaxValue, estimatedSizeBytes / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 0, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 0, RegistryValueKind.DWord);
    }

    public static void RemoveInstallationInfo()
    {
        using RegistryKey localMachine = RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine,
            RegistryView.Registry64);
        localMachine.DeleteSubKeyTree(InstallationPaths.UninstallRegistryPath, throwOnMissingSubKey: false);
    }

    public static void SetAutoStart(string installRoot, bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(InstallationPaths.AutoStartRegistryPath);

        if (enabled)
        {
            key.SetValue(
                InstallationPaths.ProductName,
                $"\"{InstallationPaths.BinaryExecutable(installRoot)}\" /autorun");
            return;
        }

        key.DeleteValue(InstallationPaths.ProductName, throwOnMissingValue: false);
    }

    private static InstallationInfo? DetectAtPath(string installRoot, string? registeredVersion = null)
    {
        string normalizedRoot;

        try
        {
            normalizedRoot = InstallationPaths.NormalizeInstallRoot(installRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }

        string binaryExecutable = InstallationPaths.BinaryExecutable(normalizedRoot);
        string legacyExecutable = Path.Combine(normalizedRoot, InstallationPaths.ApplicationExecutableName);

        if (!File.Exists(binaryExecutable) && !File.Exists(legacyExecutable))
        {
            return null;
        }

        InstallationState state = ReadState(normalizedRoot);
        string version = string.IsNullOrWhiteSpace(registeredVersion)
            ? state.Version
            : registeredVersion;
        return new InstallationInfo(normalizedRoot, version, state with { Version = version });
    }

    private static string GetInstalledVersion(string installRoot)
    {
        string[] candidates =
        [
            InstallationPaths.BinaryExecutable(installRoot),
            Path.Combine(installRoot, InstallationPaths.ApplicationExecutableName),
        ];

        string? executablePath = candidates.FirstOrDefault(File.Exists);
        string? version = executablePath is null
            ? null
            : FileVersionInfo.GetVersionInfo(executablePath).FileVersion;
        return string.IsNullOrWhiteSpace(version) ? "未知" : version;
    }
}
