using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── SettingArgs ──────────────────────────────────────────────────────────────
//
// Base for the per-component arguments a SettingDescriptor can carry — the
// "+ arguments" half of "variable → component + arguments + visibility". A
// component that needs none (a toggle) leaves Args null. Concrete subtypes are
// added with the kind that consumes them, e.g.:
//
//   PathArgs(FolderPickerMode)   — folder picker: "configure" vs "open-only"
//   ChoiceArgs(itemsProvider)    — dropdown whose items are sourced at runtime
//   MagnitudeArgs(min, max)      — numeric bounds, unit and shape
//
// The base is empty on purpose: it is the typed slot the composer dispatches on,
// not a grab-bag. Each subtype stays minimal and specific to its component.
public abstract record SettingArgs;

// ── MagnitudeArgs ─────────────────────────────────────────────────────────────
//
// Bounds and shape of a Magnitude setting, the one numeric block: a number field
// with its arrows, and beside it a slider when the value earns a gesture. There
// is no step: a magnitude derives its "nice" 1-2-5 grain from the range itself
// (MagnitudeGrain.StepFor), so the caller states only where the value lives,
// what it measures and what shape it takes. Unit is the optional suffix beside
// the field ("ms", "x", "dBFS"); null when dimensionless. Slider false keeps the
// field alone, for a figure that is typed rather than swept (a token count, a
// duration nobody drags). Integer says the value is a count: the grain never
// drops below one, so the field shows no decimals and the arrows move whole
// units. Like every *Args these describe the CONTROL — the value type (double)
// is fixed by the kind, and an integer model still travels as a double through
// the selectors.
public sealed record MagnitudeArgs(
    double Minimum,
    double Maximum,
    string? Unit = null,
    bool Slider = true,
    bool Integer = false) : SettingArgs;

// ── TextArgs ──────────────────────────────────────────────────────────────────
//
// Shape of a Text setting's TextBox. All optional, because a bare text field is
// the common case: Placeholder is the greyed prompt shown while the field is
// empty (PlaceholderText — a hint, never a value); Multiline switches the box to
// AcceptsReturn with a bounded height and lays the card out vertically (like
// Path), for a value that wraps — a prompt, a note; MaxLength caps the input the
// box accepts, null for no cap. The value type (string) is fixed by the kind, so
// these describe only the CONTROL, like a slider's bounds.
public sealed record TextArgs(
    string? Placeholder = null,
    bool Multiline = false,
    int? MaxLength = null) : SettingArgs;

// ── EditorArgs ────────────────────────────────────────────────────────────────
//
// The builder of an Editor setting's element. Build is invoked once at compose
// time and returns the element the card hosts; the element keeps itself in sync
// with the model (the composer has no control value to push into it). Unlike a
// Path picker, which is host-owned and built through PathControlFactory, an
// editor is descriptor-owned: the module that declares the setting supplies it.
public sealed record EditorArgs(Func<FrameworkElement> Build) : SettingArgs;

// ── HotkeyArgs ────────────────────────────────────────────────────────────────
//
// Shape of a Hotkey setting. AllowUnbound adds a Clear button that writes null:
// a module toggle hotkey may be left unbound, the Record hotkey may not (it is
// the dictation's only keyboard entry point). The value type (HotkeyChord?) is
// fixed by the kind.
public sealed record HotkeyArgs(bool AllowUnbound) : SettingArgs;

// ── FolderPickerMode ──────────────────────────────────────────────────────────
//
// Whether a folder-path setting lets the user repoint the folder (Configure), let
// the user TYPE a path as well as pick one (Editable), or only reveal it in
// Explorer (OpenOnly). Configure shows a read-only path with Change + Open;
// Editable swaps the read-only readout for a typeable TextBox (still with Change +
// Open) — the faster route when the user is transplanting a folder from another
// machine (a pre-populated models directory, say) rather than browsing to it.
// OpenOnly fits a path the app owns and the user shouldn't move — the row still
// shows where data lands and offers a way in, but hides the "Change" affordance so
// the location reads as fixed.
public enum FolderPickerMode
{
    Configure,
    Editable,
    OpenOnly,
}

// ── PathArgs ──────────────────────────────────────────────────────────────────
//
// Arguments for a Path (folder) setting. Mode selects the FolderPickerCard
// affordance set (see FolderPickerMode). DefaultPath is a deferred lookup, not a
// stored string, because the fallback shown when the value is empty is computed
// at compose time from AppPaths and would be wrong if captured earlier; the
// composer invokes it once when building the card.
public sealed record PathArgs(
    FolderPickerMode Mode = FolderPickerMode.Configure,
    Func<string>? DefaultPath = null) : SettingArgs;

// ── ChoiceOption ────────────────────────────────────────────────────────────────
//
// One entry of a Choice: the Value the setting takes when this option is picked
// (matched against the getter by value-equality to drive the selection — a string
// like "Dark" or a boxed int index, whatever the VM property is), paired with the
// LabelKey of its .resw "<LabelKey>.Content" entry — the same key a ComboBoxItem's
// x:Uid would carry. Built by the Setting.Choice factory from typed (value, key)
// pairs, so the value type is checked against the selectors at the call site.
public sealed record ChoiceOption(object? Value, string LabelKey);

// ── ChoiceArgs ──────────────────────────────────────────────────────────────────
//
// The options of a Choice setting, in display order. Rendered as a ComboBox by
// default (the doctrine's control for "more than a few" mutually-exclusive
// options); when Radio is set the composer renders a RadioButtons group instead —
// the doctrine's control for "a few", where laying every option flat and visible
// beats hiding them behind a dropdown. The options belong in args, like a slider's
// bounds: they describe the control's shape, while the value type is fixed by the
// kind's selectors. Radio defaults false, so existing ComboBox call sites that
// pass only the options stay unchanged.
public sealed record ChoiceArgs(
    IReadOnlyList<ChoiceOption> Options,
    bool Radio = false) : SettingArgs;

// ── GroupArgs ─────────────────────────────────────────────────────────────────
//
// The child settings a Group reveals, in display order. They are ordinary leaf
// descriptors (Toggle, Magnitude, Path, Choice) — the composer renders each as a
// SettingsCard inside the group's SettingsExpander and HIDES them while the
// master toggle is off (Microsoft-first dependency gating masks, never greys).
// Like a slider's bounds or a choice's options, the
// children describe the control's shape and so belong in args, not in the
// descriptor's value selectors (which carry the master toggle's own bool).
//
// Children may not themselves be groups: folds never nest (settings-UX doctrine),
// and the composer enforces it.
public sealed record GroupArgs(IReadOnlyList<SettingDescriptor> Children) : SettingArgs;

// ── SectionArgs ───────────────────────────────────────────────────────────────
//
// The child settings a Section groups, in display order — mirroring GroupArgs but
// with no master selectors, because a section has no value of its own and gates
// nothing. The children are ordinary leaf descriptors the composer renders as
// SettingsCards inside the section's SettingsExpander; unlike a group they are NOT
// masked by a master toggle (there is none), so each stands on its own VisibleWhen.
//
// Children may not themselves be groups or sections: folds never nest (settings-UX
// doctrine), and the composer enforces it — the same guard BuildGroup applies.
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

// ── CurveArgs ─────────────────────────────────────────────────────────────────
//
// Shape of a Curve setting's editor, the one curve component of the settings
// design system (decision 21). Frame is read at compose time and on every
// refresh, so the plot follows the bounds the sibling cards set (the hangover
// anchors); null draws the unit square alone. Describe turns the points into the
// sentence the card shows as its description, re-read on every refresh; null
// keeps the .resw description. Floor is an optional horizontal guide in data y
// (the Ambient brightness floor). AllowOutside lets a handle's Y leave [0,1] by
// a quarter, for a curve that overshoots.
public sealed record CurveArgs(
    Func<CurveFrame>? Frame = null,
    Func<CurvePoints, string?>? Describe = null,
    Func<double?>? Floor = null,
    bool AllowOutside = false) : SettingArgs;
