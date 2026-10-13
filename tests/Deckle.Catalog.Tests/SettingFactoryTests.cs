using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// Behavior tests on the Setting.* factories, the contract between a manifest
// author and the composer. They check what a factory produces (kind, args, boxed
// selectors, default, advisory), not how the composer renders it: descriptors are
// pure records with no UI.
[Trait("Category", "unit")]
public class SettingFactoryTests
{
    // ── Toggle: boxed round trip and default ───────────────────────────────────

    [Fact]
    public void ToggleRoundTripsThroughBoxedSelectorsAndCarriesItsDefault()
    {
        bool value = false;
        var d = Setting.Toggle("k", () => value, v => value = v, defaultValue: () => true);

        Assert.Equal(SettingKind.Toggle, d.Kind);

        d.SetValue(true);
        Assert.True(value);
        Assert.Equal(true, d.GetValue());

        Assert.NotNull(d.Default);
        Assert.Equal(true, d.Default!());
    }

    [Fact]
    public void AnOmittedDefaultLeavesNoResettableDefault()
    {
        // With no Default the composer renders no reset.
        var d = Setting.Toggle("k", () => false, _ => { });
        Assert.Null(d.Default);
    }

    [Fact]
    public async Task ToggleAndGroupCarryTheSameLiveEnableConfirmationContract()
    {
        bool allow = false;
        int requests = 0;
        Task<bool> Confirm(Microsoft.UI.Xaml.XamlRoot _)
        {
            requests++;
            return Task.FromResult(allow);
        }

        var toggle = Setting.Toggle("toggle", () => false, _ => { }, confirmOnEnable: Confirm);
        var group = Setting.Group("group", () => false, _ => { }, [], confirmOnEnable: Confirm);

        Assert.NotNull(toggle.ConfirmOnEnable);
        Assert.NotNull(group.ConfirmOnEnable);
        Assert.False(await toggle.ConfirmOnEnable!(null!));

        allow = true;
        Assert.True(await group.ConfirmOnEnable!(null!));
        Assert.Equal(2, requests);
    }

    // ── Text: default args, supplied args, round trip ──────────────────────────

    [Fact]
    public void TextWithoutArgsDefaultsToAPlainSingleLineField()
    {
        var d = Setting.Text("k", () => "", _ => { });

        Assert.Equal(SettingKind.Text, d.Kind);
        var args = Assert.IsType<TextArgs>(d.Args);
        Assert.Null(args.Placeholder);
        Assert.False(args.Multiline);
        Assert.Null(args.MaxLength);
    }

    [Fact]
    public void TextCarriesItsShapeArgsAndRoundTripsTheString()
    {
        string value = "";
        var d = Setting.Text("k", () => value, v => value = v,
            new TextArgs(Placeholder: "hint", Multiline: true, MaxLength: 5),
            defaultValue: () => "def");

        var args = Assert.IsType<TextArgs>(d.Args);
        Assert.Equal("hint", args.Placeholder);
        Assert.True(args.Multiline);
        Assert.Equal(5, args.MaxLength);

        d.SetValue("typed");
        Assert.Equal("typed", value);
        Assert.Equal("typed", d.GetValue());
        Assert.Equal("def", d.Default!());
    }

    // ── Choice vs Radio: same kind, told apart by the Radio flag ───────────────

    [Fact]
    public void RadioIsAChoiceWithTheRadioFlagSetAndOptionsMappedInOrder()
    {
        var d = Setting.Radio<string>("k", () => "a", _ => { },
            new[] { ("a", "KeyA"), ("b", "KeyB") });

        Assert.Equal(SettingKind.Choice, d.Kind);
        var args = Assert.IsType<ChoiceArgs>(d.Args);
        Assert.True(args.Radio);
        Assert.Equal(2, args.Options.Count);
        Assert.Equal("a", args.Options[0].Value);
        Assert.Equal("KeyA", args.Options[0].LabelKey);
        Assert.Equal("b", args.Options[1].Value);
        Assert.Equal("KeyB", args.Options[1].LabelKey);
    }

    [Fact]
    public void ChoiceIsTheSameKindWithTheRadioFlagClear()
    {
        var d = Setting.Choice<string>("k", () => "a", _ => { },
            new[] { ("a", "KeyA") });

        Assert.Equal(SettingKind.Choice, d.Kind);
        Assert.False(Assert.IsType<ChoiceArgs>(d.Args).Radio);
    }

    [Fact]
    public void ChoiceRoundTripsItsTypedValueThroughTheBoxedSelectors()
    {
        string value = "a";
        var d = Setting.Choice<string>("k", () => value, v => value = v,
            new[] { ("a", "KeyA"), ("b", "KeyB") }, defaultValue: () => "b");

        d.SetValue("b");
        Assert.Equal("b", value);
        Assert.Equal("b", d.GetValue());
        Assert.Equal("b", d.Default!());
    }

    // ── Section: a valueless fold with no master, holding its children ─────────

    [Fact]
    public void SectionIsValuelessAndHoldsItsChildren()
    {
        var child = Setting.Toggle("child", () => false, _ => { });
        var s = Setting.Section("sec", new[] { child }, glyph: "G");

        Assert.Equal(SettingKind.Section, s.Kind);
        Assert.Equal("G", s.Glyph);

        // The write must not throw: a valueless node ignores it.
        Assert.Null(s.GetValue());
        s.SetValue("anything");
        Assert.Null(s.Default);

        var args = Assert.IsType<SectionArgs>(s.Args);
        var only = Assert.Single(args.Children);
        Assert.Equal("child", only.LabelKey);
    }

    [Fact]
    public void SectionVisibleWhenGatesTheWholeFold()
    {
        bool visible = false;
        var s = Setting.Section("sec", Array.Empty<SettingDescriptor>(), visibleWhen: () => visible);

        Assert.NotNull(s.VisibleWhen);
        Assert.False(s.VisibleWhen!());
        visible = true;
        Assert.True(s.VisibleWhen!());
    }

    // ── Advisory: a live channel, re-evaluated like VisibleWhen ────────────────

    [Fact]
    public void AdvisoryIsLiveAndReEvaluatedEachCall()
    {
        string? message = null;
        var d = Setting.Text("k", () => "", _ => { }, advisory: () => message);

        Assert.NotNull(d.Advisory);
        Assert.Null(d.Advisory!());

        message = "beware";
        Assert.Equal("beware", d.Advisory!());   // the state changed, the advisory follows
    }

    // ── Path: the Editable mode and the deferred default path ──────────────────

    [Fact]
    public void PathCarriesTheEditableModeAndItsDeferredDefaultPath()
    {
        var d = Setting.Path("k", () => "", _ => { },
            new PathArgs(FolderPickerMode.Editable, DefaultPath: () => "D:/models"));

        Assert.Equal(SettingKind.Path, d.Kind);
        var args = Assert.IsType<PathArgs>(d.Args);
        Assert.Equal(FolderPickerMode.Editable, args.Mode);
        Assert.NotNull(args.DefaultPath);
        Assert.Equal("D:/models", args.DefaultPath!());   // resolved at call time, not captured
    }

    // ── Magnitude: bounds and control shape live in the args ───────────────────

    [Fact]
    public void MagnitudeDefaultsToASliderOverAPlainRange()
    {
        var d = Setting.Magnitude("k", () => 0, _ => { }, new MagnitudeArgs(0.25, 3.0, Unit: "×"));

        Assert.Equal(SettingKind.Magnitude, d.Kind);
        var args = Assert.IsType<MagnitudeArgs>(d.Args);
        Assert.Equal(0.25, args.Minimum);
        Assert.Equal(3.0, args.Maximum);
        Assert.Equal("×", args.Unit);
        Assert.True(args.Slider);
        Assert.False(args.Integer);
    }

    [Fact]
    public void MagnitudeCarriesTheFieldOnlyAndCountShapes()
    {
        var d = Setting.Magnitude("k", () => 5, _ => { },
            new MagnitudeArgs(1, 10, Slider: false, Integer: true));

        var args = Assert.IsType<MagnitudeArgs>(d.Args);
        Assert.False(args.Slider);
        Assert.True(args.Integer);
    }

    // ── Editor: module-owned element, boxed selectors for the reset ────────────

    [Fact]
    public void EditorCarriesItsBuilderAndRoundTripsItsValue()
    {
        (int A, int B) value = (0, 0);
        // Build is never invoked here: constructing a WinUI element needs a XAML
        // runtime. The factory must only carry it.
        var d = Setting.Editor<(int A, int B)>("k", () => value, v => value = v,
            new EditorArgs(() => throw new InvalidOperationException("not built in tests")),
            defaultValue: () => (1, 2));

        Assert.Equal(SettingKind.Editor, d.Kind);
        var args = Assert.IsType<EditorArgs>(d.Args);
        Assert.NotNull(args.Build);

        d.SetValue((3, 4));
        Assert.Equal((3, 4), value);
        Assert.Equal((3, 4), d.GetValue());
        Assert.Equal((1, 2), d.Default!());
    }

    // ── Hotkey: a nullable chord through the boxed selectors ───────────────────

    [Fact]
    public void HotkeyRoundTripsAChordAndNullThroughTheBoxedSelectors()
    {
        HotkeyChord? value = null;
        var d = Setting.Hotkey("k", () => value, v => value = v, new HotkeyArgs(AllowUnbound: true));

        Assert.Equal(SettingKind.Hotkey, d.Kind);

        var chord = new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x1E);
        d.SetValue(chord);
        Assert.Equal(chord, value);
        Assert.Equal(chord, d.GetValue());

        d.SetValue(null);
        Assert.Null(value);
        Assert.Null(d.GetValue());
    }

    [Fact]
    public void HotkeyDefaultEqualsTheBoxedValueItResetsTo()
    {
        var def = new HotkeyChord(HotkeyModifiers.Win, 0x29);
        var d = Setting.Hotkey("k", () => def, _ => { }, new HotkeyArgs(AllowUnbound: false),
            defaultValue: () => new HotkeyChord(HotkeyModifiers.Win, 0x29));

        // The composer's dirty check compares the boxed value with the boxed
        // default by Equals: an equal chord must read as clean.
        Assert.NotNull(d.Default);
        Assert.Equal(d.GetValue(), d.Default!());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HotkeyCarriesWhetherTheChordMayBeCleared(bool allowUnbound)
    {
        var d = Setting.Hotkey("k", () => null, _ => { }, new HotkeyArgs(allowUnbound));

        Assert.Equal(allowUnbound, Assert.IsType<HotkeyArgs>(d.Args).AllowUnbound);
    }

    // ── Group: master and children, the master round-trips ─────────────────────

    [Fact]
    public void GroupRoundTripsItsMasterAndHoldsItsChildren()
    {
        bool master = false;
        var child = Setting.Magnitude("child", () => 0, _ => { }, new MagnitudeArgs(0, 1));
        var g = Setting.Group("g", () => master, v => master = v, new[] { child },
            defaultValue: () => true);

        Assert.Equal(SettingKind.Group, g.Kind);
        g.SetValue(true);
        Assert.True(master);
        Assert.Equal(true, g.GetValue());
        Assert.Equal(true, g.Default!());

        var args = Assert.IsType<GroupArgs>(g.Args);
        Assert.Single(args.Children);
    }
}
