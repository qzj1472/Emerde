using System.IO.Compression;
using Emerde.Installer;

namespace Emerde.Tests;

public sealed class InstallationServiceTests
{
    [Fact]
    public async Task InstallAndUninstallUseOwnedDirectoryTree()
    {
        string testRoot = CreateTemporaryDirectory();
        string installRoot = Path.Combine(testRoot, "CustomRoot");
        string userDataDirectory = Path.Combine(testRoot, "UserData");
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(userDataDirectory);
        await File.WriteAllTextAsync(Path.Combine(installRoot, "unrelated.txt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(userDataDirectory, "config.yaml"), "keep");
        TestInstallationPlatform platform = new(userDataDirectory);
        InstallationService service = new(CreatePayload(), platform);

        try
        {
            InstallationInfo installation = await service.InstallAsync(
                new InstallationRequest(installRoot, true, true),
                InstallationOperation.Install,
                new Progress<InstallationProgress>());

            Assert.True(File.Exists(Path.Combine(installRoot, "bin", "Emerde.exe")));
            Assert.True(File.Exists(Path.Combine(installRoot, "runtime", "shared", "library.dll")));
            Assert.True(File.Exists(Path.Combine(installRoot, "maintenance", "Emerde.Uninstaller.exe")));
            Assert.True(File.Exists(Path.Combine(installRoot, "maintenance", "install-state.json")));
            Assert.True(File.Exists(Path.Combine(installRoot, "maintenance", "repair-state.json")));
            Assert.Null(InstallationRegistry.ReadUpgradeNotice(installRoot));
            Assert.Contains(
                InstallationRegistry.ReadRepairState(installRoot)!.Files,
                file => string.Equals(file.RelativePath, "runtime/shared/library.dll", StringComparison.OrdinalIgnoreCase));
            Assert.True(platform.ShortcutsApplied);
            Assert.True(platform.AutoStartEnabled);
            Assert.True(platform.InstallationInfoWritten);

            await service.UninstallAsync(installation, keepUserData: true, new Progress<InstallationProgress>());

            Assert.False(Directory.Exists(Path.Combine(installRoot, "bin")));
            Assert.False(Directory.Exists(Path.Combine(installRoot, "runtime")));
            Assert.False(Directory.Exists(Path.Combine(installRoot, "maintenance")));
            Assert.True(File.Exists(Path.Combine(installRoot, "unrelated.txt")));
            Assert.True(File.Exists(Path.Combine(userDataDirectory, "config.yaml")));
            Assert.True(platform.ShortcutsRemoved);
            Assert.False(platform.AutoStartEnabled);
            Assert.True(platform.InstallationInfoRemoved);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RepairRestoresDamagedFilesAndRemovesStaleFiles()
    {
        string testRoot = CreateTemporaryDirectory();
        string installRoot = Path.Combine(testRoot, "InstallRoot");
        string userDataDirectory = Path.Combine(testRoot, "UserData");
        TestInstallationPlatform platform = new(userDataDirectory);
        InstallationService service = new(CreatePayload(), platform);

        try
        {
            InstallationInfo installation = await service.InstallAsync(
                new InstallationRequest(installRoot, false, false),
                InstallationOperation.Install,
                new Progress<InstallationProgress>());
            await File.WriteAllTextAsync(Path.Combine(installRoot, "runtime", "shared", "library.dll"), "damaged");
            await File.WriteAllTextAsync(Path.Combine(installRoot, "runtime", "shared", "stale.dll"), "stale");

            await service.InstallAsync(
                new InstallationRequest(installRoot, false, false),
                InstallationOperation.Repair,
                new Progress<InstallationProgress>());

            Assert.Equal("library", await File.ReadAllTextAsync(Path.Combine(installRoot, "runtime", "shared", "library.dll")));
            Assert.False(File.Exists(Path.Combine(installRoot, "runtime", "shared", "stale.dll")));
            Assert.Equal(installation.Version, InstallationRegistry.ReadState(installRoot).Version);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RepairRestoresMissingSharedRuntimeDirectory()
    {
        string testRoot = CreateTemporaryDirectory();
        string installRoot = Path.Combine(testRoot, "InstallRoot");
        string userDataDirectory = Path.Combine(testRoot, "UserData");
        TestInstallationPlatform platform = new(userDataDirectory);
        InstallationService service = new(CreatePayload(), platform);

        try
        {
            await service.InstallAsync(
                new InstallationRequest(installRoot, false, false),
                InstallationOperation.Install,
                new Progress<InstallationProgress>());
            Directory.Delete(Path.Combine(installRoot, "runtime"), recursive: true);

            await service.InstallAsync(
                new InstallationRequest(installRoot, false, false),
                InstallationOperation.Repair,
                new Progress<InstallationProgress>());

            Assert.Equal(
                "library",
                await File.ReadAllTextAsync(Path.Combine(installRoot, "runtime", "shared", "library.dll")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RepairFailureRestoresChangedAndRemovedFiles()
    {
        string testRoot = CreateTemporaryDirectory();
        string installRoot = Path.Combine(testRoot, "InstallRoot");
        string userDataDirectory = Path.Combine(testRoot, "UserData");
        TestInstallationPlatform platform = new(userDataDirectory);
        InstallationService service = new(CreatePayload(), platform);

        try
        {
            await service.InstallAsync(
                new InstallationRequest(installRoot, true, true),
                InstallationOperation.Install,
                new Progress<InstallationProgress>());
            string damagedPath = Path.Combine(installRoot, "runtime", "shared", "library.dll");
            string stalePath = Path.Combine(installRoot, "runtime", "shared", "stale.dll");
            await File.WriteAllTextAsync(damagedPath, "damaged");
            await File.WriteAllTextAsync(stalePath, "stale");
            string stateBefore = await File.ReadAllTextAsync(InstallationPaths.StateFile(installRoot));
            string repairStateBefore = await File.ReadAllTextAsync(InstallationPaths.RepairStateFile(installRoot));
            platform.FailNextInstallationInfoWrite = true;

            await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(
                new InstallationRequest(installRoot, false, false),
                InstallationOperation.Repair,
                new Progress<InstallationProgress>()));

            Assert.Equal("damaged", await File.ReadAllTextAsync(damagedPath));
            Assert.Equal("stale", await File.ReadAllTextAsync(stalePath));
            Assert.Equal(stateBefore, await File.ReadAllTextAsync(InstallationPaths.StateFile(installRoot)));
            Assert.Equal(repairStateBefore, await File.ReadAllTextAsync(InstallationPaths.RepairStateFile(installRoot)));
            Assert.True(platform.ShortcutsEnabled);
            Assert.True(platform.AutoStartEnabled);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpgradeFailureRestoresPreviousFilesAndPlatformState()
    {
        string testRoot = CreateTemporaryDirectory();
        string installRoot = Path.Combine(testRoot, "InstallRoot");
        string userDataDirectory = Path.Combine(testRoot, "UserData");
        TestInstallationPlatform platform = new(userDataDirectory);
        InstallationService initialService = new(CreatePayload("old-application", "old-library"), platform);

        try
        {
            await initialService.InstallAsync(
                new InstallationRequest(installRoot, true, true),
                InstallationOperation.Install,
                new Progress<InstallationProgress>());
            string stateBefore = await File.ReadAllTextAsync(InstallationPaths.StateFile(installRoot));
            string repairStateBefore = await File.ReadAllTextAsync(InstallationPaths.RepairStateFile(installRoot));
            platform.FailNextInstallationInfoWrite = true;
            InstallationService upgradeService = new(CreatePayload("new-application", "new-library"), platform);

            await Assert.ThrowsAsync<IOException>(() => upgradeService.InstallAsync(
                new InstallationRequest(installRoot, false, false),
                InstallationOperation.Upgrade,
                new Progress<InstallationProgress>()));

            Assert.Equal("old-application", await File.ReadAllTextAsync(Path.Combine(installRoot, "bin", "Emerde.exe")));
            Assert.Equal("old-library", await File.ReadAllTextAsync(Path.Combine(installRoot, "runtime", "shared", "library.dll")));
            Assert.Equal(stateBefore, await File.ReadAllTextAsync(InstallationPaths.StateFile(installRoot)));
            Assert.Equal(repairStateBefore, await File.ReadAllTextAsync(InstallationPaths.RepairStateFile(installRoot)));
            Assert.True(platform.ShortcutsEnabled);
            Assert.True(platform.AutoStartEnabled);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpgradeRemovesLegacyLayoutAndPreservesUnrelatedFiles()
    {
        string testRoot = CreateTemporaryDirectory();
        string installRoot = Path.Combine(testRoot, "InstallRoot");
        string userDataDirectory = Path.Combine(testRoot, "UserData");
        Directory.CreateDirectory(Path.Combine(installRoot, "ffmpeg"));
        Directory.CreateDirectory(Path.Combine(installRoot, "downloads"));
        await File.WriteAllTextAsync(Path.Combine(installRoot, "Emerde.exe"), "legacy");
        await File.WriteAllTextAsync(Path.Combine(installRoot, "libSkiaSharp.dll"), "legacy");
        await File.WriteAllTextAsync(Path.Combine(installRoot, "ffmpeg", "avcodec.dll"), "legacy");
        await File.WriteAllTextAsync(Path.Combine(installRoot, "downloads", "recording.mp4"), "keep");
        await File.WriteAllTextAsync(Path.Combine(installRoot, "custom.txt"), "keep");
        InstallationRegistry.WriteState(new InstallationState(installRoot, false, false, "1.6.6.0"));
        TestInstallationPlatform platform = new(userDataDirectory);
        InstallationService service = new(CreatePayload(), platform);

        try
        {
            await service.InstallAsync(
                new InstallationRequest(installRoot, false, false),
                InstallationOperation.Upgrade,
                new Progress<InstallationProgress>());

            Assert.False(File.Exists(Path.Combine(installRoot, "Emerde.exe")));
            Assert.False(File.Exists(Path.Combine(installRoot, "libSkiaSharp.dll")));
            Assert.False(Directory.Exists(Path.Combine(installRoot, "ffmpeg")));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(installRoot, "downloads", "recording.mp4")));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(installRoot, "custom.txt")));
            UpgradeNoticeState? notice = InstallationRegistry.ReadUpgradeNotice(installRoot);
            Assert.NotNull(notice);
            Assert.True(notice.Pending);
            Assert.False(string.IsNullOrWhiteSpace(notice.NoticeId));
            Assert.Equal("1.6.6.0", notice.PreviousVersion);
            Assert.Equal(InstallationPaths.ProductVersion, notice.Version);

            string firstNoticeId = notice.NoticeId!;
            await service.InstallAsync(
                new InstallationRequest(installRoot, false, false),
                InstallationOperation.Upgrade,
                new Progress<InstallationProgress>());

            UpgradeNoticeState? repeatedVersionNotice = InstallationRegistry.ReadUpgradeNotice(installRoot);
            Assert.NotNull(repeatedVersionNotice);
            Assert.True(repeatedVersionNotice.Pending);
            Assert.Equal(InstallationPaths.ProductVersion, repeatedVersionNotice.PreviousVersion);
            Assert.Equal(InstallationPaths.ProductVersion, repeatedVersionNotice.Version);
            Assert.NotEqual(firstNoticeId, repeatedVersionNotice.NoticeId);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UninstallRemovesUserDataWhenNotPreserved()
    {
        string testRoot = CreateTemporaryDirectory();
        string installRoot = Path.Combine(testRoot, "InstallRoot");
        string userDataDirectory = Path.Combine(testRoot, "UserData");
        Directory.CreateDirectory(userDataDirectory);
        await File.WriteAllTextAsync(Path.Combine(userDataDirectory, "config.yaml"), "remove");
        TestInstallationPlatform platform = new(userDataDirectory);
        InstallationService service = new(CreatePayload(), platform);

        try
        {
            InstallationInfo installation = await service.InstallAsync(
                new InstallationRequest(installRoot, false, false),
                InstallationOperation.Install,
                new Progress<InstallationProgress>());
            await service.UninstallAsync(installation, keepUserData: false, new Progress<InstallationProgress>());

            Assert.False(Directory.Exists(installRoot));
            Assert.False(Directory.Exists(userDataDirectory));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static InstallerPayload CreatePayload(
        string applicationContent = "application",
        string libraryContent = "library")
    {
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry executable = archive.CreateEntry("bin/Emerde.exe");
            using (StreamWriter executableWriter = new(executable.Open()))
            {
                executableWriter.Write(applicationContent);
            }
            ZipArchiveEntry library = archive.CreateEntry("runtime/shared/library.dll");
            using (StreamWriter libraryWriter = new(library.Open()))
            {
                libraryWriter.Write(libraryContent);
            }
            ZipArchiveEntry uninstaller = archive.CreateEntry("maintenance/Emerde.Uninstaller.exe");
            using (StreamWriter uninstallerWriter = new(uninstaller.Open()))
            {
                uninstallerWriter.Write("uninstaller");
            }
        }

        byte[] bytes = output.ToArray();
        return new InstallerPayload(() => new MemoryStream(bytes, writable: false));
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "EmerdeInstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestInstallationPlatform(string userDataDirectory) : IInstallationPlatform
    {
        public string UserDataDirectory => userDataDirectory;

        public bool ShortcutsApplied { get; private set; }

        public bool ShortcutsEnabled { get; private set; }

        public bool ShortcutsRemoved { get; private set; }

        public bool AutoStartEnabled { get; private set; }

        public bool InstallationInfoWritten { get; private set; }

        public bool InstallationInfoRemoved { get; private set; }

        public bool FailNextInstallationInfoWrite { get; set; }

        public void ApplyShortcuts(string installRoot, bool createExternalShortcuts)
        {
            ShortcutsApplied = true;
            ShortcutsEnabled = createExternalShortcuts;
        }

        public void RemoveShortcuts(string installRoot)
        {
            ShortcutsRemoved = true;
            ShortcutsEnabled = false;
        }

        public void SetAutoStart(string installRoot, bool enabled)
        {
            AutoStartEnabled = enabled;
        }

        public void WriteInstallationInfo(InstallationState state, long estimatedSizeBytes)
        {
            if (FailNextInstallationInfoWrite)
            {
                FailNextInstallationInfoWrite = false;
                throw new IOException("installation info failure");
            }

            InstallationInfoWritten = true;
        }

        public void RemoveInstallationInfo()
        {
            InstallationInfoRemoved = true;
        }

        public void EnsureAvailableSpace(string installRoot, long payloadSizeBytes)
        {
        }

        public void ApplyTransparentCompression(string installRoot)
        {
        }
    }
}
