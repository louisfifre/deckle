using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── SettingArgs ──────────────────────────────────────────────────────────────
//
// Base for the per-kind arguments a SettingDescriptor carries; a kind that needs
// none (a toggle) leaves Args null. Empty on purpose: it is the typed slot the
// composer dispatches on, and each subtype stays specific to its kind.
public abstract record SettingArgs;

// ── MagnitudeArgs ─────────────────────────────────────────────────────────────
//
// Bounds and shape of a Magnitude setting. There is no step: the grain is derived
// from the range (MagnitudeGrain.StepFor). Unit ("ms", "×", "dBFS") joins the
// card's header in parentheses and the reset tooltip's value, never the row,
// so every field ends at the same edge. Slider false keeps the field alone, for a figure
// that is typed rather than swept. Integer marks a count: the grain never drops
// below one, so the field shows no decimals. An integer model still travels as a
// double through the selectors.
public sealed record MagnitudeArgs(
    double Minimum,
    double Maximum,
    string? Unit = null,
    bool Slider = true,
    bool Integer = false) : SettingArgs;

// ── TextArgs ──────────────────────────────────────────────────────────────────
//
// Shape of a Text setting's TextBox. Placeholder is a hint, never a value.
// Multiline accepts Return, bounds the height and hosts the field on its own row.
// MaxLength null means no cap.
public sealed record TextArgs(
    string? Placeholder = null,
    bool Multiline = false,
    int? MaxLength = null) : SettingArgs;

// ── EditorArgs ────────────────────────────────────────────────────────────────
//
// The builder of an Editor setting's element, invoked once at compose time. The
// element keeps itself in sync with the model. Unlike the host-owned Path picker,
// an editor is supplied by the module that declares the setting.
public sealed record EditorArgs(Func<FrameworkElement> Build) : SettingArgs;

// ── HotkeyArgs ────────────────────────────────────────────────────────────────
//
// AllowUnbound adds a Clear button that writes null: a module toggle hotkey may
// be left unbound, the Record hotkey may not (it is the dictation's only keyboard
// entry point).
public sealed record HotkeyArgs(bool AllowUnbound) : SettingArgs;

// ── FolderPickerMode ──────────────────────────────────────────────────────────
//
// Configure: a read-only path with Change and Open. Editable: the same with a
// typeable TextBox, for a path pasted from another machine rather than browsed
// to. OpenOnly: a path the app owns; the row shows it and offers Open, no Change.
public enum FolderPickerMode
{
    Configure,
    Editable,
    OpenOnly,
}

// ── PathArgs ──────────────────────────────────────────────────────────────────
//
// DefaultPath is a deferred lookup, not a stored string: the fallback shown when
// the value is empty comes from AppPaths and would be wrong if captured earlier.
// The host's PathControlFactory invokes it once when building the card.
public sealed record PathArgs(
    FolderPickerMode Mode = FolderPickerMode.Configure,
    Func<string>? DefaultPath = null) : SettingArgs;

// ── ChoiceOption ────────────────────────────────────────────────────────────────
//
// One entry of a Choice. Value is matched against the getter by equality to drive
// the selection. LabelKey names the .resw "<LabelKey>.Content" entry, the key a
// ComboBoxItem's x:Uid would carry.
public sealed record ChoiceOption(object? Value, string LabelKey);

// ── ChoiceArgs ──────────────────────────────────────────────────────────────────
//
// The options of a Choice setting, in display order. A ComboBox by default, the
// doctrine's control for more than a few options; Radio renders a RadioButtons
// group, its control for a few.
public sealed record ChoiceArgs(
    IReadOnlyList<ChoiceOption> Options,
    bool Radio = false) : SettingArgs;

// ── GroupArgs ─────────────────────────────────────────────────────────────────
//
// The leaf settings a Group reveals, in display order, hidden while the master
// toggle is off: dependents are masked, never greyed. Children may not be groups
// or sections: folds never nest (settings-UX doctrine), and the composer enforces
// it.
public sealed record GroupArgs(IReadOnlyList<SettingDescriptor> Children) : SettingArgs;

// ── SectionArgs ───────────────────────────────────────────────────────────────
//
// The leaf settings a Section groups, in display order. There is no master, so
// each child stands on its own VisibleWhen. Folds never nest; the composer
// enforces it.
public sealed record SectionArgs(IReadOnlyList<SettingDescriptor> Children) : SettingArgs;

// ── ModeArgs ──────────────────────────────────────────────────────────────────
//
// The options of a Mode fold's choice (the same shape a Choice carries, rendered
// as a ComboBox in the fold's header) and the child settings the modes reveal, in
// display order. A child names the mode that shows it through its own
// VisibleWhen (`() => Preset == "custom"`); a child with no VisibleWhen shows in
// every mode. The composer adds an information card, shown when no child is
// visible, so a mode with nothing to set still says so.
//
// Children may not themselves be folds: folds never nest, and the composer
// enforces it as it does for Group and Section.
public sealed record ModeArgs(
    IReadOnlyList<ChoiceOption> Options,
    IReadOnlyList<SettingDescriptor> Children) : SettingArgs;

// ── CurveAnchor ───────────────────────────────────────────────────────────────
//
// One value of the frame a curve is drawn in, edited as a field in the editor's
// legend beside the plot: the curve exception of the settings-UX doctrine puts a
// curve's values in fields beside the drawing. The legend slot is a first form
// of the legend block left open, still to confirm with Louis. LabelKey resolves
// "<LabelKey>/Header" as the field's caption and "<LabelKey>/Description" as its
// tooltip, from the module's .resw like a card. The bounds clamp the field; the
// value is written at the field's commit, the gesture rule. Default, when given,
// joins the card's reset: the one wheel restores the curve and its frame.
public sealed record CurveAnchor(
    string LabelKey,
    Func<double> Get,
    Action<double> Set,
    double Minimum,
    double Maximum,
    bool Integer = false,
    Func<double>? Default = null);

// ── CurveArgs ─────────────────────────────────────────────────────────────────
//
// Shape of a Curve setting's editor, the one curve component of the settings
// surfaces. Frame is read at compose time and on every refresh, so the plot
// follows the bounds the anchors set; null draws the unit square alone.
// Anchors are those bounds as legend fields beside the plot, in display order;
// null shows the Bézier fields alone. Describe turns the points into the
// sentence the card shows as its description, re-read on every refresh; null
// keeps the .resw description. Floor is an optional horizontal guide in data y
// (the Ambient brightness floor). AllowOutside lets a handle's Y leave [0,1] by
// a quarter, for a curve that overshoots.
public sealed record CurveArgs(
    Func<CurveFrame>? Frame = null,
    Func<CurvePoints, string?>? Describe = null,
    Func<double?>? Floor = null,
    bool AllowOutside = false,
    IReadOnlyList<CurveAnchor>? Anchors = null) : SettingArgs;
