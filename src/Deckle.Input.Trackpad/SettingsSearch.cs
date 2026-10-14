using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Input.Trackpad;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// The trackpad page's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card. The composition root registers this list
// at boot alongside the module's nav descriptor, and the index resolves each
// entry's text from this module's own PRI subtree without composing the page.
//
// The list is declared here, not derived from the manifests: they are instance
// properties on TrackpadViewModel, and the index must never construct a
// ViewModel at boot. Composed and hand-authored cards alike resolve "/Header"
// and "/Description" from Resources.resw, so none needs a LiteralLabel.
//
// Maintenance contract: one card on the page, one entry here. A hand-authored
// card also needs its Tag="<LabelKey>" in TrackpadPage.xaml so a hit can scroll
// to it.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "TrackpadPage_DragCard",
            Keywords = ["gesture", "magic trackpad", "pickup", "drop"],
        },

        new SettingSearchEntry
        {
            LabelKey = "TrackpadPage_SpeedCard",
            Keywords = ["sensitivity", "acceleration", "velocity"],
        },

        new SettingSearchEntry
        {
            LabelKey = "TrackpadPage_GesturesCard",
            Keywords = ["swipe", "neutralize", "virtual desktop", "task view"],
        },
        new SettingSearchEntry
        {
            LabelKey = "TrackpadPage_RepairCard",
            Keywords = ["pairing", "driver", "reconnect", "apple"],
        },
        new SettingSearchEntry
        {
            LabelKey = "TrackpadPage_ElevatedCard",
            Keywords = ["admin", "administrator", "uac", "privileges"],
        },
    ];

    // The raw-frame recording, composed on Settings > Diagnostics
    // (TrackpadTelemetryViewModel); registered under that page with this
    // module's assembly, where its words live.
    public static IReadOnlyList<SettingSearchEntry> TelemetryEntries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "TrackpadPage_RecordFramesCard",
            Keywords = ["telemetry", "capture", "jsonl", "diagnostics", "trackpad"],
        },
    ];
}
