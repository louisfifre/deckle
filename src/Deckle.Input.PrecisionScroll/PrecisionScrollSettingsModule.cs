using Deckle.Catalog;

namespace Deckle.Input.PrecisionScroll;

public static class PrecisionScrollSettingsModule
{
    public const string PageTag = "Deckle.Input.PrecisionScroll.PrecisionScrollPage, Deckle.Input.PrecisionScroll";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "precision-scroll",
        PageTag = PageTag,
        OwningAssembly = "Deckle.Input.PrecisionScroll",
        Glyph = Glyphs.Mouse,
        Order = order,
    };
}
