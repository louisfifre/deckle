using Deckle.Catalog;

namespace Deckle.Lighting.Ambient.Views;

// ── AmbientSettingsModule ─────────────────────────────────────────────────────
//
// The ambient-lighting module's own nav identity for the Settings shell. The module
// declares its page and its icon here; the composition root supplies only the Order.
public static class AmbientSettingsModule
{
    // This module's PRI subtree. Code resolving a module string through
    // Loc.GetFrom (the Playground's Ambient tab) reads it from here, never from a
    // repeated literal.
    public const string ResourceLibrary = "Deckle.Lighting.Ambient.Views";

    public const string PageTag = "Deckle.Lighting.Ambient.Views.AmbientPage, Deckle.Lighting.Ambient.Views";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "ambient",
        PageTag = PageTag,
        OwningAssembly = ResourceLibrary,
        Glyph = Glyphs.Lightbulb,
        Order = order,
    };
}
