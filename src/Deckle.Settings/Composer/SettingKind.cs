namespace Deckle.Settings;

// ── SettingKind ──────────────────────────────────────────────────────────────
//
// The control family a SettingDescriptor renders as. Each value has exactly one
// case in SettingsComposer and one factory on Setting. The set grows as real
// settings demand it — the pages inventory shows the next needed kind is Text —
// but a value is added only together with its composer case, never speculatively.
public enum SettingKind
{
    Toggle,
    Slider,
    Path,
    Choice,

    // A NumberBox over a double, for an exact numeric value the user types (or
    // nudges by SmallChange/LargeChange) rather than sweeps on a slider — the
    // segmenter's millisecond durations and dBFS threshold, where the precise
    // figure matters more than the gesture. Range/step live in NumberArgs.
    Number,

    // A master toggle that reveals dependent child settings, rendered as a
    // SettingsExpander (toggle in the header, children as expanded rows). The
    // descriptor's own value IS the master toggle (a bool); the children live in
    // GroupArgs. The one structural kind — the doctrine's "inline disclosure for
    // the fine configuration of something activatable" — and the only one whose
    // value gates other settings. Folds never nest, so a group's children are
    // leaf kinds, never groups themselves.
    Group,
}
