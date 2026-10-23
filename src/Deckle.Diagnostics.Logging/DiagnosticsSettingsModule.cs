using Deckle.Catalog;

namespace Deckle.Diagnostics.Logging;

// The diagnostics module's nav identity for the Settings shell; the
// composition root supplies only the Order.
public static class DiagnosticsSettingsModule
{
    // Public so a window outside the Settings shell (the LogWindow) can open
    // this page by name without rebuilding the descriptor.
    public const string PageTag = "Deckle.Diagnostics.Logging.DiagnosticsPage, Deckle.Diagnostics.Logging";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "diagnostics",
        PageTag = PageTag,
        OwningAssembly = "Deckle.Diagnostics.Logging",
        Glyph = Glyphs.Diagnostics,
        Order = order,
    };
}
