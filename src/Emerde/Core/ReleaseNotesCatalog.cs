using Emerde.Properties;

namespace Emerde.Core;

internal static class ReleaseNotesCatalog
{
    public static IReadOnlyList<ReleaseNoteEntry> Entries { get; } =
    [
        Create1682(),
        Create1681(),
        Create1672(),
        Create1671(),
        Create1670(),
    ];

    internal static IReadOnlyList<ReleaseNoteAuditRecord> AuditRecords { get; } =
        Entries.SelectMany(entry => entry.AuditTrail).ToArray();

    public static ReleaseNoteEntry GetEntry(string version)
    {
        return Entries.FirstOrDefault(entry => string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase))
            ?? new ReleaseNoteEntry(
                version,
                GetText("ReleaseNotesUnknownTitle", "Emerde update"),
                string.Empty,
                [Section("ReleaseNotesCategoryStability", "Performance and stability", [GetText("ReleaseNotesUnknownItem", "This version includes stability and experience improvements.")])])
            {
                AuditTrail = []
            };
    }

    private static ReleaseNoteEntry Create1682()
    {
        IReadOnlyList<string> items = SplitItems(GetText("ReleaseNotes1682Items", "Removed leftover old-UI surfaces so add-room, home selection, settings, video list, and notifications stay on UI-X|About page now explains automatic segmentation: it is a fallback for recording anomalies, reconnects, and recovery; default auto segments are merged at the end, while custom segmentation in Settings stays separate|Recording restarts after a persistent audio-content anomaly instead of continuing with a bad audio track|Internal stall-recovery parts are no longer shown as extra finished videos|Video library no longer reserves the old header space, so in-progress cards no longer leave a large empty gap"));
        IReadOnlyList<string> additional = SplitItems(GetText("ReleaseNotes1682AdditionalItems", "Installer wording and dialogs follow the current version|Tightened conversion timeline and audio-content diagnostics when finishing recordings"));
        ReleaseNoteEntry entry = new(
            "1.6.8.2",
            GetText("ReleaseNotes1682Title", "Emerde 1.6.8.2"),
            GetText("ReleaseNotes1682Date", "2026-09-12"),
            RemoveEmpty(
                Section("ReleaseNotesCategoryBugFixes", "Bug fixes", Pick(items, 0, 2, 3)),
                Section("ReleaseNotesCategoryFeatures", "Feature additions", Pick(items, 1)),
                Section("ReleaseNotesCategoryStability", "Performance and stability", Pick(additional, 1)),
                Section("ReleaseNotesCategoryUi", "Interface changes", Pick(items, 4).Concat(Pick(additional, 0)).ToArray())));
        return entry with
        {
            AuditTrail = CreateAuditTrail(
                "1.6.8.2",
                items.Concat(additional),
                [
                    ("2026-09-12", "working-tree-20260912"),
                    ("2026-09-12", "working-tree-20260912"),
                    ("2026-09-12", "working-tree-20260912"),
                    ("2026-09-12", "working-tree-20260912"),
                    ("2026-09-12", "working-tree-20260912"),
                    ("2026-09-12", "working-tree-20260912"),
                    ("2026-09-12", "working-tree-20260912")
                ])
        };
    }

    private static ReleaseNoteEntry Create1681()
    {
        IReadOnlyList<string> items = SplitItems(GetText("ReleaseNotes1681Items", "Added media diagnostics for audio, video, container, codec, resolution, frame rate, timeline, stream count, decode errors, RMS, peak, and near-silence intervals|MKV recordings always request optimized audio; when optimization cannot be produced but the original audio is valid, the original audio is preserved and the issue is marked clearly|Added recording timeline and audio-content sampling to identify whether anomalies occur in the live source, TS input, intermediate segments, or final output|Added automatic safety segmentation for recording recovery, with fallback segments merged into one final product while user-configured segmentation remains separate|Active recording and processing segments are folded into one room card, with stable numbering and simultaneous status indicators|Existing video covers are preserved; the new cover generation flow applies to newly created covers|Added disk-space protection that pauses recording work and keeps the alert until space is restored|Improved recovery and metadata writes so interrupted processing can resume without losing recording state|Transcoding failures caused by insufficient disk space no longer trigger meaningless recovery retries|Empty metadata streams no longer continue producing repeated error logs"));
        IReadOnlyList<string> additional = SplitItems(GetText("ReleaseNotes1681AdditionalItems", "Improved installer version display, output naming, and dialog mask consistency|Improved configuration restore alignment, long status text wrapping, avatar cache cleanup, and resolver cancellation|Recorded processing speed observations for later comparison; no conversion performance logic was changed in this release"));
        ReleaseNoteEntry entry = new(
            "1.6.8.1",
            GetText("ReleaseNotes1681Title", "Emerde 1.6.8.1"),
            GetText("ReleaseNotes1681Date", "2026-09-10"),
            RemoveEmpty(
                Section("ReleaseNotesCategoryBugFixes", "Bug fixes", Pick(items, 1, 2, 4, 5, 6, 7, 8, 9)),
                Section("ReleaseNotesCategoryFeatures", "Feature additions", Pick(items, 0, 3)),
                Section("ReleaseNotesCategoryStability", "Performance and stability", Pick(additional, 2)),
                Section("ReleaseNotesCategoryUi", "Interface changes", Pick(additional, 0, 1))));
        return entry with
        {
            AuditTrail = CreateAuditTrail(
                "1.6.8.1",
                items.Concat(additional),
                [
                    ("2026-09-07", "97c6a80"),
                    ("2026-09-07", "97c6a80"),
                    ("2026-09-07", "97c6a80"),
                    ("2026-09-10", "working-tree-20260910"),
                    ("2026-09-10", "working-tree-20260910"),
                    ("2026-09-07", "abe23f8"),
                    ("2026-09-07", "37d4629"),
                    ("2026-09-07", "0defb13;2f321e3"),
                    ("2026-09-07", "0e9a486;c64dd22"),
                    ("2026-09-10", "working-tree-20260910"),
                    ("2026-09-10", "working-tree-20260910"),
                    ("2026-09-10", "working-tree-20260910"),
                    ("2026-09-10", "working-tree-20260910")
                ])
        };
    }

    private static ReleaseNoteEntry Create1672()
    {
        IReadOnlyList<string> items = [
            GetText("ReleaseNotes1672RecoveryItem", "Recordings stopped during shutdown or restart are saved as pending tasks and processed on the next startup; unregistered TS and FLV files are no longer scanned automatically."),
            .. SplitItems(GetText("ReleaseNotes1672Items", "Automatically rediscovered unprocessed recordings and improved recovery queues|Improved recording cleanup and UI-X preference persistence|Refined UI-X menus and input states|Generated independent covers from recorded frames|Added clearer video processing status badges|Improved notifications and notification history|Improved configuration save and recording-state recovery")).Skip(1),
            GetText("ReleaseNotes1672Item18", "Video card titles now hide file-format extensions."),
            GetText("ReleaseNotes1672Item19", "Recovered recordings now complete final naming instead of retaining temporary names."),
            GetText("ReleaseNotes1672Item20", "Recording video cards now use the complete streamer avatar saved at recording start as their cover."),
            GetText("ReleaseNotes1672Item21", "Manual transcode now lets you choose whether to delete the source file after a successful conversion; the default follows recording settings."),
        ];
        IReadOnlyList<string> additional = SplitItems(GetText("ReleaseNotes1672AdditionalItems", "Reduced unnecessary UI refresh work|Improved recovery retries|Improved video-list refresh feedback and resize performance|Improved shared UI resource isolation|Improved video-list refresh and tray-hidden exception handling"));
        return new ReleaseNoteEntry(
            "1.6.7.2",
            GetText("ReleaseNotes1672Title", "Emerde 1.6.7.2"),
            GetText("ReleaseNotes1672Date", "2026-08-24"),
            RemoveEmpty(
                Section("ReleaseNotesCategoryBugFixes", "Bug fixes", Pick(items, 2, 4, 5, 10, 15, 19)),
                Section("ReleaseNotesCategoryFeatures", "Feature additions", Pick(items, 0, 1, 3, 6, 7, 9, 12, 13, 14, 20, 21)),
                Section("ReleaseNotesCategoryStability", "Performance and stability", Pick(additional, 0, 1, 2, 4)),
                Section("ReleaseNotesCategoryUi", "Interface changes", Pick(items, 8, 11, 16, 17, 18).Concat(Pick(additional, 3)).ToArray())))
        {
            AuditTrail = CreateAuditTrail("1.6.7.2", items.Concat(additional), "2026-08-24", "d5120e4")
        };
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
                Section("ReleaseNotesCategoryInstall", "Installation and upgrade", Pick(additional, 0))))
        {
            AuditTrail = CreateAuditTrail("1.6.7.1", items.Concat(additional), "2026-08-24", "d5120e4")
        };
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
                Section("ReleaseNotesCategoryInstall", "Installation and upgrade", Pick(items, 0).Concat(Pick(additional, 1)).ToArray())))
        {
            AuditTrail = CreateAuditTrail("1.6.7.0", items.Concat(additional), "2026-08-24", "d5120e4")
        };
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

    private static IReadOnlyList<ReleaseNoteAuditRecord> CreateAuditTrail(
        string version,
        IEnumerable<string> items,
        string writtenAt,
        string sourceRevision)
    {
        return CreateAuditTrail(version, items, items.Select(_ => (writtenAt, sourceRevision)).ToArray());
    }

    private static IReadOnlyList<ReleaseNoteAuditRecord> CreateAuditTrail(
        string version,
        IEnumerable<string> items,
        IReadOnlyList<(string WrittenAt, string SourceRevision)> metadata)
    {
        string[] values = items.ToArray();
        if (values.Length != metadata.Count)
        {
            throw new InvalidOperationException($"Release note audit metadata count mismatch for {version}");
        }

        return values
            .Select((text, index) => new ReleaseNoteAuditRecord(
                $"{version.Replace('.', '-')}-{index + 1:000}",
                version,
                text,
                metadata[index].WrittenAt,
                metadata[index].SourceRevision))
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
    internal IReadOnlyList<ReleaseNoteAuditRecord> AuditTrail { get; init; } = [];

    public string VersionLabel => string.IsNullOrWhiteSpace(Date)
        ? Version
        : $"{Version}  {Date}";
}

internal sealed record ReleaseNoteAuditRecord(
    string ItemId,
    string Version,
    string Text,
    string WrittenAt,
    string SourceRevision);
