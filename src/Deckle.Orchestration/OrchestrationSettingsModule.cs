using Deckle.Catalog;

namespace Deckle.Orchestration;

// The module's nav identity for the Settings shell: page tag, PRI subtree and
// icon. The composition root supplies only the Order. The nav label resolves
// from SettingsModuleNavLabel in this module's Resources.resw.
public static class OrchestrationSettingsModule
{
    // This module's PRI subtree: the .resw carrying the page strings.
    public const string ResourceLibrary = "Deckle.Orchestration";

    public const string PageTag = "Deckle.Orchestration.OrchestrationPage, Deckle.Orchestration";

    private const string ModuleId = "orchestration";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = ModuleId,
        PageTag = PageTag,
        OwningAssembly = ResourceLibrary,
        Glyph = Glyphs.Games,
        Order = order,
    };
}
