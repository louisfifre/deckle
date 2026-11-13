using System;
using System.Globalization;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Catalog;

// ── Write: the one logging point of the composed settings ───────────────────
//
// Every value a composed card writes, by gesture or by reset, goes through
// Write, which reads the value before and after the setter and logs one line
// when they differ. The value is re-read rather than taken from the control,
// so a setter that clamps or refuses logs what the model actually holds, and a
// write the model already held logs nothing.
public sealed partial class SettingsComposer
{
    private void Write(SettingDescriptor s, object? value)
    {
        object? before = s.GetValue();
        s.SetValue(value);
        LogChange(s, before, s.GetValue());
    }

    private void LogChange(SettingDescriptor s, object? before, object? after)
    {
        if (DefaultEquals(before, after)) return;
        DeckleSettingsUxSource.Log.SettingWritten(
            _module ?? "",
            s.LabelKey,
            SettingValueText.Format(s, before),
            SettingValueText.Format(s, after));
    }
}

// A setting's value as the log may carry it. Only the kinds whose value is
// chosen from what the app offers are written out: a toggle, a number, a
// choice and a hotkey's modifier flags and scancode. Text, a path, an editor's
// shape and a curve may hold what the person typed or a folder naming them,
// so they read "(changed)" and the line says only that the setting moved.
public static class SettingValueText
{
    public const string Withheld = "(changed)";

    public static string Format(SettingDescriptor s, object? value) => s.Kind switch
    {
        SettingKind.Toggle or SettingKind.Group => value is bool b ? (b ? "on" : "off") : Withheld,
        SettingKind.Magnitude => value is double d ? d.ToString("R", CultureInfo.InvariantCulture) : Withheld,
        SettingKind.Choice => FormatChoice(((ChoiceArgs)s.Args!).Options, value),
        SettingKind.Mode => FormatChoice(((ModeArgs)s.Args!).Options, value),
        SettingKind.Hotkey => value is HotkeyChord c ? $"{c.Modifiers}+0x{c.ScanCode:X}" : "unbound",
        _ => Withheld,
    };

    // An enum names itself. Any other option value (a string such as a theme
    // name) is written as the option's resource key, which the code declared,
    // never as the value itself.
    private static string FormatChoice(System.Collections.Generic.IReadOnlyList<ChoiceOption> options, object? value)
    {
        if (value is Enum e) return e.ToString();
        foreach (ChoiceOption option in options)
            if (Equals(option.Value, value)) return option.LabelKey;
        return Withheld;
    }
}