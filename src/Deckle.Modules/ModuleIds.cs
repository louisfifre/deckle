namespace Deckle.Modules;

// ── ModuleIds ─────────────────────────────────────────────────────────────────
//
// The ids the presence file stores, the selector's wording is keyed by, and the
// composition root gates on. They live here, not with the descriptors in the
// composition root (AppModules), because more than one consumer speaks them:
// the App declares and gates, the wizard's selector routes on them. An id is
// vocabulary; a descriptor (glyph, dependency edges) is composition knowledge.
public static class ModuleIds
{
    public const string Transcription   = "transcription";
    public const string Rewrite         = "rewrite";
    public const string Autocorrect     = "autocorrect";
    public const string Ambient         = "ambient";
    public const string Trackpad        = "trackpad";
    public const string PrecisionScroll = "precision-scroll";
    public const string Anytype         = "anytype";
}
