using Deckle.Catalog;

namespace Deckle.Diagnostics.Logging.Views;

// The diagnostics module's nav identity for the Settings shell; the
// composition root supplies only the Order.
public static class DiagnosticsSettingsModule
{
    // Public so a window outside the Settings shell (the LogWindow) can open
    // this page by name without rebuilding the descriptor. The shell resolves
    // it with Type.GetType, so it names the assembly that compiles the page.
    public const string PageTag = "Deckle.Diagnostics.Logging.Views.DiagnosticsPage, Deckle.Diagnostics.Logging.Views";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "diagnostics",
        PageTag = PageTag,
        // The PRI subtree of the nav label and the search entries: the page's
        // strings live with the page, in the Views assembly.
        OwningAssembly = "Deckle.Diagnostics.Logging.Views",
        Glyph = Glyphs.Diagnostics,
        Order = order,
    };
}
