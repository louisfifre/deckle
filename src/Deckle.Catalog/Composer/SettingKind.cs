namespace Deckle.Catalog;

// ── SettingKind ──────────────────────────────────────────────────────────────
//
// The control family a SettingDescriptor renders as. Each value has exactly one
// case in SettingsComposer and one factory on Setting. The set grows as real
// settings demand it and shrinks when a kind stops earning its place (Slider and
// Number folded into Magnitude), but a value is added only together with its
// composer case, never speculatively.
public enum SettingKind
{
    Toggle,
    Path,
    Choice,

    // The one numeric block, over a double: an editable NumberBox with its arrows
    // on hover, and beside it a Slider when the value earns a gesture (MagnitudeArgs
    // hides the slider for a figure that is typed, never swept). Sweep to
    // approximate, type or nudge for the exact figure, both driving the same
    // value. The grain is not declared — it is derived as a "nice" 1-2-5 step from
    // the range, floored at one for a count — so a magnitude declares only bounds,
    // unit and shape. The model is written once per gesture: at the release of
    // the slider, at the commit of the field.
    Magnitude,

    // A TextBox over a string, for free-form text the user types — a name, a
    // label, a prompt fragment, an endpoint — where the value is open rather than
    // chosen from a set (Choice) or pointed at on disk (Path). Single-line by
    // default; the optional placeholder, multiline shape and max length live in
    // TextArgs.
    Text,

    // A module-owned editor for a value with a shape — a curve, an envelope —
    // where the doctrine wants a dedicated editor rather than a row of raw numbers.
    // The module builds the element (EditorArgs.Build) and keeps it in sync with
    // the model itself (x:Bind or its own events); the composer hosts it vertically
    // in the card and owns the chrome around it: visibility, enablement and, when
    // a Default is supplied, the per-card reset.
    Editor,

    // A global hotkey chord over a HotkeyChord? (null = unbound): the modifiers
    // as four toggle buttons (Ctrl, Shift, Alt, Win) plus one button that
    // captures the key. Toggle buttons, not a pressed chord, because pressing
    // Win alone opens Start before the page sees it. The value is written only
    // when complete (a modifier and a key) or cleared to null; HotkeyArgs says
    // whether clearing is allowed.
    Hotkey,

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

    // A choice that decides which settings exist, rendered as a SettingsExpander
    // whose header carries the choice (a ComboBox) instead of a toggle — the mode
    // fold of the settings design system (decision 12). The descriptor's own value
    // is the chosen mode; the children in ModeArgs are the settings of the modes,
    // each naming the mode that shows it through its VisibleWhen, and the fold
    // shows one information card when the chosen mode has nothing to set. A
    // preset selector is a mode fold too: Custom is the mode that reveals the
    // hand values. Like the other folds it never nests.
    Mode,
}
