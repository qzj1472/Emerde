using System.Text.Json;
using Emerde.Core;

namespace Emerde.Tests;

public sealed class UpgradeNoticeServiceTests
{
    [Fact]
    public void TryGetInstallRoot_OnlyAcceptsInstalledBinDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeUpgradeNoticeTests", Guid.NewGuid().ToString("N"));
        string bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);

        try
        {
            Assert.Equal(root, UpgradeNoticeService.TryGetInstallRoot(bin));
            Assert.Equal(root, UpgradeNoticeService.TryGetInstallRoot(bin + Path.DirectorySeparatorChar));
            Assert.Null(UpgradeNoticeService.TryGetInstallRoot(Path.Combine(root, "Debug")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryReadPendingNotice_RequiresPendingCurrentFourPartVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeUpgradeNoticeTests", Guid.NewGuid().ToString("N"));
        string bin = Path.Combine(root, "bin");
        string maintenance = Path.Combine(root, "maintenance");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(maintenance);
        string noticePath = Path.Combine(maintenance, "upgrade-notice.json");
        File.WriteAllText(noticePath, JsonSerializer.Serialize(new
        {
            Version = "1.6.7.1",
            PreviousVersion = "1.6.7.0",
            InstalledAtUtc = DateTime.UtcNow,
            Pending = true,
        }));

        try
        {
            UpgradeNoticeState? notice = UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7.1", string.Empty);

            Assert.NotNull(notice);
            Assert.Equal("1.6.7.1", notice.Version);
            Assert.Equal("1.6.7.0", notice.PreviousVersion);
            Assert.Null(UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7.0", string.Empty));
            Assert.Null(UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7.1", "1.6.7.1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReleaseNotesCatalog_UsesFourPartVersions()
    {
        Assert.Contains(ReleaseNotesCatalog.Entries, entry => entry.Version == "1.6.7.0");
        Assert.Equal("1.6.7.2", ReleaseNotesCatalog.GetEntry("1.6.7.2").Version);
    }
}
