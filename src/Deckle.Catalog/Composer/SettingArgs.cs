using System;
using System.Collections.Generic;

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
//   SliderArgs(min, max, step)   — slider range and increment
//
// The base is empty on purpose: it is the typed slot the composer dispatches on,
// not a grab-bag. Each subtype stays minimal and specific to its component.
public abstract record SettingArgs;

// ── SliderArgs ────────────────────────────────────────────────────────────────
//
// Range and increment for a Slider setting, mirroring the Slider properties the
// composer sets (Minimum/Maximum/StepFrequency). Unit is the optional suffix
// rendered next to the value readout ("dBFS", "%", "ms"); null when the value is
// dimensionless (e.g. the curve exponent). These belong in args, not the
// descriptor's selectors, because they describe the CONTROL's bounds — the
// value type (double) is fixed by the kind, the bounds vary per setting.
public sealed record SliderArgs(
    double Minimum,
    double Maximum,
    double StepFrequency,
    string? Unit = null) : SettingArgs;

// ── NumberArgs ────────────────────────────────────────────────────────────────
//
// Range and increments for a Number setting, mirroring the NumberBox properties
// the composer sets (Minimum/Maximum/SmallChange/LargeChange — keyboard arrow vs
// PageUp/PageDown nudge). No Unit, unlike SliderArgs: the segmenter cards read as
// bare figures (the unit lives in the card's header/description), so a trailing
// suffix would only add chrome. Like a slider's bounds these describe the
// CONTROL, not the value type (double, fixed by the kind).
public sealed record NumberArgs(
    double Minimum,
    double Maximum,
    double SmallChange,
    double LargeChange) : SettingArgs;

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
// descriptors (Toggle, Slider, Path, Choice) — the composer renders each as a
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
