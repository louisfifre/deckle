using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ── LlmSettingsModule ─────────────────────────────────────────────────────────
//
// The rewrite module's own nav identity for the Settings shell. The module declares
// its page, its icon and its label key here; the composition root supplies only the
// Order. Registered from App.OnLaunched via SettingsModuleRegistry.
public static class LlmSettingsModule
{
    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "llm",
        PageTag = "Deckle.Llm.Rewrite.LlmPage, Deckle.Llm.Rewrite",
        OwningAssembly = "Deckle.Llm.Rewrite",
        Glyph = Glyphs.Sparkle,
        Order = order,
    };
}
