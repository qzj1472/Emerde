using Emerde.Properties;

namespace Emerde.Core;

internal static class ReleaseNotesCatalog
{
    public static IReadOnlyList<ReleaseNoteEntry> Entries { get; } =
    [
        Create1672(),
        Create1671(),
        Create1670(),
    ];

    public static ReleaseNoteEntry GetEntry(string version)
    {
        return Entries.FirstOrDefault(entry => string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase))
            ?? new ReleaseNoteEntry(
                version,
                GetText("ReleaseNotesUnknownTitle", "Emerde update"),
                string.Empty,
                [Section("ReleaseNotesCategoryStability", "Performance and stability", [GetText("ReleaseNotesUnknownItem", "This version includes stability and experience improvements.")])]);
    }

    private static ReleaseNoteEntry Create1672()
    {
        IReadOnlyList<string> items = SplitItems(GetText("ReleaseNotes1672Items", "Automatically rediscovered unprocessed recordings and improved recovery queues|Improved recording cleanup and UI-X preference persistence|Refined UI-X menus and input states|Generated independent covers from recorded frames|Added clearer video processing status badges|Improved notifications and notification history|Improved configuration save and recording-state recovery"));
        IReadOnlyList<string> additional = SplitItems(GetText("ReleaseNotes1672AdditionalItems", "Reduced unnecessary UI refresh work|Improved recovery retries|Improved video-list refresh feedback and resize performance|Improved shared UI resource isolation"));
        return new ReleaseNoteEntry(
            "1.6.7.2",
            GetText("ReleaseNotes1672Title", "Emerde 1.6.7.2"),
            GetText("ReleaseNotes1672Date", "2026-08-18"),
            RemoveEmpty(
                Section("ReleaseNotesCategoryBugFixes", "Bug fixes", Pick(items, 2, 4, 5, 10, 15)),
                Section("ReleaseNotesCategoryFeatures", "Feature additions", Pick(items, 0, 1, 3, 6, 7, 9, 12, 13, 14)),
                Section("ReleaseNotesCategoryStability", "Performance and stability", Pick(additional, 0, 1, 2)),
                Section("ReleaseNotesCategoryUi", "Interface changes", Pick(items, 8, 11, 16, 17).Concat(Pick(additional, 3)).ToArray())));
    }

    private static ReleaseNoteEntry Create1671()
    {
        IReadOnlyList<string> items = SplitItems(GetText("ReleaseNotes1671Items", "Improved recording and conversion reliability|Added damaged-recording repair|Refined UI-X pages, dialogs, preview, and video management"));
        IReadOnlyList<string> additional = SplitItems(GetText("ReleaseNotes1671AdditionalItems", "Improved upgrade notices and configuration recovery|Refined tray and notification workflows"));
        return new ReleaseNoteEntry(
            "1.6.7.1",
            GetText("ReleaseNotes1671Title", "Emerde 1.6.7.1"),
            GetText("ReleaseNotes1671Date", "2026-08-13"),
            RemoveEmpty(
                Section("ReleaseNotesCategoryBugFixes", "Bug fixes", Pick(items, 0)),
                Section("ReleaseNotesCategoryFeatures", "Feature additions", Pick(items, 1, 2)),
                Section("ReleaseNotesCategoryStability", "Performance and stability", Pick(items, 3).Concat(Pick(additional, 1, 2, 3)).ToArray()),
                Section("ReleaseNotesCategoryUi", "Interface changes", Pick(items, 4, 5, 6, 7, 8)),
                Section("ReleaseNotesCategoryInstall", "Installation and upgrade", Pick(additional, 0))));
    }

    private static ReleaseNoteEntry Create1670()
    {
        IReadOnlyList<string> items = SplitItems(GetText("ReleaseNotes1670Items", "Improved installation, upgrade, and configuration recovery|Improved room-link import and duplicate prevention|Refined home cards and context actions"));
        IReadOnlyList<string> additional = SplitItems(GetText("ReleaseNotes1670AdditionalItems", "Improved maintenance status and data retention workflows"));
        return new ReleaseNoteEntry(
            "1.6.7.0",
            GetText("ReleaseNotes1670Title", "Emerde 1.6.7.0"),
            GetText("ReleaseNotes1670Date", "2026-08-10"),
            RemoveEmpty(
                Section("ReleaseNotesCategoryFeatures", "Feature additions", Pick(items, 1, 2, 4)),
                Section("ReleaseNotesCategoryStability", "Performance and stability", Pick(additional, 0, 2)),
                Section("ReleaseNotesCategoryUi", "Interface changes", Pick(items, 3)),
                Section("ReleaseNotesCategoryInstall", "Installation and upgrade", Pick(items, 0).Concat(Pick(additional, 1)).ToArray())));
    }

    private static ReleaseNoteSection Section(string titleKey, string fallback, IEnumerable<string> items)
    {
        return new ReleaseNoteSection(GetText(titleKey, fallback), items.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray());
    }

    private static IReadOnlyList<ReleaseNoteSection> RemoveEmpty(params ReleaseNoteSection[] sections)
    {
        return sections.Where(section => section.Items.Count > 0).ToArray();
    }

    private static IReadOnlyList<string> Pick(IReadOnlyList<string> items, params int[] indexes)
    {
        return indexes.Where(index => index >= 0 && index < items.Count).Select(index => items[index]).ToArray();
    }

    private static string GetText(string key, string fallback)
    {
        return Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;
    }

    private static IReadOnlyList<string> SplitItems(string text)
    {
        return text
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }
}

public sealed record ReleaseNoteSection(string Title, IReadOnlyList<string> Items);

public sealed record ReleaseNoteEntry(
    string Version,
    string Title,
    string Date,
    IReadOnlyList<ReleaseNoteSection> Sections)
{
    public string VersionLabel => string.IsNullOrWhiteSpace(Date)
        ? Version
        : $"{Version}  {Date}";
}
