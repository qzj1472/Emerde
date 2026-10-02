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
    public void TryReadPendingNotice_RequiresPendingCurrentVersion()
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
            Version = "1.6.8",
            PreviousVersion = "1.6.7",
            InstalledAtUtc = DateTime.UtcNow,
            Pending = true,
        }));

        try
        {
            UpgradeNoticeState? notice = UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.8", string.Empty);

            Assert.NotNull(notice);
            Assert.Equal("upgrade-1", notice.NoticeId);
            Assert.Equal("1.6.8", notice.Version);
            Assert.Equal("1.6.7", notice.PreviousVersion);
            Assert.Null(UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.7", string.Empty));
            Assert.Null(UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.8", "upgrade-1"));
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
            Version = "1.6.8",
            PreviousVersion = "1.6.8",
            InstalledAtUtc = DateTime.UtcNow,
            Pending = true,
        }));

        try
        {
            UpgradeNoticeState? notice = UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.8", "upgrade-1");

            Assert.NotNull(notice);
            Assert.Equal("upgrade-2", notice.NoticeId);
            Assert.Equal("1.6.8", notice.PreviousVersion);
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
            Version = "1.6.8",
            PreviousVersion = "1.6.8",
            InstalledAtUtc = installedAtUtc,
            Pending = true,
        }));

        try
        {
            UpgradeNoticeState? firstRead = UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.8", string.Empty);

            Assert.NotNull(firstRead);
            Assert.StartsWith("legacy:1.6.8:", firstRead.NoticeId);
            Assert.Null(UpgradeNoticeService.TryReadPendingNotice(bin, "1.6.8", firstRead.NoticeId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryReadDevelopmentNotice_UsesBuildIdentityAndDoesNotDependOnInstalledNoticeFile()
    {
        UpgradeNoticeState? notice = UpgradeNoticeService.TryReadDevelopmentNotice("1.6.8", "debug-build-2", string.Empty);

        Assert.NotNull(notice);
        Assert.Equal("debug:1.6.8:debug-build-2", notice.NoticeId);
        Assert.Equal(string.Empty, notice.PreviousVersion);
        Assert.Empty(notice.NoticePath);
        Assert.Null(UpgradeNoticeService.TryReadDevelopmentNotice("1.6.8", "debug-build-2", notice.NoticeId));
        Assert.NotNull(UpgradeNoticeService.TryReadDevelopmentNotice("1.6.8", "debug-build-3", notice.NoticeId));
    }

    [Theory]
    [InlineData(Wpf.Ui.Violeta.Controls.ContentDialogResult.None, true)]
    [InlineData(Wpf.Ui.Violeta.Controls.ContentDialogResult.Secondary, true)]
    [InlineData(Wpf.Ui.Violeta.Controls.ContentDialogResult.Primary, true)]
    public void UpgradeNoticeIsMarkedAfterDialogCloses(
        Wpf.Ui.Violeta.Controls.ContentDialogResult result,
        bool expected)
    {
        Assert.Equal(expected, Emerde.Views.MainWindow.ShouldMarkUpgradeNoticeAsShown(result));
    }

    [Fact]
    public void ReleaseNotesCatalog_UsesNormalizedVersions()
    {
        Assert.Equal("1.7.1", ReleaseNotesCatalog.Entries[0].Version);
        Assert.Contains(ReleaseNotesCatalog.Entries, entry => entry.Version == "1.6.11");
        Assert.Contains(ReleaseNotesCatalog.Entries, entry => entry.Version == "1.6.10");
        Assert.Contains(ReleaseNotesCatalog.Entries, entry => entry.Version == "1.6.9");
        Assert.Contains(ReleaseNotesCatalog.Entries, entry => entry.Version == "1.6.8");
        Assert.Contains(ReleaseNotesCatalog.Entries, entry => entry.Version == "1.6.7");
        Assert.Equal("1.6.9", ReleaseNotesCatalog.GetEntry("1.6.9").Version);
    }

    [Fact]
    public void ReleaseNotes1611_ContainsAuditRecordsForEveryDisplayedItem()
    {
        ReleaseNoteEntry entry = ReleaseNotesCatalog.GetEntry("1.6.11");
        string[] items = entry.Sections.SelectMany(section => section.Items).ToArray();

        Assert.Equal(items.Length, entry.AuditTrail.Count);
        Assert.All(entry.AuditTrail, audit =>
        {
            Assert.Equal("1.6.11", audit.Version);
            Assert.Contains(audit.WrittenAt, new[] { "2026-09-12", "2026-09-13" });
            Assert.Contains(audit.Text, items);
        });
        Assert.Equal(items.Length, ReleaseNotesCatalog.AuditRecords.Count(audit => audit.Version == "1.6.11"));
        Assert.Equal(13, items.Length);
        Assert.Equal(items.Length, items.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ReleaseNotes1610_ContainsAuditRecordsForEveryDisplayedItem()
    {
        ReleaseNoteEntry entry = ReleaseNotesCatalog.GetEntry("1.6.10");
        string[] items = entry.Sections.SelectMany(section => section.Items).ToArray();

        Assert.Equal(items.Length, entry.AuditTrail.Count);
        Assert.All(entry.AuditTrail, audit =>
        {
            Assert.Equal("1.6.10", audit.Version);
            Assert.Contains(audit.WrittenAt, new[] { "2026-09-07", "2026-09-10" });
            Assert.Contains(audit.Text, items);
        });
        Assert.Equal(items.Length, ReleaseNotesCatalog.AuditRecords.Count(audit => audit.Version == "1.6.10"));
    }

    [Fact]
    public void ReleaseNotes169_MapsEveryLocalizedItemExactlyOnce()
    {
        ReleaseNoteEntry entry = ReleaseNotesCatalog.GetEntry("1.6.9");
        string[] items = entry.Sections.SelectMany(section => section.Items).ToArray();

        Assert.Equal([6, 11, 4, 6], entry.Sections.Select(section => section.Items.Count));
        Assert.Equal(27, items.Length);
        Assert.Equal(items.Length, items.Distinct(StringComparer.Ordinal).Count());
    }
}
