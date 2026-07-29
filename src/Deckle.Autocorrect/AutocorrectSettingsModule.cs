using Deckle.Catalog;

namespace Deckle.Autocorrect;

// ── AutocorrectSettingsModule ─────────────────────────────────────────────────
//
// The autocorrect module's own nav identity for the Settings shell. The module
// declares its pages, their icons and their label keys here; the composition root
// supplies only the Order. Registered from App.OnLaunched via SettingsModuleRegistry.
//
// Three pages, one family: the Autocorrect page itself, plus two children nested
// under it in the rail — Lexical domains and Apps enrolled, each a surface too
// large to fold into the parent page. The page tags are constants because they are
// used twice: here, to name the destination in the rail, and on AutocorrectPage's
// navigation cards, which drill in through SettingsNavigation.GoToPage.
//
// The module's PRI subtree name lives here too — this is where the module states
// its identity to the shell, and the descriptors below already declare it. Any
// code resolving a module string through Loc.GetFrom reads it from here rather
// than repeating the literal or borrowing a constant off a passing view-model.
public static class AutocorrectSettingsModule
{
    // This module's PRI subtree — the .resw carrying the module's page strings.
    public const string ResourceLibrary = "Deckle.Autocorrect";

    public const string PageTag = "Deckle.Autocorrect.AutocorrectPage, Deckle.Autocorrect";

    public const string LexicalDomainsPageTag =
        "Deckle.Autocorrect.LexicalDomainsPage, Deckle.Autocorrect";

    public const string AppsEnrolledPageTag =
        "Deckle.Autocorrect.AppsEnrolledPage, Deckle.Autocorrect";

    private const string ModuleId = "autocorrect";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = ModuleId,
        PageTag = PageTag,
        OwningAssembly = ResourceLibrary,
        Glyph = Glyphs.Autocorrect,
        Order = order,
    };

    // The children carry a Glyph like any module because the search index shows one
    // beside a hit; the rail deliberately renders them without it (the parent's icon
    // names the family, indentation names the rest).
    public static SettingsModuleDescriptor DescribeLexicalDomains(int order) => new()
    {
        Id = "autocorrect-domains",
        ParentId = ModuleId,
        PageTag = LexicalDomainsPageTag,
        OwningAssembly = ResourceLibrary,
        LabelKey = "SettingsModuleNavLabel_LexicalDomains",
        Glyph = Glyphs.Autocorrect,
        Order = order,
    };

    public static SettingsModuleDescriptor DescribeAppsEnrolled(int order) => new()
    {
        Id = "autocorrect-apps",
        ParentId = ModuleId,
        PageTag = AppsEnrolledPageTag,
        OwningAssembly = ResourceLibrary,
        LabelKey = "SettingsModuleNavLabel_AppsEnrolled",
        Glyph = Glyphs.Autocorrect,
        Order = order,
    };
}
