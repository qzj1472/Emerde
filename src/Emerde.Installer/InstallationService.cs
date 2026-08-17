using System.IO;

namespace Emerde.Installer;

internal sealed class InstallationService
{
    private static readonly string[] LegacyRootFiles =
    [
        "COPYRIGHT",
        "D3DCompiler_47_cor3.dll",
        "Emerde.deps.json",
        "Emerde.dll",
        "Emerde.exe",
        "Emerde.pdb",
        "Emerde.runtimeconfig.json",
        "libSkiaSharp.dll",
        "LICENSE",
        "PenImc_cor3.dll",
        "PresentationNative_cor3.dll",
        "THIRD_PARTY_NOTICES.md",
        "vcruntime140_cor3.dll",
        "WebView2Loader.dll",
        "wpfgfx_cor3.dll",
    ];
    private static readonly string[] LegacyRootDirectories = ["ffmpeg", "libvlc", "licenses", "runtimes"];
    private readonly InstallerPayload payload;
    private readonly IInstallationPlatform platform;
    private readonly Action<string> committedBackupCleaner;

    public InstallationService(InstallerPayload payload)
        : this(payload, new WindowsInstallationPlatform())
    {
    }

    internal InstallationService(
        InstallerPayload payload,
        IInstallationPlatform platform,
        Action<string>? committedBackupCleaner = null)
    {
        this.payload = payload;
        this.platform = platform;
        this.committedBackupCleaner = committedBackupCleaner ?? DeleteDirectoryIfPresent;
    }

    public async Task<InstallationInfo> InstallAsync(
        InstallationRequest request,
        InstallationOperation operation,
        IProgress<InstallationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        string installRoot = InstallationPaths.NormalizeInstallRoot(request.InstallRoot);
        string stagingDirectory = Path.Combine(installRoot, $".emerde-install-{Guid.NewGuid():N}");
        string backupDirectory = Path.Combine(installRoot, $".emerde-backup-{Guid.NewGuid():N}");
        string[] managedDirectories =
        [
            InstallationPaths.BinaryDirectoryName,
            InstallationPaths.NativeDirectoryName,
            InstallationPaths.ResourcesDirectoryName,
            InstallationPaths.LicensesDirectoryName,
            InstallationPaths.MaintenanceDirectoryName,
            InstallationPaths.RuntimeDirectoryName,
        ];
        List<string> movedDirectories = [];
        List<string> activatedDirectories = [];
        List<string> movedLegacyEntries = [];
        bool shortcutsAttempted = false;
        bool autoStartAttempted = false;
        bool installationInfoAttempted = false;

        platform.EnsureAvailableSpace(installRoot, payload.GetUncompressedLength());
        Directory.CreateDirectory(installRoot);
        bool hadExistingInstallation = File.Exists(InstallationPaths.BinaryExecutable(installRoot))
            || File.Exists(Path.Combine(installRoot, InstallationPaths.ApplicationExecutableName));
        InstallationState previousState = InstallationRegistry.ReadState(installRoot);
        string previousVersion = operation == InstallationOperation.Upgrade
            ? previousState.Version
            : string.Empty;
        progress.Report(new InstallationProgress(2, GetPreparingStatus(operation)));

        if (operation == InstallationOperation.Repair
            && InstallationRegistry.ReadRepairState(installRoot) is not null)
        {
            return await RepairAsync(request, installRoot, progress, cancellationToken);
        }

        try
        {
            await payload.ExtractAsync(stagingDirectory, progress, cancellationToken);

            string stagedBinaryDirectory = Path.Combine(stagingDirectory, InstallationPaths.BinaryDirectoryName);
            if (!File.Exists(Path.Combine(stagedBinaryDirectory, InstallationPaths.ApplicationExecutableName)))
            {
                throw new InvalidDataException("安装负载中缺少 bin\\Emerde.exe。");
            }

            progress.Report(new InstallationProgress(78, "正在替换程序文件..."));

            MoveLegacyLayoutToBackup(installRoot, backupDirectory, movedLegacyEntries);

            foreach (string directoryName in managedDirectories)
            {
                string existingDirectory = Path.Combine(installRoot, directoryName);
                string stagedDirectory = Path.Combine(stagingDirectory, directoryName);
                string backupPath = Path.Combine(backupDirectory, directoryName);

                if (Directory.Exists(existingDirectory))
                {
                    Directory.CreateDirectory(backupDirectory);
                    Directory.Move(existingDirectory, backupPath);
                    movedDirectories.Add(directoryName);
                }

                if (Directory.Exists(stagedDirectory))
                {
                    Directory.Move(stagedDirectory, existingDirectory);
                    activatedDirectories.Add(directoryName);
                }
            }

            TryDeleteDirectory(stagingDirectory);

            progress.Report(new InstallationProgress(84, "正在写入维护组件..."));
            InstallationState state = new(
                installRoot,
                request.CreateShortcuts,
                request.AutoStart,
                InstallationPaths.ProductVersion);
            InstallationRegistry.WriteState(state);
            InstallationRegistry.WriteRepairState(installRoot, state.Version);
            if (operation == InstallationOperation.Upgrade)
            {
                InstallationRegistry.WriteUpgradeNotice(installRoot, previousVersion, state.Version);
            }

            progress.Report(new InstallationProgress(90, "正在创建快捷方式..."));
            shortcutsAttempted = true;
            platform.ApplyShortcuts(installRoot, request.CreateShortcuts);
            autoStartAttempted = true;
            platform.SetAutoStart(installRoot, request.AutoStart);

            progress.Report(new InstallationProgress(96, "正在优化磁盘占用..."));
            platform.ApplyTransparentCompression(installRoot);

            progress.Report(new InstallationProgress(98, "正在注册维护信息..."));
            installationInfoAttempted = true;
            platform.WriteInstallationInfo(state, GetDirectorySize(installRoot));
            CleanupCommittedBackup(backupDirectory);

            progress.Report(new InstallationProgress(100, "已完成"));
            return new InstallationInfo(installRoot, state.Version, state);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);

            foreach (string directoryName in activatedDirectories)
            {
                TryDeleteDirectory(Path.Combine(installRoot, directoryName));
            }

            foreach (string directoryName in movedDirectories)
            {
                string backupPath = Path.Combine(backupDirectory, directoryName);
                string restorePath = Path.Combine(installRoot, directoryName);
                if (Directory.Exists(backupPath) && !Directory.Exists(restorePath))
                {
                    Directory.Move(backupPath, restorePath);
                }
            }

            RestoreLegacyLayout(installRoot, backupDirectory, movedLegacyEntries);

            TryDeleteDirectory(backupDirectory);
            RestorePlatformState(
                installRoot,
                hadExistingInstallation,
                previousState,
                shortcutsAttempted,
                autoStartAttempted,
                installationInfoAttempted);

            throw;
        }
    }

    private async Task<InstallationInfo> RepairAsync(
        InstallationRequest request,
        string installRoot,
        IProgress<InstallationProgress> progress,
        CancellationToken cancellationToken)
    {
        string stagingDirectory = Path.Combine(installRoot, $".emerde-repair-{Guid.NewGuid():N}");
        string backupDirectory = Path.Combine(installRoot, $".emerde-repair-backup-{Guid.NewGuid():N}");
        RepairState repairState = InstallationRegistry.ReadRepairState(installRoot)
            ?? throw new InvalidDataException("Repair state is missing.");
        if (!string.Equals(repairState.Version, InstallationPaths.ProductVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("修复需要使用与已安装版本相同的安装程序；不同版本请使用升级。");
        }

        HashSet<string> expectedFiles = new(
            repairState.Files.Select(file => NormalizeRelativePath(file.RelativePath)),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> backedUpFiles = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> createdFiles = new(StringComparer.OrdinalIgnoreCase);
        InstallationState previousState = InstallationRegistry.ReadState(installRoot);
        bool shortcutsAttempted = false;
        bool autoStartAttempted = false;
        bool installationInfoAttempted = false;

        try
        {
            await payload.ExtractAsync(stagingDirectory, progress, cancellationToken);
            progress.Report(new InstallationProgress(78, "正在检查程序文件..."));
            BackupRepairTarget(installRoot, backupDirectory, InstallationPaths.StateFile(installRoot), backedUpFiles, createdFiles);
            BackupRepairTarget(installRoot, backupDirectory, InstallationPaths.RepairStateFile(installRoot), backedUpFiles, createdFiles);

            int checkedFiles = 0;
            foreach (RepairFileState file in repairState.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = NormalizeRelativePath(file.RelativePath);
                string targetPath = GetSafePath(installRoot, relativePath);
                string stagedPath = GetSafePath(stagingDirectory, relativePath);

                if (!File.Exists(stagedPath))
                {
                    throw new InvalidDataException($"Repair payload is missing {relativePath}.");
                }

                if (!IsHealthy(stagedPath, file))
                {
                    throw new InvalidDataException($"Repair payload does not match {relativePath}.");
                }

                if (!IsHealthy(targetPath, file))
                {
                    BackupRepairTarget(installRoot, backupDirectory, targetPath, backedUpFiles, createdFiles);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(stagedPath, targetPath, overwrite: true);
                }

                checkedFiles++;
                progress.Report(new InstallationProgress(
                    78 + Math.Min(12, checkedFiles * 12 / Math.Max(1, repairState.Files.Count)),
                    "正在检查程序文件..."));
            }

            RemoveStaleManagedFiles(
                installRoot,
                expectedFiles,
                filePath => BackupRepairTarget(
                    installRoot,
                    backupDirectory,
                    filePath,
                    backedUpFiles,
                    createdFiles));
            TryDeleteDirectory(stagingDirectory);

            InstallationState state = new(
                installRoot,
                request.CreateShortcuts,
                request.AutoStart,
                previousState.Version);
            InstallationRegistry.WriteState(state);
            InstallationRegistry.WriteRepairState(installRoot, state.Version);
            shortcutsAttempted = true;
            platform.ApplyShortcuts(installRoot, request.CreateShortcuts);
            autoStartAttempted = true;
            platform.SetAutoStart(installRoot, request.AutoStart);
            progress.Report(new InstallationProgress(96, "正在优化磁盘占用..."));
            platform.ApplyTransparentCompression(installRoot);
            installationInfoAttempted = true;
            platform.WriteInstallationInfo(state, GetDirectorySize(installRoot));
            CleanupCommittedBackup(backupDirectory);
            progress.Report(new InstallationProgress(100, "已完成"));
            return new InstallationInfo(installRoot, state.Version, state);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            RestoreRepairTargets(installRoot, backupDirectory, backedUpFiles, createdFiles);
            TryDeleteDirectory(backupDirectory);
            RestorePlatformState(
                installRoot,
                true,
                previousState,
                shortcutsAttempted,
                autoStartAttempted,
                installationInfoAttempted);
            throw;
        }
    }

    public Task UninstallAsync(
        InstallationInfo installation,
        bool keepUserData,
        IProgress<InstallationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string installRoot = InstallationPaths.NormalizeInstallRoot(installation.InstallRoot);

            progress.Report(new InstallationProgress(8, "正在移除快捷方式..."));
            platform.RemoveShortcuts(installRoot);
            platform.SetAutoStart(installRoot, enabled: false);

            progress.Report(new InstallationProgress(22, "正在移除系统注册信息..."));
            platform.RemoveInstallationInfo();

            progress.Report(new InstallationProgress(36, "正在移除程序文件..."));
            DeleteOwnedDirectory(installRoot, InstallationPaths.BinaryDirectoryName);
            DeleteOwnedDirectory(installRoot, InstallationPaths.NativeDirectoryName);
            DeleteOwnedDirectory(installRoot, InstallationPaths.ResourcesDirectoryName);
            DeleteOwnedDirectory(installRoot, InstallationPaths.LicensesDirectoryName);
            DeleteOwnedDirectory(installRoot, InstallationPaths.RuntimeDirectoryName);
            DeleteTransientDirectories(installRoot);
            DeleteLegacyLayout(installRoot);

            progress.Report(new InstallationProgress(72, "正在移除维护组件..."));
            DeleteOwnedDirectory(installRoot, InstallationPaths.MaintenanceDirectoryName);
            DeleteOwnedFile(installRoot, "Emerde.lnk");
            DeleteOwnedFile(installRoot, "Uninstall.lnk");
            DeleteOwnedFile(installRoot, "Uninstall Emerde.lnk");
            DeleteOwnedFile(installRoot, "卸载 Emerde.lnk");

            if (!keepUserData)
            {
                progress.Report(new InstallationProgress(86, "正在移除用户数据..."));
                TryDeleteDirectory(platform.UserDataDirectory);
            }

            TryDeleteEmptyDirectory(installRoot);
            progress.Report(new InstallationProgress(100, "已完成"));
        }, cancellationToken);
    }

    private static string GetPreparingStatus(InstallationOperation operation)
    {
        return operation switch
        {
            InstallationOperation.Upgrade => "正在准备升级...",
            InstallationOperation.Repair => "正在准备修复...",
            _ => "正在准备安装...",
        };
    }

    private static long GetDirectorySize(string path)
    {
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(filePath => !IsTransientInstallerPath(path, filePath))
            .Sum(filePath => new FileInfo(filePath).Length);
    }

    private static bool IsTransientInstallerPath(string installRoot, string filePath)
    {
        string relativePath = Path.GetRelativePath(installRoot, filePath);
        string firstSegment = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return IsTransientInstallerDirectory(firstSegment);
    }

    private static void DeleteOwnedDirectory(string installRoot, string directoryName)
    {
        string path = Path.Combine(installRoot, directoryName);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteOwnedFile(string installRoot, string fileName)
    {
        string path = Path.Combine(installRoot, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool IsHealthy(string path, RepairFileState expected)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        FileInfo info = new(path);
        if (info.Length != expected.Length)
        {
            return false;
        }

        using FileStream stream = File.OpenRead(path);
        return string.Equals(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)),
            expected.Sha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveStaleManagedFiles(
        string installRoot,
        HashSet<string> expectedFiles,
        Action<string>? beforeDelete = null)
    {
        foreach (string component in new[]
        {
            InstallationPaths.BinaryDirectoryName,
            InstallationPaths.NativeDirectoryName,
            InstallationPaths.ResourcesDirectoryName,
            InstallationPaths.LicensesDirectoryName,
            InstallationPaths.MaintenanceDirectoryName,
            InstallationPaths.RuntimeDirectoryName,
        })
        {
            string componentPath = Path.Combine(installRoot, component);
            if (!Directory.Exists(componentPath))
            {
                continue;
            }

            foreach (string filePath in Directory.EnumerateFiles(componentPath, "*", SearchOption.AllDirectories).ToArray())
            {
                string fileName = Path.GetFileName(filePath);
                string relativePath = NormalizeRelativePath(Path.GetRelativePath(installRoot, filePath));
                if (!InstallationRegistry.IsMaintenanceStateFile(fileName)
                    && !expectedFiles.Contains(relativePath))
                {
                    beforeDelete?.Invoke(filePath);
                    File.Delete(filePath);
                }
            }
        }
    }

    private static void BackupRepairTarget(
        string installRoot,
        string backupDirectory,
        string targetPath,
        HashSet<string> backedUpFiles,
        HashSet<string> createdFiles)
    {
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(installRoot, targetPath));
        if (backedUpFiles.Contains(relativePath) || createdFiles.Contains(relativePath))
        {
            return;
        }

        if (!File.Exists(targetPath))
        {
            createdFiles.Add(relativePath);
            return;
        }

        string backupPath = GetSafePath(backupDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(targetPath, backupPath, overwrite: false);
        backedUpFiles.Add(relativePath);
    }

    private static void RestoreRepairTargets(
        string installRoot,
        string backupDirectory,
        IEnumerable<string> backedUpFiles,
        IEnumerable<string> createdFiles)
    {
        foreach (string relativePath in createdFiles)
        {
            string targetPath = GetSafePath(installRoot, relativePath);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }

        foreach (string relativePath in backedUpFiles)
        {
            string backupPath = GetSafePath(backupDirectory, relativePath);
            string targetPath = GetSafePath(installRoot, relativePath);
            if (!File.Exists(backupPath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(backupPath, targetPath, overwrite: true);
        }
    }

    private void RestorePlatformState(
        string installRoot,
        bool hadExistingInstallation,
        InstallationState previousState,
        bool shortcutsAttempted,
        bool autoStartAttempted,
        bool installationInfoAttempted)
    {
        if (shortcutsAttempted)
        {
            TryRestorePlatformState(() =>
            {
                if (hadExistingInstallation)
                {
                    platform.ApplyShortcuts(installRoot, previousState.CreateShortcuts);
                }
                else
                {
                    platform.RemoveShortcuts(installRoot);
                }
            });
        }

        if (autoStartAttempted)
        {
            TryRestorePlatformState(() => platform.SetAutoStart(
                installRoot,
                hadExistingInstallation && previousState.AutoStart));
        }

        if (installationInfoAttempted)
        {
            TryRestorePlatformState(() =>
            {
                if (hadExistingInstallation)
                {
                    platform.WriteInstallationInfo(previousState, GetDirectorySize(installRoot));
                }
                else
                {
                    platform.RemoveInstallationInfo();
                }
            });
        }
    }

    private static void TryRestorePlatformState(Action restore)
    {
        try
        {
            restore();
        }
        catch
        {
        }
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string GetSafePath(string root, string relativePath)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Invalid repair path: {relativePath}");
        }

        return fullPath;
    }

    private static void DeleteTransientDirectories(string installRoot)
    {
        if (!Directory.Exists(installRoot))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(installRoot))
        {
            string name = Path.GetFileName(directory);
            if (IsTransientInstallerDirectory(name))
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    private static bool IsTransientInstallerDirectory(string name)
    {
        return name.StartsWith(".emerde-install-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".emerde-backup-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".emerde-repair-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".emerde-repair-backup-", StringComparison.OrdinalIgnoreCase);
    }

    private static void MoveLegacyLayoutToBackup(
        string installRoot,
        string backupDirectory,
        List<string> movedEntries)
    {
        if (!File.Exists(Path.Combine(installRoot, InstallationPaths.ApplicationExecutableName)))
        {
            return;
        }

        string legacyBackup = Path.Combine(backupDirectory, "legacy");
        foreach (string name in LegacyRootDirectories.Concat(LegacyRootFiles))
        {
            string source = Path.Combine(installRoot, name);
            if (!Directory.Exists(source) && !File.Exists(source))
            {
                continue;
            }

            Directory.CreateDirectory(legacyBackup);
            string destination = Path.Combine(legacyBackup, name);
            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }
            else
            {
                File.Move(source, destination);
            }

            movedEntries.Add(name);
        }
    }

    private static void RestoreLegacyLayout(
        string installRoot,
        string backupDirectory,
        IEnumerable<string> movedEntries)
    {
        string legacyBackup = Path.Combine(backupDirectory, "legacy");
        foreach (string name in movedEntries.Reverse())
        {
            string source = Path.Combine(legacyBackup, name);
            string destination = Path.Combine(installRoot, name);
            if (Directory.Exists(source) && !Directory.Exists(destination))
            {
                Directory.Move(source, destination);
            }
            else if (File.Exists(source) && !File.Exists(destination))
            {
                File.Move(source, destination);
            }
        }
    }

    private static void DeleteLegacyLayout(string installRoot)
    {
        if (!File.Exists(Path.Combine(installRoot, InstallationPaths.ApplicationExecutableName)))
        {
            return;
        }

        foreach (string name in LegacyRootDirectories)
        {
            DeleteOwnedDirectory(installRoot, name);
        }

        foreach (string name in LegacyRootFiles)
        {
            DeleteOwnedFile(installRoot, name);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void CleanupCommittedBackup(string path)
    {
        try
        {
            committedBackupCleaner(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }
}
