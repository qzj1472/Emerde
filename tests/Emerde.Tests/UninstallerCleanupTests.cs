using UninstallerProgram = Emerde.Uninstaller.Program;
using CleanupProgram = Emerde.Cleanup.Program;

namespace Emerde.Tests;

public sealed class UninstallerCleanupTests
{
    [Fact]
    public void ShutdownRequestSignalsExistingApplicationEvent()
    {
        string eventName = $"Emerde.Tests.Shutdown.{Guid.NewGuid():N}";
        using EventWaitHandle handle = new(false, EventResetMode.AutoReset, eventName);

        Assert.True(UninstallerProgram.RequestApplicationShutdown(eventName));
        Assert.True(handle.WaitOne(1000));
    }

    [Fact]
    public async Task DeferredCleanupRemovesManagedDirectoriesAndEmptyRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeUninstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "maintenance"));
        Directory.CreateDirectory(Path.Combine(root, "runtime", "shared"));
        await File.WriteAllTextAsync(Path.Combine(root, "maintenance", "Emerde.Uninstaller.exe"), "uninstaller");
        await File.WriteAllTextAsync(Path.Combine(root, "runtime", "shared", "runtime.dll"), "runtime");

        try
        {
            Assert.True(CleanupProgram.DeleteManagedDirectories(root));

            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeferredCleanupPreservesUnknownRootFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeUninstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "maintenance"));
        Directory.CreateDirectory(Path.Combine(root, "runtime"));
        await File.WriteAllTextAsync(Path.Combine(root, "maintenance", "Emerde.Uninstaller.exe"), "uninstaller");
        string unknownFile = Path.Combine(root, "custom.txt");
        await File.WriteAllTextAsync(unknownFile, "keep");

        try
        {
            Assert.True(CleanupProgram.DeleteManagedDirectories(root));

            Assert.True(Directory.Exists(root));
            Assert.Equal("keep", await File.ReadAllTextAsync(unknownFile));
            Assert.False(Directory.Exists(Path.Combine(root, "maintenance")));
            Assert.False(Directory.Exists(Path.Combine(root, "runtime")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DeferredCleanupRejectsDirectoryWithoutInstallationMarker()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeUninstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "maintenance"));
        Directory.CreateDirectory(Path.Combine(root, "runtime"));

        try
        {
            Assert.Throws<InvalidDataException>(() => CleanupProgram.DeleteManagedDirectories(root));
            Assert.True(Directory.Exists(Path.Combine(root, "maintenance")));
            Assert.True(Directory.Exists(Path.Combine(root, "runtime")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
