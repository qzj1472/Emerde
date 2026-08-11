using Emerde.Properties;

namespace Emerde.Core;

internal static class ReleaseNotesCatalog
{
    public static IReadOnlyList<ReleaseNoteEntry> Entries { get; } =
    [
        new(
            "1.6.7.0",
            GetText("ReleaseNotes1670Title", "Emerde 1.6.7.0"),
            GetText("ReleaseNotes1670Date", "2026-08-10"),
            SplitItems(GetText("ReleaseNotes1670Items", "Installer upgrade notice|Installer maintenance flow|Home card interaction refinement"))),
    ];

    public static ReleaseNoteEntry GetEntry(string version)
    {
        return Entries.FirstOrDefault(entry => string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase))
            ?? new ReleaseNoteEntry(
                version,
                GetText("ReleaseNotesUnknownTitle", "Emerde update"),
                string.Empty,
                [GetText("ReleaseNotesUnknownItem", "This version includes stability and experience improvements.")]);
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
    IReadOnlyList<string> Items)
{
    public string VersionLabel => string.IsNullOrWhiteSpace(Date)
        ? Version
        : $"{Version}  {Date}";
}
