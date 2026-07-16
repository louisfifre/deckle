using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Lighting.Ambient;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// The ambient-lighting module's contribution to the shell's cross-page settings
// search: one SettingSearchEntry per findable card on AmbientPage. The shell resolves
// each entry's text from this module's PRI subtree at boot (Loc.GetFrom on the page's
// OwningAssembly) and answers queries over the resolved set — no page is ever composed.
//
// The list is declared by hand because AmbientPage is entirely hand-authored: it has no
// SettingDescriptor manifest to enumerate (its would-be ViewModel lives in
// Deckle.Playground and is not owned here), so every card exists only as a XAML x:Uid
// and this list is the searchable set's only source. Each entry's LabelKey IS that
// x:Uid: it resolves "<key>/Header" and "<key>/Description" straight from the module
// .resw, and it is the same string stamped onto the card's Tag in AmbientPage.xaml so a
// hit can scroll the card into view. No LiteralLabel is needed — these cards are
// localized through x:Uid like any composed card, only without a composer to stamp the Tag.
//
// Maintenance contract: a card added to AmbientPage that a user should be able to find
// gets one entry here AND a matching Tag in the XAML. The transient NotPaired InfoBar is
// deliberately absent — it is a state banner, not a setting.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new() { LabelKey = "AmbientEnabledCard", Keywords = ["ambilight", "bias lighting", "backlight"] },
        new() { LabelKey = "AmbientModeCard", Keywords = ["preset", "game", "movie"] },
        new() { LabelKey = "HueBridgeExpander", Keywords = ["philips", "pair", "connect"] },
        new() { LabelKey = "HueBridgeAddressCard", Keywords = ["ip", "discover", "network"] },
        new() { LabelKey = "HueBridgeGroupCard", Keywords = ["group", "entertainment", "lights"] },
        new() { LabelKey = "HueBridgeForgetCard", Keywords = ["remove", "unpair", "delete"] },
        new() { LabelKey = "AmbientOpenPlaygroundCard", Keywords = ["tuning", "calibrate"] },
    ];
}
