using System;
using System.Collections.Generic;

namespace Deckle.Settings;

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

// ── FolderPickerMode ──────────────────────────────────────────────────────────
//
// Whether a folder-path setting lets the user repoint the folder (Configure) or
// only reveal it in Explorer (OpenOnly). OpenOnly fits a path the app owns and
// the user shouldn't move — the row still shows where data lands and offers a way
// in, but hides the "Change" affordance so the location reads as fixed.
public enum FolderPickerMode
{
    Configure,
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
// The options of a Choice setting, in display order. Rendered as a ComboBox today
// (the doctrine's control for "more than a few" mutually-exclusive options); a
// radio style is a future field added with the first clean radio site, not before.
// The options belong in args, like a slider's bounds: they describe the control's
// shape, while the value type is fixed by the kind's selectors.
public sealed record ChoiceArgs(IReadOnlyList<ChoiceOption> Options) : SettingArgs;

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
