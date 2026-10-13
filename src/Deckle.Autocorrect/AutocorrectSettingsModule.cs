using Deckle.Catalog;

namespace Deckle.Autocorrect;

// ── AutocorrectSettingsModule ─────────────────────────────────────────────────
//
// The autocorrect module's nav identity for the Settings shell: its pages, their
// icons and label keys. The composition root supplies only the Order.
//
// Three pages, one family: the Autocorrect page, plus Lexical domains and Apps
// enrolled nested under it in the rail. The page tags are constants because
// AutocorrectPage's navigation cards also drill in to them.
public static class AutocorrectSettingsModule
{
    // This module's PRI subtree. Code resolving a module string through
    // Loc.GetFrom reads it from here, never from a repeated literal.
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
