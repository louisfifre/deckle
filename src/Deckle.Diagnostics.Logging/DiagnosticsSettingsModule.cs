using Deckle.Catalog;

namespace Deckle.Diagnostics.Logging;

// The diagnostics module's nav identity for the Settings shell; the
// composition root supplies only the Order.
public static class DiagnosticsSettingsModule
{
    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "diagnostics",
        PageTag = "Deckle.Diagnostics.Logging.DiagnosticsPage, Deckle.Diagnostics.Logging",
        OwningAssembly = "Deckle.Diagnostics.Logging",
        Glyph = Glyphs.Diagnostics,
        Order = order,
    };
}
