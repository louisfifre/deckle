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
// Each entry's LabelKey resolves "<key>/Header" and "<key>/Description" from the module
// .resw and is the string on the card's Tag, the handle a hit scrolls into view: the
// composer stamps it on the cards it builds from AmbientViewModel.Settings.cs, and the
// bespoke cards carry Tag="<key>" in AmbientPage.xaml.
//
// Children of a composed fold (the band shape and depth under "Colour per zone", the
// floor level under "Brightness floor") are not indexed — a hit must be bringable into
// view, and they hide with their master — so the fold's keywords bridge to them.
//
// Maintenance contract: a card added to AmbientPage that a user should be able to find
// gets one entry here (and, when bespoke, a matching Tag in the XAML). The transient
// NotPaired InfoBar is deliberately absent — it is a state banner, not a setting.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new() { LabelKey = "AmbientEnabledCard", Keywords = ["ambilight", "bias lighting", "backlight"] },
        new() { LabelKey = "AmbientModeCard", Keywords = ["preset", "game", "movie", "custom"] },
        new() { LabelKey = "AmbientZonesGroup", Keywords = ["multi light", "zones", "edges", "border", "depth", "band"] },
        new() { LabelKey = "AmbientBrightnessCurveCard", Keywords = ["curve", "bezier", "response", "shadows"] },
        new() { LabelKey = "AmbientMinBrightnessGroup", Keywords = ["minimum brightness", "floor", "dark scenes"] },
        new() { LabelKey = "AmbientSmoothingCard", Keywords = ["smoothing", "speed", "fade", "lag"] },
        new() { LabelKey = "AmbientChangeThresholdCard", Keywords = ["threshold", "flicker", "steady"] },
        new() { LabelKey = "AmbientExposureCard", Keywords = ["ev", "brightness", "hdr"] },
        new() { LabelKey = "AmbientSaturationCard", Keywords = ["vivid", "colour", "color", "chroma"] },
        new() { LabelKey = "AmbientMonitorCard", Keywords = ["monitor", "screen", "display"] },
        new() { LabelKey = "AmbientCaptureRateCard", Keywords = ["fps", "frame rate", "hz", "performance"] },
        new() { LabelKey = "HueBridgeExpander", Keywords = ["philips", "pair", "connect"] },
        new() { LabelKey = "HueBridgeAddressCard", Keywords = ["ip", "discover", "network"] },
        new() { LabelKey = "HueBridgeGroupCard", Keywords = ["group", "entertainment", "lights"] },
        new() { LabelKey = "HueBridgeForgetCard", Keywords = ["remove", "unpair", "delete"] },
    ];
}
