using Deckle.Catalog;

namespace Deckle.Input.PrecisionScroll;

public static class PrecisionScrollSettingsModule
{
    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "precision-scroll",
        PageTag = "Deckle.Input.PrecisionScroll.PrecisionScrollPage, Deckle.Input.PrecisionScroll",
        OwningAssembly = "Deckle.Input.PrecisionScroll",
        Glyph = Glyphs.Mouse,
        Order = order,
    };
}
