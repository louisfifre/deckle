namespace Deckle.Catalog;

// ── SettingKind ──────────────────────────────────────────────────────────────
//
// The control family a SettingDescriptor renders as. Each value has exactly one
// case in SettingsComposer and one factory on Setting; a value is added only
// together with its composer case, never speculatively.
public enum SettingKind
{
    Toggle,

    // A folder on disk. Standing alone it renders as a SettingsExpander headed
    // by the setting's title and description, holding the Location line; as a
    // fold's child it is the Location line alone. PathArgs says what the line
    // offers.
    Path,

    Choice,

    // An editable NumberBox over a double, with a Slider beside it when the value
    // earns a gesture. The grain is derived from the range, so a magnitude
    // declares only bounds, unit and shape. The model is written once per
    // gesture: at the release of the slider, at the commit of the field.
    Magnitude,

    // Free-form text, open rather than chosen from a set (Choice) or pointed at
    // on disk (Path).
    Text,

    // A module-owned editor for a value with a shape (a curve, an envelope). The
    // module builds the element and keeps it in sync with the model; the composer
    // owns visibility, enablement and the per-card reset.
    Editor,

    // A global hotkey chord, null when unbound. Written only when complete (a
    // modifier and a key) or cleared to null.
    Hotkey,

    // A master toggle that reveals dependent child settings, rendered as a
    // SettingsExpander; the only kind whose value gates other settings. Folds
    // never nest, so its children are leaf kinds.
    Group,

    // A fold with no master toggle: valueless, gates nothing, its children stand
    // on their own VisibleWhen. Folds never nest, so its children are leaf kinds.
    Section,

    // A choice that decides which settings exist, rendered as a SettingsExpander
    // whose header carries the choice (a ComboBox) instead of a toggle — the mode
    // fold. The descriptor's own value is the chosen mode; the children in
    // ModeArgs are the settings of the modes, each naming the mode that shows it
    // through its VisibleWhen, and the fold shows one information card when the
    // chosen mode has nothing to set. A preset selector is a mode fold too:
    // Custom is the mode that reveals the hand values. Like the other folds it
    // never nests.
    Mode,

    // A two-point cubic Bézier over CurvePoints, drawn by the one curve editor:
    // the drawing takes the card's width with its two handles, the four values
    // are fields in a two-by-two grid beside it, and the card's description is
    // the sentence computed from the points. A handle writes at its release, a
    // field at its commit, a key nudge at once. CurveArgs carries the frame the
    // curve is drawn in and the sentence.
    Curve,
}
