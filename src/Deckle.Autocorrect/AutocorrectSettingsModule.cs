using Deckle.Catalog;

namespace Deckle.Autocorrect;

// ── AutocorrectSettingsModule ─────────────────────────────────────────────────
//
// The autocorrect module's own nav identity for the Settings shell. The module
// declares its page, its icon and its label key here; the composition root supplies
// only the Order. Registered from App.OnLaunched via SettingsModuleRegistry.
public static class AutocorrectSettingsModule
{
    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "autocorrect",
        PageTag = "Deckle.Autocorrect.AutocorrectPage, Deckle.Autocorrect",
        OwningAssembly = "Deckle.Autocorrect",
        Glyph = Glyphs.Autocorrect,
        Order = order,
    };
}
