using Emerde.Properties;

namespace Emerde.Core;

internal static class ReleaseNotesCatalog
{
    public static IReadOnlyList<ReleaseNoteEntry> Entries { get; } =
    [
        new(
            "1.6.7.1",
            GetText("ReleaseNotes1671Title", "Emerde 1.6.7.1"),
            GetText("ReleaseNotes1671Date", "2026-08-13"),
            SplitItems(GetText("ReleaseNotes1671Items", "Improved recording and conversion reliability|Added damaged-recording repair|Refined UI-X pages, dialogs, preview, and video management")),
            SplitItems(GetText("ReleaseNotes1671AdditionalItems", "Improved upgrade notices and configuration recovery|Refined tray and notification workflows"))),
        new(
            "1.6.7.0",
            GetText("ReleaseNotes1670Title", "Emerde 1.6.7.0"),
            GetText("ReleaseNotes1670Date", "2026-08-10"),
            SplitItems(GetText("ReleaseNotes1670Items", "Improved installation, upgrade, and configuration recovery|Improved room-link import and duplicate prevention|Refined home cards and context actions")),
            SplitItems(GetText("ReleaseNotes1670AdditionalItems", "Improved maintenance status and data retention workflows"))),
    ];

    public static ReleaseNoteEntry GetEntry(string version)
    {
        return Entries.FirstOrDefault(entry => string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase))
            ?? new ReleaseNoteEntry(
                version,
                GetText("ReleaseNotesUnknownTitle", "Emerde update"),
                string.Empty,
                [GetText("ReleaseNotesUnknownItem", "This version includes stability and experience improvements.")],
                []);
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

public sealed record ReleaseNoteEntry(
    string Version,
    string Title,
    string Date,
    IReadOnlyList<string> Items,
    IReadOnlyList<string> AdditionalItems)
{
    public bool HasAdditionalItems => AdditionalItems.Count > 0;

    public string VersionLabel => string.IsNullOrWhiteSpace(Date)
        ? Version
        : $"{Version}  {Date}";
}
