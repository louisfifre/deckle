namespace Deckle.Catalog;

// ── SettingsNavTier ───────────────────────────────────────────────────────────
//
// Which band of the Settings NavigationView a module's page lands in. Header sits
// above the main list, Main is the ordinary body, Footer is the bottom band (where
// the shell keeps cross-cutting commands like Logs). Order sorts WITHIN a tier, so
// a module places itself in a band and then among its band-mates.
public enum SettingsNavTier
{
    Header,
    Main,
    Footer,
}

// ── SettingsModuleDescriptor ──────────────────────────────────────────────────
//
// A module's self-description for the Settings shell: the one object a module
// contributes so its page gets a NavigationView entry, without the shell knowing
// the module at compile time. Registered into SettingsModuleRegistry (by the App
// composition root today, by the future module installer tomorrow); the shell
// materialises one NavigationViewItem per descriptor at window-build time.
//
// It lives in Deckle.Catalog, the settings floor every settings-bearing module
// already references (the composer was relocated here for the same reason) — so a
// module DECLARES its own nav identity without a reference back to the shell. The
// module owns its name AND its icon; the composition root supplies only the Order.
//
// Deliberately UI-agnostic and late-bound, mirroring the shell's existing nav:
// PageTag is a Type.GetType string (assembly-qualified for a module page in its
// own assembly), resolved the same way OnNavSelectionChanged already resolves the
// static items — so a module's assembly need not be referenced by the shell. The
// label is not carried inline but resolved at render time from the module's OWN
// PRI subtree (Loc.GetFrom(OwningAssembly, LabelKey)), so an installed module
// ships its own nav wording rather than depending on a string compiled into the
// shell. The glyph is a Glyphs.* character (the code-side FontIcon path the
// composer uses), built directly with no resource lookup.
public sealed record SettingsModuleDescriptor
{
    // Stable identity, unique within the registry — the key Register replaces on
    // and Unregister removes by. Conventionally the module's persistence folder id
    // (modules/<Id>/settings.json), so a module reads as one thing across nav and
    // storage.
    public required string Id { get; init; }

    // Type.GetType tag for the page, assembly-qualified for a page living in its
    // own module assembly (e.g. "Deckle.Transcription.WhisperPage, Deckle.Transcription").
    // Resolved by SettingsWindow exactly like the static items' Tag.
    public required string PageTag { get; init; }

    // The module assembly name whose PRI subtree carries the nav label — the same
    // value the composer derives from its source ViewModel's assembly. The shell
    // resolves the label via Loc.GetFrom(OwningAssembly, LabelKey).
    public required string OwningAssembly { get; init; }

    // The .resw key of the nav label inside OwningAssembly's subtree. Defaults to
    // the shared convention key every settings module defines, so a module only
    // overrides it to reuse an existing entry. A bare code-style key (no ".Content"
    // segment): the shell reads it into NavigationViewItem.Content directly, not
    // through an x:Uid.
    public string LabelKey { get; init; } = "SettingsModuleNavLabel";

    // Header glyph character from the Glyphs.* constants (the C# mirror of
    // Icons.xaml), built straight into a FontIcon — no StaticResource lookup, the
    // blessed programmatic path. Module-owned: the module picks its own icon.
    public required string Glyph { get; init; }

    // Which nav band this page lands in. Defaults to Main — the ordinary body,
    // between the shell's own anchors — so a plain module reads as one line without
    // stating a tier. Header / Footer opt into the top or bottom bands.
    public SettingsNavTier Tier { get; init; } = SettingsNavTier.Main;

    // Sort key WITHIN the tier. Ascending; leaves gaps between values so a later
    // module can land between two existing ones.
    public int Order { get; init; }

    // Optional parent module id: set it and this page becomes a CHILD of that
    // module in the rail, nested under an expandable parent item rather than
    // sitting beside it. Left null for the ordinary case — one page per family
    // with in-page sections — and reached for only when a family grows a surface
    // too large to fold into its parent page.
    //
    // A child renders WITHOUT an icon (the WinUI hierarchical-navigation sample:
    // the parent's icon carries the family, indentation carries the rest), so its
    // Glyph serves only the search index, which shows it beside a hit. Children
    // sort among themselves by (Tier, Order) like any other module; a ParentId
    // naming no registered module is skipped, so a child cannot outlive an
    // uninstalled parent in the rail.
    public string? ParentId { get; init; }
}
