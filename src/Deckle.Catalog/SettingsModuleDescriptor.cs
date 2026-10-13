namespace Deckle.Catalog;

// ── SettingsNavTier ───────────────────────────────────────────────────────────
//
// Which band of the Settings NavigationView a module's page lands in. Order sorts
// within a tier.
public enum SettingsNavTier
{
    Header,
    Main,
    Footer,
}

// ── SettingsModuleDescriptor ──────────────────────────────────────────────────
//
// A module's self-description for the Settings shell, so its page gets a
// NavigationView entry without the shell knowing the module at compile time. It
// lives in Deckle.Catalog so a module declares its nav identity without a
// reference to the shell. The module owns its name and its icon; the composition
// root supplies only the Order.
//
// Late-bound: PageTag is a Type.GetType string, so the shell need not reference
// the module's assembly, and the label resolves at render time from the module's
// own PRI subtree, so an installed module ships its own nav wording.
public sealed record SettingsModuleDescriptor
{
    // Unique within the registry: Register replaces on it, Unregister removes by
    // it. Conventionally the module's persistence folder id
    // (modules/<Id>/settings.json).
    public required string Id { get; init; }

    // Assembly-qualified for a page in its own module assembly
    // (e.g. "Deckle.Transcription.WhisperPage, Deckle.Transcription").
    public required string PageTag { get; init; }

    // The module assembly whose PRI subtree carries the nav label.
    public required string OwningAssembly { get; init; }

    // The .resw key of the nav label inside OwningAssembly's subtree. Defaults to
    // the shared convention key every settings module defines, so a module only
    // overrides it to reuse an existing entry. A bare code-style key (no ".Content"
    // segment): the shell reads it into NavigationViewItem.Content directly, not
    // through an x:Uid.
    public string LabelKey { get; init; } = "SettingsModuleNavLabel";

    // A Glyphs.* character, built straight into a FontIcon.
    public required string Glyph { get; init; }

    public SettingsNavTier Tier { get; init; } = SettingsNavTier.Main;

    // Ascending within the tier; leave gaps between values so a later module can
    // land between two existing ones.
    public int Order { get; init; }

    // Set it and this page nests under that module in the rail. Reserved for a
    // family whose surface is too large to fold into its parent page.
    //
    // A child renders without an icon (the WinUI hierarchical-navigation sample:
    // the parent's icon carries the family), so its Glyph serves only the search
    // index. A ParentId naming no registered module is skipped, so a child cannot
    // outlive an uninstalled parent in the rail.
    public string? ParentId { get; init; }
}
