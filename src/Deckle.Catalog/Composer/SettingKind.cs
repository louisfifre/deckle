namespace Deckle.Catalog;

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

    // A Slider and an editable NumberBox over a double, fused into one control:
    // sweep the slider to approximate, type the box for an exact figure, both
    // driving the same value. The grain is not declared — it is derived as a
    // "nice" 1-2-5 step from the range (MagnitudeArgs carries only bounds + unit) —
    // so this is the numeric control for a bounded value worth BOTH a gesture and a
    // precise entry (a threshold, a duration, a rate). It does not replace Slider
    // (pure gesture, read-only readout) or Number (pure entry, no track); it is the
    // third numeric kind, for when both inputs earn their place.
    Magnitude,

    // A TextBox over a string, for free-form text the user types — a name, a
    // label, a prompt fragment, an endpoint — where the value is open rather than
    // chosen from a set (Choice) or pointed at on disk (Path). Single-line by
    // default; the optional placeholder, multiline shape and max length live in
    // TextArgs.
    Text,

    // A master toggle that reveals dependent child settings, rendered as a
    // SettingsExpander (toggle in the header, children as expanded rows). The
    // descriptor's own value IS the master toggle (a bool); the children live in
    // GroupArgs. The one structural kind — the doctrine's "inline disclosure for
    // the fine configuration of something activatable" — and the only one whose
    // value gates other settings. Folds never nest, so a group's children are
    // leaf kinds, never groups themselves.
    Group,

    // A header-and-chevron grouping with NO master toggle — "Group minus the
    // master". Rendered as the same SettingsExpander, hosting child cards in its
    // Items, but the header carries only the optional section-level reset, not a
    // ToggleSwitch: the section has no value of its own and gates nothing. It is
    // the structural fold for "a few related settings worth collapsing together"
    // where there is nothing to activate — the children stand on their own
    // VisibleWhen, and the section node itself is valueless. Like Group, folds
    // never nest, so a section's children are leaf kinds.
    Section,
}
