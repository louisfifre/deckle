using Deckle.Catalog;

namespace Deckle.Lighting.Ambient;

// ── AmbientSettingsModule ─────────────────────────────────────────────────────
//
// The ambient-lighting module's own nav identity for the Settings shell. The module
// declares its page, its icon and its label key here; the composition root supplies
// only the Order. Registered from App.OnLaunched via SettingsModuleRegistry.
public static class AmbientSettingsModule
{
    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "ambient",
        PageTag = "Deckle.Lighting.Ambient.AmbientPage, Deckle.Lighting.Ambient",
        OwningAssembly = "Deckle.Lighting.Ambient",
        Glyph = Glyphs.Lightbulb,
        Order = order,
    };
}
