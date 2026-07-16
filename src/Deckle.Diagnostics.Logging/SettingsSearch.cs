using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Diagnostics.Logging;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// The diagnostics page's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card. The composition root registers this list
// at boot alongside the module's nav descriptor, and the index resolves each
// entry's text from this module's own PRI subtree without composing the page.
//
// The list is DECLARED here, not derived from the page or the manifest, on
// purpose. The composed cards live as instance properties on DiagnosticsViewModel
// (LoggingSettings, TelemetrySettings, StorageFolderSettings) — reaching them means
// constructing a ViewModel, which the index must never do at boot. So a card's
// identity travels as its bare LabelKey, the same string the manifest already
// carries and the composer stamps onto the built card's Tag. Composed and bespoke
// cards therefore declare identically: a LabelKey, whose "/Header" and
// "/Description" resolve from Resources.resw for both — the bespoke Diagnostic-files
// card carries .resw entries under its x:Uid just like the composed ones, so none
// needs a LiteralLabel.
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
            LabelKey = "LoggingWindowingCard",
            Keywords = ["monitor", "display", "placement"],
        },

        // "Application log" section.
        new SettingSearchEntry
        {
            LabelKey = "GeneralAppLogCard",
            Keywords = ["journal", "debug", "troubleshoot"],
        },

        // Storage folder — composed Path card (StorageFolderSettings).
        new SettingSearchEntry
        {
            LabelKey = "GeneralStorageFolderCard",
            Keywords = ["directory", "location", "path"],
        },

        // "Diagnostic files" section — hand-authored card, Tag'd in the XAML.
        new SettingSearchEntry
        {
            LabelKey = "DiagnosticsFilesCard",
            Keywords = ["crash", "troubleshoot", "debug"],
        },
    ];
}
