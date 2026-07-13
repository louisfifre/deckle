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
// The list is DECLARED here, not derived from the page or the manifests, on
// purpose. The composed cards live as instance properties on TrackpadViewModel
// (TrackpadDragSettingsManifest, TrackpadDiagnosticsSettingsManifest) — reaching
// them means constructing a ViewModel, which the index must never do at boot. So
// a card's identity travels as its bare LabelKey, the same string the manifest
// already carries and the composer stamps onto the built card's Tag. Composed and
// bespoke cards therefore declare identically: a LabelKey, whose "/Header" and
// "/Description" resolve from Resources.resw for both — the three Windows-
// integration cards carry .resw entries under their x:Uid just like the composed
// ones, so none needs a LiteralLabel.
//
// Maintenance contract: one card on the page, one entry here. Add a card (composed
// or hand-authored) and it stays invisible to search until it gets a line below;
// a bespoke card also needs its Tag="<LabelKey>" in TrackpadPage.xaml so a hit can
// scroll to it.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // "Three-finger drag" section — composed cards (TrackpadDragSettingsManifest).
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

        // "Diagnostics" section — composed card (TrackpadDiagnosticsSettingsManifest).
        new SettingSearchEntry
        {
            LabelKey = "TrackpadPage_RecordFramesCard",
            Keywords = ["telemetry", "capture", "jsonl", "diagnostics"],
        },

        // "Windows integration" section — hand-authored cards, Tag'd in the XAML.
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
}
