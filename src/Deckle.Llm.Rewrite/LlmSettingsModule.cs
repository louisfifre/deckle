using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

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
