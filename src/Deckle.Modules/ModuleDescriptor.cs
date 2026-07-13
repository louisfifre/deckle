namespace Deckle.Modules;

// ── ModuleDescriptor ──────────────────────────────────────────────────────────
//
// One user-facing Deckle module as the presence catalogue knows it: a stable id,
// its place in the selector, the modules it needs, and how to tell whether its
// heavy assets are on disk. Presence — the user chose to have the module
// installed — is one axis; runtime activation (a module's own Enabled toggle)
// is another, below it: an unchecked module is GONE (engine not composed,
// settings pages not registered), a disabled one is still installed.
//
// Deliberately lean, and deliberately NOT self-described by the module it names,
// unlike SettingsModuleDescriptor: the end state of the installer companion is a
// catalogue that can describe a module whose DLLs are not on disk yet, so the
// descriptor (and the selector's wording, keyed by Id in the selector's own
// resources) must live with the catalogue's owner — the composition root — not
// inside the module assembly it describes.
public sealed record ModuleDescriptor
{
    // Stable identity, unique within the registry. Convention: lowercase ASCII,
    // no spaces — the same shape as the per-module persistence folder ids
    // (modules/<id>/), and the key the presence file stores.
    public required string Id { get; init; }

    // Selector glyph, a Segoe Fluent character. Carried as a raw string so this
    // module stays UI-free; declarers that reference Deckle.Catalog pass a
    // Glyphs.* constant.
    public required string Glyph { get; init; }

    // Ids of the modules this one needs to function. The selector cascades on
    // these edges: checking this module checks its dependencies, unchecking a
    // dependency unchecks this module. Presence-level edges only — support
    // assemblies compiled into the app are not modules and never appear here.
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    // Whether the module's heavy assets (native runtimes, model weights, pinned
    // binaries) are on disk. Null when the module has nothing to provision —
    // its DLLs compiled into the app are all it needs. Distinct from presence:
    // a module can be chosen (present) and not yet provisioned; the wizard's
    // install step is what closes that gap.
    public Func<bool>? IsProvisioned { get; init; }

    // Sort key for the selector. Ascending; leaves gaps between values so a
    // later module can land between two existing ones.
    public int Order { get; init; }
}
