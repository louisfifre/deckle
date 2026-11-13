using System;
using Deckle.Catalog;
using Deckle.Core;
using Xunit;

namespace Deckle.Catalog.Tests;

// What the one logging point of the composed settings writes for each kind:
// the value where the app chose it, "(changed)" where the person may have typed
// it or where it names a folder.
[Trait("Category", "observability")]
public class SettingValueTextTests
{
    private enum Side { Left, Right }

    [Fact]
    public void AToggleReadsOnOrOff()
    {
        var d = Setting.Toggle("k", () => false, _ => { });
        Assert.Equal("on", SettingValueText.Format(d, true));
        Assert.Equal("off", SettingValueText.Format(d, false));
    }

    [Fact]
    public void ANumberIsWrittenInvariant()
    {
        var d = new SettingDescriptor
        {
            Kind = SettingKind.Magnitude, LabelKey = "k",
            GetValue = () => 0.0, SetValue = _ => { },
        };
        Assert.Equal("-42.5", SettingValueText.Format(d, -42.5));
    }

    [Fact]
    public void AnEnumChoiceNamesItsValue()
    {
        var d = Setting.Choice("k", () => Side.Left, _ => { },
            [(Side.Left, "SideLeft"), (Side.Right, "SideRight")]);
        Assert.Equal("Right", SettingValueText.Format(d, Side.Right));
    }

    [Fact]
    public void AStringChoiceIsWrittenAsItsOptionKeyNotItsValue()
    {
        var d = Setting.Choice("k", () => "Dark", _ => { },
            [("System", "ThemeSystem"), ("Dark", "ThemeDark")]);
        Assert.Equal("ThemeDark", SettingValueText.Format(d, "Dark"));
        Assert.Equal(SettingValueText.Withheld, SettingValueText.Format(d, "not an option"));
    }

    [Fact]
    public void AHotkeyCarriesItsModifiersAndScancodeOrUnbound()
    {
        var d = new SettingDescriptor
        {
            Kind = SettingKind.Hotkey, LabelKey = "k",
            GetValue = () => null, SetValue = _ => { },
        };
        Assert.Equal("Control, Shift+0x1E",
            SettingValueText.Format(d, new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x1E)));
        Assert.Equal("unbound", SettingValueText.Format(d, null));
    }

    [Theory]
    [InlineData(SettingKind.Text)]
    [InlineData(SettingKind.Path)]
    [InlineData(SettingKind.Editor)]
    [InlineData(SettingKind.Curve)]
    public void AKindThatMayHoldWhatThePersonTypedIsWithheld(SettingKind kind)
    {
        var d = new SettingDescriptor
        {
            Kind = kind, LabelKey = "k",
            GetValue = () => null, SetValue = _ => { },
        };
        Assert.Equal(SettingValueText.Withheld,
            SettingValueText.Format(d, @"C:\Users\someone\Documents\note"));
    }
}