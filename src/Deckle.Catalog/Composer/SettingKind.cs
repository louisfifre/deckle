namespace Deckle.Catalog;

// ── SettingKind ──────────────────────────────────────────────────────────────
//
// The control family a SettingDescriptor renders as. Each value has exactly one
// case in SettingsComposer and one factory on Setting; a value is added only
// together with its composer case, never speculatively.
public enum SettingKind
{
    Toggle,
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
}
