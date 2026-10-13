using System;
using System.Globalization;

namespace Deckle.Catalog;

// The default of a setting as the words its reset tooltip shows ("Reset to
// 0.50", "Reset to 250 ms", "Reset to No limit"). The description of a card no
// longer states the default (settings design system, decision 13), so the
// tooltip is the one place the user reads it, and it must read exactly like
// the control beside it: the magnitude on its display grain with its unit, the
// choice by its option label, the chord as Windows writes it. Pure formatting,
// kept apart from the WinUI composer so each form can be tested without a
// control. A null result means the default has no readable form (an empty
// text, an option the set no longer exposes) and the composer falls back to the
// generic "Reset to default".
public static class DefaultValueText
{
    // The magnitude written as its field shows it: snapped to the display grain,
    // with the fixed number of decimals the grain needs and the unit after a
    // space. The provider defaults to the current culture so the decimal
    // separator matches the field; tests pass the invariant culture.
    public static string Magnitude(double value, int decimals, string? unit, IFormatProvider? provider = null)
    {
        int digits = Math.Clamp(decimals, 0, 6);
        string number = MagnitudeGrain.Snap(value, digits)
            .ToString("F" + digits, provider ?? CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(unit) ? number : number + " " + unit;
    }

    // The label of the option whose value equals the default, resolved through
    // the caller (the composer reads the module's .resw). Null when no option
    // carries that value.
    public static string? Choice(ChoiceArgs args, object? value, Func<string, string> optionLabel)
    {
        foreach (ChoiceOption option in args.Options)
            if (Equals(option.Value, value)) return optionLabel(option.LabelKey);
        return null;
    }

    // A text or a path as it is; null when blank, which has no readable form.
    public static string? Text(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
