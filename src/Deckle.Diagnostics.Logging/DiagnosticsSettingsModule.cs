using Deckle.Catalog;

namespace Deckle.Diagnostics.Logging;

// ── DiagnosticsSettingsModule ─────────────────────────────────────────────────
//
// The diagnostics module's nav identity for the Settings shell. Diagnostics used
// to be a static anchor in SettingsWindow.xaml; relocating the page here makes it
// a registered module page like the others, so the module owns its page, icon and
// label key and the composition root supplies only the Order. Order 600 keeps it
// last in the module band — after Trackpad — exactly where the static anchor sat.
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
