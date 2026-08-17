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
            NoticeId = "upgrade-1",
            Version = "1.6.7.1",
            PreviousVersion = "1.6.7.0",
            InstalledAtUtc = DateTime.UtcNow,
            Pending = true,
        }));

        try
        {
            UpgradeNoticeState? notice = UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7.1", string.Empty);

            Assert.NotNull(notice);
            Assert.Equal("upgrade-1", notice.NoticeId);
            Assert.Equal("1.6.7.1", notice.Version);
            Assert.Equal("1.6.7.0", notice.PreviousVersion);
            Assert.Null(UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7.0", string.Empty));
            Assert.Null(UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7.1", "upgrade-1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryReadPendingNotice_AllowsAnotherUpgradeEventForSameVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeUpgradeNoticeTests", Guid.NewGuid().ToString("N"));
        string bin = Path.Combine(root, "bin");
        string maintenance = Path.Combine(root, "maintenance");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(maintenance);
        string noticePath = Path.Combine(maintenance, "upgrade-notice.json");
        File.WriteAllText(noticePath, JsonSerializer.Serialize(new
        {
            NoticeId = "upgrade-2",
            Version = "1.6.7.1",
            PreviousVersion = "1.6.7.1",
            InstalledAtUtc = DateTime.UtcNow,
            Pending = true,
        }));

        try
        {
            UpgradeNoticeState? notice = UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7.1", "upgrade-1");

            Assert.NotNull(notice);
            Assert.Equal("upgrade-2", notice.NoticeId);
            Assert.Equal("1.6.7.1", notice.PreviousVersion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryReadPendingNotice_UsesStableIdentityForLegacyNotice()
    {
        string root = Path.Combine(Path.GetTempPath(), "EmerdeUpgradeNoticeTests", Guid.NewGuid().ToString("N"));
        string bin = Path.Combine(root, "bin");
        string maintenance = Path.Combine(root, "maintenance");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(maintenance);
        string noticePath = Path.Combine(maintenance, "upgrade-notice.json");
        DateTime installedAtUtc = new(2026, 8, 13, 14, 18, 46, DateTimeKind.Utc);
        File.WriteAllText(noticePath, JsonSerializer.Serialize(new
        {
            Version = "1.6.7.1",
            PreviousVersion = "1.6.7.1",
            InstalledAtUtc = installedAtUtc,
            Pending = true,
        }));

        try
        {
            UpgradeNoticeState? firstRead = UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7.1", string.Empty);

            Assert.NotNull(firstRead);
            Assert.StartsWith("legacy:1.6.7.1:", firstRead.NoticeId);
            Assert.Null(UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7.1", firstRead.NoticeId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryReadDevelopmentNotice_UsesBuildIdentityAndDoesNotDependOnInstalledNoticeFile()
    {
        UpgradeNoticeState? notice = UpgradeNoticeService.TryReadDevelopmentNotice("1.6.7.1", "debug-build-2", string.Empty);

        Assert.NotNull(notice);
        Assert.Equal("debug:1.6.7.1:debug-build-2", notice.NoticeId);
        Assert.Equal(string.Empty, notice.PreviousVersion);
        Assert.Empty(notice.NoticePath);
        Assert.Null(UpgradeNoticeService.TryReadDevelopmentNotice("1.6.7.1", "debug-build-2", notice.NoticeId));
        Assert.NotNull(UpgradeNoticeService.TryReadDevelopmentNotice("1.6.7.1", "debug-build-3", notice.NoticeId));
    }

    [Theory]
    [InlineData(Wpf.Ui.Violeta.Controls.ContentDialogResult.None, false)]
    [InlineData(Wpf.Ui.Violeta.Controls.ContentDialogResult.Secondary, false)]
    [InlineData(Wpf.Ui.Violeta.Controls.ContentDialogResult.Primary, true)]
    public void UpgradeNoticeIsMarkedOnlyAfterAcknowledgement(
        Wpf.Ui.Violeta.Controls.ContentDialogResult result,
        bool expected)
    {
        Assert.Equal(expected, Emerde.Views.MainWindow.ShouldMarkUpgradeNoticeAcknowledgement(result));
    }

    [Fact]
    public void ReleaseNotesCatalog_UsesFourPartVersions()
    {
        Assert.Equal("1.6.7.2", ReleaseNotesCatalog.Entries[0].Version);
        Assert.Contains(ReleaseNotesCatalog.Entries, entry => entry.Version == "1.6.7.2");
        Assert.Contains(ReleaseNotesCatalog.Entries, entry => entry.Version == "1.6.7.1");
        Assert.Contains(ReleaseNotesCatalog.Entries, entry => entry.Version == "1.6.7.0");
        Assert.Equal("1.6.7.2", ReleaseNotesCatalog.GetEntry("1.6.7.2").Version);
    }

    [Fact]
    public void ReleaseNotes1672_MapsEveryLocalizedItemExactlyOnce()
    {
        ReleaseNoteEntry entry = ReleaseNotesCatalog.GetEntry("1.6.7.2");
        string[] items = entry.Sections.SelectMany(section => section.Items).ToArray();

        Assert.Equal([5, 9, 3, 5], entry.Sections.Select(section => section.Items.Count));
        Assert.Equal(22, items.Length);
        Assert.Equal(items.Length, items.Distinct(StringComparer.Ordinal).Count());
    }
}
