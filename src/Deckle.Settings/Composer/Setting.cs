using System;
using System.Collections.Generic;

namespace Deckle.Settings;

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
    public static SettingDescriptor Toggle(
        string labelKey,
        Func<bool> get,
        Action<bool> set,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<bool>? defaultValue = null) => new()
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
        Func<double>? defaultValue = null) => new()
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
        Func<string>? defaultValue = null) => new()
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
        Func<T>? defaultValue = null)
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
            Args = new ChoiceArgs(boxed),
            GetValue = () => get(),
            SetValue = value => set((T)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            // Box the typed default into object? — for a Choice the boxed value is
            // what IndexOfValue/DefaultEquals match against the options, the same
            // currency as GetValue.
            Default = defaultValue is null ? null : () => defaultValue(),
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
        Func<bool>? defaultValue = null) => new()
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
        };
}
