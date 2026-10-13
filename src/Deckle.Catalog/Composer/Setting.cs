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
// The optional defaultValue drives the per-row reset; a setting without one
// renders no reset. Point it at the POCO initializer (e.g. () => new
// XxxSettings().Prop) so the default has exactly one source of truth.
public static class Setting
{
    // confirmOnEnable gates the OFF→ON flip behind an async confirmation; the
    // composer holds the write until it resolves true, so a consent toggle never
    // switches its feature on even for a moment. Only Toggle and Group take it:
    // confirmation belongs to an activation.
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

    // MagnitudeArgs is required: a numeric control is meaningless without its
    // bounds. There is no step to pass: the composer derives the grain from the
    // range.
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

    // TextArgs is optional: a bare single-line field needs none.
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

    // Global hotkey chord, null when unbound. HotkeyArgs is required because
    // whether the chord may be cleared is a decision each manifest must state.
    // No enabledWhen: a hotkey card is shown or hidden, never greyed. The
    // composer calls set only with a complete chord, or with null on Clear.
    public static SettingDescriptor Hotkey(
        string labelKey,
        Func<HotkeyChord?> get,
        Action<HotkeyChord?> set,
        HotkeyArgs args,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? visibleWhen = null,
        Func<HotkeyChord?>? defaultValue = null,
        Func<string?>? advisory = null) => new()
        {
            Kind = SettingKind.Hotkey,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = args,
            GetValue = () => get(),
            SetValue = value => set((HotkeyChord?)value),
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
            Advisory = advisory,
        };

    // PathArgs is required: the picker needs at least its mode.
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

    // Choice among a small fixed set, over the VM property's own type T. Each
    // option pairs a value with its .resw label key; generic so the option values
    // cannot drift from the property type.
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

    // A Choice rendered as a RadioButtons group, the doctrine's control for a few
    // options. Same value semantics as Choice<T>, so swapping one for the other
    // changes only the rendering. A separate factory rather than a flag so the
    // manifest names the shape.
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
            // Boxed the same way as GetValue, so DefaultEquals compares like with like.
            Default = defaultValue is null ? null : () => defaultValue(),
            Advisory = advisory,
        };
    }

    // A module-owned editor for a value with a shape (a curve, an envelope). The
    // element binds to the model on its own; get/set exist so the composer can
    // drive the per-card reset.
    public static SettingDescriptor Editor<T>(
        string labelKey,
        Func<T> get,
        Action<T> set,
        EditorArgs args,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<T>? defaultValue = null) => new()
        {
            Kind = SettingKind.Editor,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = args,
            GetValue = () => get(),
            SetValue = value => set((T)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
        };

    // A master toggle that reveals child settings: get/set are the master's own
    // bool, and the composer hides the children while it is off. enabledWhen and
    // visibleWhen gate the whole group.
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
            // The master's own default. The group-header reset combines it with each
            // child's Default; a master without one still resets its children.
            Default = defaultValue is null ? null : () => defaultValue(),
            ConfirmOnEnable = confirmOnEnable,
        };

    // A fold with no master toggle. The section has no value, so it takes no
    // get/set/defaultValue: its children carry their own. GetValue and SetValue
    // only satisfy the descriptor's required members; nothing reads or writes them
    // for a Section. visibleWhen gates the whole fold.
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
            GetValue = () => null,
            SetValue = _ => { },
            VisibleWhen = visibleWhen,
        };

    // A choice that decides which settings exist — a fold whose header carries
    // the choice (settings design system, decision 12). The get/set selectors are
    // the chosen mode, over any value type T like Choice; the children are the
    // settings of the modes, each naming the mode that shows it through its own
    // visibleWhen. A preset family's selector is declared this way, with
    // SelectorOptions() as the options and the hand values as Custom-only
    // children. The fold's reset returns the choice to its default and every
    // defaulted child to its own.
    public static SettingDescriptor Mode<T>(
        string labelKey,
        Func<T> get,
        Action<T> set,
        IReadOnlyList<(T Value, string LabelKey)> options,
        IReadOnlyList<SettingDescriptor> children,
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
            Kind = SettingKind.Mode,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = new ModeArgs(boxed, children),
            GetValue = () => get(),
            SetValue = value => set((T)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
        };
    }

    // A two-point cubic Bézier, edited by the one curve component (settings
    // design system, decision 21). The selectors read and write the four control
    // point values as one CurvePoints; CurveArgs says what frame the curve is
    // drawn in and how to say what it does. The default, when given, is the
    // points the per-card reset writes back.
    public static SettingDescriptor Curve(
        string labelKey,
        Func<CurvePoints> get,
        Action<CurvePoints> set,
        CurveArgs? args = null,
        string? glyph = null,
        bool isAdvanced = false,
        Func<bool>? enabledWhen = null,
        Func<bool>? visibleWhen = null,
        Func<CurvePoints>? defaultValue = null) => new()
        {
            Kind = SettingKind.Curve,
            LabelKey = labelKey,
            Glyph = glyph,
            IsAdvanced = isAdvanced,
            Args = args ?? new CurveArgs(),
            GetValue = () => get(),
            SetValue = value => set((CurvePoints)value!),
            EnabledWhen = enabledWhen,
            VisibleWhen = visibleWhen,
            Default = defaultValue is null ? null : () => defaultValue(),
        };
}
