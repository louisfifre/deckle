using System;
using System.Collections.Generic;
using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// Tests de comportement sur les factories Setting.* — le contrat entre l'auteur
// d'un manifeste et le composer. On vérifie ce qu'une factory PRODUIT (le kind,
// les args, les sélecteurs boxés, le défaut, l'avis), pas comment le composer le
// rend : ces descripteurs sont des records purs, sans UI, donc exerçables tels
// quels. Un test survit à un refactor du rendu.
//
// Portée : les capacités ajoutées ce workstream (Text, la variante radio de
// Choice, la Section sans maître, le canal d'avis, le mode Editable du Path) plus
// le contrat de base des sélecteurs (aller-retour, défaut) sur lequel le reset et
// la synchro du composer s'appuient.
[Trait("Category", "unit")]
public class SettingFactoryTests
{
    // ── Toggle : aller-retour boxé + défaut ────────────────────────────────────

    [Fact]
    public void ToggleRoundTripsThroughBoxedSelectorsAndCarriesItsDefault()
    {
        bool value = false;
        var d = Setting.Toggle("k", () => value, v => value = v, defaultValue: () => true);

        Assert.Equal(SettingKind.Toggle, d.Kind);

        // Le composer écrit/lit via les sélecteurs object? boxés.
        d.SetValue(true);
        Assert.True(value);
        Assert.Equal(true, d.GetValue());

        // Le défaut est lu à la source à chaque appel (jamais mis en cache).
        Assert.NotNull(d.Default);
        Assert.Equal(true, d.Default!());
    }

    [Fact]
    public void AnOmittedDefaultLeavesNoResettableDefault()
    {
        // Pas de defaultValue → Default null : le composer ne rend alors aucun reset.
        var d = Setting.Toggle("k", () => false, _ => { });
        Assert.Null(d.Default);
    }

    // ── Text : args par défaut, args fournis, aller-retour ─────────────────────

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

    // ── Choice vs Radio : même kind, le drapeau Radio les distingue ────────────

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

    // ── Section : pli sans maître, valueless, porte ses enfants ────────────────

    [Fact]
    public void SectionIsValuelessAndHoldsItsChildren()
    {
        var child = Setting.Toggle("child", () => false, _ => { });
        var s = Setting.Section("sec", new[] { child }, glyph: "G");

        Assert.Equal(SettingKind.Section, s.Kind);
        Assert.Equal("G", s.Glyph);

        // Nœud sans valeur : lit null, l'écriture est un no-op qui ne jette pas,
        // et il n'a pas de défaut resettable (seuls ses enfants en ont).
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

    // ── Avis : un canal réactif, ré-évalué comme VisibleWhen ───────────────────

    [Fact]
    public void AdvisoryIsLiveAndReEvaluatedEachCall()
    {
        string? message = null;
        var d = Setting.Text("k", () => "", _ => { }, advisory: () => message);

        Assert.NotNull(d.Advisory);
        Assert.Null(d.Advisory!());   // rien à dire

        message = "beware";
        Assert.Equal("beware", d.Advisory!());   // l'état a changé, l'avis suit
    }

    // ── Path : le mode Editable et le défaut différé ───────────────────────────

    [Fact]
    public void PathCarriesTheEditableModeAndItsDeferredDefaultPath()
    {
        var d = Setting.Path("k", () => "", _ => { },
            new PathArgs(FolderPickerMode.Editable, DefaultPath: () => "D:/models"));

        Assert.Equal(SettingKind.Path, d.Kind);
        var args = Assert.IsType<PathArgs>(d.Args);
        Assert.Equal(FolderPickerMode.Editable, args.Mode);
        Assert.NotNull(args.DefaultPath);
        Assert.Equal("D:/models", args.DefaultPath!());   // résolu à l'appel, pas capturé
    }

    // ── Slider / Number : les bornes du contrôle vivent dans les args ──────────

    [Fact]
    public void SliderCarriesItsRangeStepAndUnit()
    {
        var d = Setting.Slider("k", () => 0, _ => { }, new SliderArgs(0.25, 3.0, 0.05, Unit: "×"));

        Assert.Equal(SettingKind.Slider, d.Kind);
        var args = Assert.IsType<SliderArgs>(d.Args);
        Assert.Equal(0.25, args.Minimum);
        Assert.Equal(3.0, args.Maximum);
        Assert.Equal(0.05, args.StepFrequency);
        Assert.Equal("×", args.Unit);
    }

    [Fact]
    public void NumberCarriesItsRangeAndNudges()
    {
        var d = Setting.Number("k", () => 0, _ => { }, new NumberArgs(-1, 448, 1, 10));

        Assert.Equal(SettingKind.Number, d.Kind);
        var args = Assert.IsType<NumberArgs>(d.Args);
        Assert.Equal(-1, args.Minimum);
        Assert.Equal(448, args.Maximum);
        Assert.Equal(1, args.SmallChange);
        Assert.Equal(10, args.LargeChange);
    }

    // ── Group : maître + enfants, le maître fait l'aller-retour ────────────────

    [Fact]
    public void GroupRoundTripsItsMasterAndHoldsItsChildren()
    {
        bool master = false;
        var child = Setting.Slider("child", () => 0, _ => { }, new SliderArgs(0, 1, 0.1));
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
