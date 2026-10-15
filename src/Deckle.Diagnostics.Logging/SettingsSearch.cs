using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Diagnostics.Logging;

// The diagnostics page's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card, resolved from this module's own PRI
// subtree without composing the page.
//
// The list is declared, not derived from the manifest: the composed cards are
// instance properties of DiagnosticsViewModel, and the index must never construct
// a ViewModel at boot. A card's identity is its bare LabelKey, the string the
// composer stamps onto the built card's Tag.
//
// Maintenance contract: one card on the page, one entry here. Add a card (composed
// or hand-authored) and it stays invisible to search until it gets a line below; a
// bespoke card also needs its Tag="<LabelKey>" in DiagnosticsPage.xaml so a hit can
// scroll to it.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // "Logging" section — composed card (LoggingSettings).
        new SettingSearchEntry
        {
            LabelKey = "LoggingAmbientCard",
            Keywords = ["lighting", "capture", "hue"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingTranscriptionCard",
            Keywords = ["dictation", "speech", "file"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingAutocorrectCard",
            Keywords = ["typing", "correction", "learning"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingInputCard",
            Keywords = ["raw input", "trackpad", "gesture", "frames"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LoggingWindowingCard",
            Keywords = ["monitor", "display", "placement"],
        },

        // "Application log" section.
        new SettingSearchEntry
        {
            LabelKey = "GeneralAppLogCard",
            Keywords = ["journal", "debug", "troubleshoot"],
        },

        // "Telemetry" section — the page's own composed toggle (TelemetrySettings).
        new SettingSearchEntry
        {
            LabelKey = "TelemetryMouseWheelCard",
            Keywords = ["mouse", "scroll", "wheel", "jsonl", "capture"],
        },

        // "Folders" section, last on the page: the two composed folder folds
        // (FoldersSettings), the telemetry folder then the diagnostics folder.
        new SettingSearchEntry
        {
            LabelKey = "GeneralStorageFolderCard",
            Keywords = ["directory", "location", "path", "telemetry"],
        },
        new SettingSearchEntry
        {
            LabelKey = "DiagnosticsFilesCard",
            Keywords = ["crash", "troubleshoot", "debug"],
        },
    ];
}
