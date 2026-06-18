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
        Func<bool>? visibleWhen = null) => new()
        {
            Kind = SettingKind.Toggle,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            GetValue = () => get(),
            SetValue = value => set((bool)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
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
        Func<bool>? visibleWhen = null) => new()
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
        Func<bool>? visibleWhen = null) => new()
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
        Func<bool>? visibleWhen = null)
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
        };
    }
}
