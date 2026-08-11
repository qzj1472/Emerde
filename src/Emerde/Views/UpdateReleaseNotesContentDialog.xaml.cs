using System.Globalization;
using Emerde.Core;
using Wpf.Ui.Violeta.Controls;
using AppResources = Emerde.Properties.Resources;

namespace Emerde.Views;

public partial class UpdateReleaseNotesContentDialog : ContentDialog
{
    public string DialogTitle { get; }

    public string CurrentVersionTitle { get; }

    public string UpgradeSummary { get; }

    public string HistoryLabel { get; }

    public IReadOnlyList<ReleaseNoteEntry> Entries { get; }

    public UpdateReleaseNotesContentDialog(UpgradeNoticeState notice)
    {
        DialogTitle = GetText("UpdateReleaseNotesDialogTitle", "Update notes");
        CurrentVersionTitle = FormatText("UpdateReleaseNotesCurrentVersionFormat", "Updated to {0}", notice.Version);
        UpgradeSummary = CreateUpgradeSummary(notice);
        HistoryLabel = GetText("UpdateReleaseNotesHistoryLabel", "Version history");
        ReleaseNoteEntry selectedEntry = ReleaseNotesCatalog.GetEntry(notice.Version);
        Entries = ReleaseNotesCatalog.Entries.Any(entry => string.Equals(entry.Version, selectedEntry.Version, StringComparison.OrdinalIgnoreCase))
            ? ReleaseNotesCatalog.Entries
            : [selectedEntry, .. ReleaseNotesCatalog.Entries];
        DataContext = this;
        InitializeComponent();
        ReleaseNoteVersionPicker.SelectedItem = selectedEntry;
    }

    private static string CreateUpgradeSummary(UpgradeNoticeState notice)
    {
        return !string.IsNullOrWhiteSpace(notice.PreviousVersion)
            ? FormatText("UpdateReleaseNotesUpgradeFromFormat", "Upgraded from {0} to {1}.", notice.PreviousVersion, notice.Version)
            : FormatText("UpdateReleaseNotesUpgradeCurrentFormat", "This is the first launch after upgrading to {0}.", notice.Version);
    }

    private static string GetText(string key, string fallback)
    {
        return AppResources.ResourceManager.GetString(key, AppResources.Culture) ?? fallback;
    }

    private static string FormatText(string key, string fallback, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, GetText(key, fallback), args);
    }
}
