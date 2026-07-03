using Deckle.Catalog;

namespace Deckle.Input.Trackpad;

// ── TrackpadSettingsModule ────────────────────────────────────────────────────
//
// The trackpad module's own nav identity for the Settings shell. The module declares
// its page, its icon and its label key here; the composition root supplies only the
// Order. Registered from App.OnLaunched via SettingsModuleRegistry.
public static class TrackpadSettingsModule
{
    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "trackpad",
        PageTag = "Deckle.Input.Trackpad.TrackpadPage, Deckle.Input.Trackpad",
        OwningAssembly = "Deckle.Input.Trackpad",
        Glyph = Glyphs.Trackpad,
        Order = order,
    };
}
