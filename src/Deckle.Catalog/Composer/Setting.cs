using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── Setting ──────────────────────────────────────────────────────────────────
//
// Factories for SettingDescriptor, one per UI kind. They are the strongly-typed
// front door: a Toggle takes Func<bool>/Action<bool>, so the value type can
// never drift from the kind, and the boxing into the composer's object-typed
// selectors happens here, once.
//
// The optional defaultValue selector follows the same front-door boxing as
// get/set: a strongly-typed Func<T> from the call site, boxed into the object?
// Default the composer reads to gate (and act on) its per-row reset. It stays
// optional — a setting with no resettable default omits it and renders no reset
// affordance. Point it at the POCO initializer (e.g. () => new XxxSettings().Prop)
// so the default has exactly one source of truth.
//
// New kinds are added here alongside their case in SettingsComposer, when a real
// setting needs them — we type a control the first time we meet it, not before.
public static class Setting
{
    // confirmOnEnable, when supplied, gates the OFF→ON flip behind an async
    // confirmation (its Task<bool> = "allow the enable?"); the composer holds the
    // write until it resolves. It lives only on Toggle, not the other factories:
    // confirmation is a property of an activation, and the only consumers are leaf
    // consent toggles. Null — the default — leaves the toggle's write synchronous.
    public static SettingDescriptor Toggle(
        string labelKey,
        Func<bool> get,
        Action<bool> set,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<bool>? defaultValue = null,
        Func<XamlRoot, Task<bool>>? confirmOnEnable = null,
        Func<string?>? advisory = null) => new()
        {
            Kind = SettingKind.Toggle,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            GetValue = () => get(),
            SetValue = value => set((bool)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
            ConfirmOnEnable = confirmOnEnable,
            Advisory = advisory,
        };

    // Slider over a double. The range/step live in SliderArgs (the control's
    // bounds, not the value), required because a slider is meaningless without
    // them — unlike Toggle, args is not optional here.
    public static SettingDescriptor Slider(
        string labelKey,
        Func<double> get,
        Action<double> set,
        SliderArgs args,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<double>? defaultValue = null,
        Func<string?>? advisory = null) => new()
        {
            Kind = SettingKind.Slider,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = args,
            GetValue = () => get(),
            SetValue = value => set((double)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
            Advisory = advisory,
        };

    // NumberBox over a double. Like Slider, the range/increments live in
    // NumberArgs (the control's bounds, not the value) and are required — a
    // NumberBox without Minimum/Maximum has no clamp. Same boxed selectors as
    // Slider, including the optional default for the per-card reset.
    public static SettingDescriptor Number(
        string labelKey,
        Func<double> get,
        Action<double> set,
        NumberArgs args,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<double>? defaultValue = null,
        Func<string?>? advisory = null) => new()
        {
            Kind = SettingKind.Number,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = args,
            GetValue = () => get(),
            SetValue = value => set((double)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
            Advisory = advisory,
        };

    // A Slider fused with an editable NumberBox over a double — sweep to
    // approximate, type to be exact. MagnitudeArgs carries only the bounds and
    // unit; the composer derives the slider's "nice" 1-2-5 grain from the range, so
    // unlike Slider there is no StepFrequency to pass. Same boxed selectors and
    // optional default (for the per-card reset) as the other numeric kinds.
    public static SettingDescriptor Magnitude(
        string labelKey,
        Func<double> get,
        Action<double> set,
        MagnitudeArgs args,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<double>? defaultValue = null,
        Func<string?>? advisory = null) => new()
        {
            Kind = SettingKind.Magnitude,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = args,
            GetValue = () => get(),
            SetValue = value => set((double)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
            Advisory = advisory,
        };

    // Free-form text as a string. TextArgs shapes the TextBox (placeholder,
    // multiline, max length) and is optional — a bare single-line field needs no
    // arguments, so unlike Slider/Number it defaults to a plain TextArgs. Same
    // boxed selectors and optional default as the other kinds; the default points
    // at the POCO initializer so an empty/edited field can reset to it.
    public static SettingDescriptor Text(
        string labelKey,
        Func<string> get,
        Action<string> set,
        TextArgs? args = null,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<string>? defaultValue = null,
        Func<string?>? advisory = null) => new()
        {
            Kind = SettingKind.Text,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = args ?? new TextArgs(),
            GetValue = () => get(),
            SetValue = value => set((string)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
            Advisory = advisory,
        };

    // Folder path as a string. PathArgs carries the picker mode and the
    // deferred default-path lookup; like Slider's args it is required, since a
    // FolderPickerCard needs at minimum its mode.
    public static SettingDescriptor Path(
        string labelKey,
        Func<string> get,
        Action<string> set,
        PathArgs args,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<string>? defaultValue = null,
        Func<string?>? advisory = null) => new()
        {
            Kind = SettingKind.Path,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = args,
            GetValue = () => get(),
            SetValue = value => set((string)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
            Advisory = advisory,
        };

    // Choice among a small fixed set, over any value type T (the VM property's
    // type — a string like the theme, a boxed int index, an enum). The options
    // pair each value with its .resw label key; they go into ChoiceArgs so the
    // composer can build the ComboBox and match the getter against them. Generic
    // here, like the typed selectors, so the option values cannot drift from the
    // property type; boxed into object once at this front door.
    public static SettingDescriptor Choice<T>(
        string labelKey,
        Func<T> get,
        Action<T> set,
        IReadOnlyList<(T Value, string LabelKey)> options,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<T>? defaultValue = null,
        Func<string?>? advisory = null)
        => Choice(labelKey, get, set, options, radio: false,
            glyph, isAdvanced, enabledWhen, visibleWhen, defaultValue, advisory);

    // Choice rendered as a flat RadioButtons group rather than a ComboBox — the
    // doctrine's control for "a few" mutually-exclusive options, where showing
    // every option at once reads clearer than a dropdown. Same value semantics as
    // Choice<T> (the options carry the typed values, matched by equality), so a
    // call site swaps Choice→Radio purely to change the rendering, nothing else.
    // A separate factory rather than a flag on Choice so the manifest states the
    // shape by name (Setting.Radio vs Setting.Choice), the way each kind already
    // gets its own named front door.
    public static SettingDescriptor Radio<T>(
        string labelKey,
        Func<T> get,
        Action<T> set,
        IReadOnlyList<(T Value, string LabelKey)> options,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<T>? defaultValue = null,
        Func<string?>? advisory = null)
        => Choice(labelKey, get, set, options, radio: true,
            glyph, isAdvanced, enabledWhen, visibleWhen, defaultValue, advisory);

    // Shared body of Choice/Radio — both are a SettingKind.Choice, differing only
    // in the ChoiceArgs.Radio flag the composer dispatches the rendering on. Kept
    // private so the two public factories stay the strongly-typed front door and
    // the boxing of options/default happens once.
    private static SettingDescriptor Choice<T>(
        string labelKey,
        Func<T> get,
        Action<T> set,
        IReadOnlyList<(T Value, string LabelKey)> options,
        bool radio,
        string? glyph,
        bool isAdvanced,
        Func<bool>? enabledWhen,
        Func<bool>? visibleWhen,
        Func<T>? defaultValue,
        Func<string?>? advisory)
    {
        var boxed = new List<ChoiceOption>(options.Count);
        foreach ((T value, string optionKey) in options)
            boxed.Add(new ChoiceOption(value, optionKey));

        return new()
        {
            Kind = SettingKind.Choice,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = new ChoiceArgs(boxed, radio),
            GetValue = () => get(),
            SetValue = value => set((T)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            // Box the typed default into object? — for a Choice the boxed value is
            // what IndexOfValue/DefaultEquals match against the options, the same
            // currency as GetValue.
            Default = defaultValue is null ? null : () => defaultValue(),
            Advisory = advisory,
        };
    }

    // A master toggle that reveals child settings. Like Toggle, the get/set
    // selectors are the master's own bool — what the composer wires to the
    // ToggleSwitch in the expander header; the children are the dependent
    // settings the composer HIDES while the master is off. The children are
    // declared with the same Setting.* factories, so the group is just "a Toggle
    // that carries a payload of other settings".
    //
    // EnabledWhen/VisibleWhen here gate the GROUP itself (rare — a whole feature
    // unavailable in some context); the per-child masking on the master toggle is
    // implicit and applied by the composer.
    public static SettingDescriptor Group(
        string labelKey,
        Func<bool> get,
        Action<bool> set,
        IReadOnlyList<SettingDescriptor> children,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<bool>? defaultValue = null,
        Func<XamlRoot, Task<bool>>? confirmOnEnable = null) => new()
        {
            Kind = SettingKind.Group,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = new GroupArgs(children),
            GetValue = () => get(),
            SetValue = value => set((bool)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            // The master's own default. The group-header reset combines this (when
            // present) with each child's Default; a master without a resettable
            // default still resets its children.
            Default = defaultValue is null ? null : () => defaultValue(),
            // Like a leaf Toggle, the master may gate its OFF→ON flip behind an async
            // consent dialog — the composer holds the enable until it resolves true, so
            // a consent fold (the Diagnostics corpus opt-in) never transiently switches
            // its feature on. Null leaves the master's write synchronous.
            ConfirmOnEnable = confirmOnEnable,
        };

    // A header-and-chevron grouping with NO master toggle — "Group minus the
    // master". The section has no value of its own (it activates nothing), so
    // unlike Group it carries no get/set/defaultValue: the children declared with
    // the same Setting.* factories carry their own values and defaults, and the
    // composer renders each as a card inside the section's SettingsExpander.
    //
    // GetValue/SetValue must still satisfy the descriptor's `required` selectors,
    // but a valueless node has nothing to read or write — so GetValue returns null
    // and SetValue is a no-op. Nothing dispatches on them for a Section (BuildSection
    // never touches them, and the section registers no per-row dirty-check off its
    // own value, only the section-level reset folded over its children), so the
    // no-ops are never exercised; they exist purely to honour the contract. Default
    // is left null for the same reason — the section has no resettable value, only
    // its children do, and the section-header reset is built from those.
    //
    // EnabledWhen/VisibleWhen here gate the SECTION itself (the whole fold appears
    // or not in some context); children stand on their own VisibleWhen, with no
    // master to compose in.
    public static SettingDescriptor Section(
        string labelKey,
        IReadOnlyList<SettingDescriptor> children,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? visibleWhen = null) => new()
        {
            Kind = SettingKind.Section,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = new SectionArgs(children),
            // Valueless node: read nothing, write nothing. See the note above.
            GetValue = () => null,
            SetValue = _ => { },
            VisibleWhen = visibleWhen,
        };
}
