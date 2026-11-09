using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

public static class LlmSettingsModule
{
    // Also the parent a profile's subpage names in its trail.
    public const string PageTag = "Deckle.Llm.Rewrite.LlmPage, Deckle.Llm.Rewrite";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "llm",
        PageTag = PageTag,
        OwningAssembly = "Deckle.Llm.Rewrite",
        Glyph = Glyphs.Sparkle,
        Order = order,
    };
}
