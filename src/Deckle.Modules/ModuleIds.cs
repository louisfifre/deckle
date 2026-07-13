namespace Deckle.Modules;

// ── ModuleIds ─────────────────────────────────────────────────────────────────
//
// The shared vocabulary of the presence model — the ids the presence file
// stores, the selector's wording is keyed by, and the composition root gates
// on. The ids live here (not with the descriptors in the composition root)
// because more than one consumer speaks them: the App declares and gates, the
// wizard's selector routes on them. Lowercase ASCII, the same shape as the
// per-module persistence folder ids.
//
// The descriptors themselves — glyph, dependency edges, provisioning probes —
// stay declared by the composition root (AppModules): an id is vocabulary,
// a descriptor is composition knowledge.
public static class ModuleIds
{
    public const string Transcription = "transcription";
    public const string Rewrite       = "rewrite";
    public const string Autocorrect   = "autocorrect";
    public const string Ambient       = "ambient";
    public const string Trackpad      = "trackpad";
    public const string Anytype       = "anytype";
}
