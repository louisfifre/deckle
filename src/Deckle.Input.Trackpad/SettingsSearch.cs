using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Input.Trackpad;

// The trackpad page's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card. The composition root registers this list
// at boot alongside the module's nav descriptor, and the index resolves each
// entry's text from this module's own PRI subtree without composing the page.
//
// The list is DECLARED here, not derived from the manifests: they are instance
// properties on TrackpadViewModel, and the index must never construct a
// ViewModel at boot. A card's identity is its bare LabelKey, the string the
// composer stamps onto the built card's Tag; composed and hand-authored cards
// alike resolve "/Header" and "/Description" from Resources.resw.
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
