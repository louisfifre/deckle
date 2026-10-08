using System;
using System.Collections.Generic;
using Deckle.Presets;
using Xunit;

namespace Deckle.Presets.Tests;

// Behaviour tests for a preset family: what it reports and writes against a
// store it does not own. The store is a local variable read and written
// through lambdas, the way a module hands over its own settings accessors.
[Trait("Category", "unit")]
public class PresetFamilyTests
{
    private readonly record struct Look(int Radius, double Opacity);

    private static readonly Preset<Look> Soft = new("soft", "PresetSoft", new Look(8, 0.4));
    private static readonly Preset<Look> Sharp = new("sharp", "PresetSharp", new Look(2, 0.9));
    private static readonly IReadOnlyList<Preset<Look>> Declared = [Soft, Sharp];

    private sealed class Store
    {
        public Look Current;
        public int Writes;
    }

    private static PresetFamily<Look> FamilyOver(Store store) =>
        new(Declared, () => store.Current, value => { store.Current = value; store.Writes++; });

    [Fact]
    public void Match_returns_the_preset_whose_values_equal_the_current_values()
    {
        var store = new Store { Current = Sharp.Values };
        var family = FamilyOver(store);

        Assert.Equal(Sharp, family.Match());
        Assert.Equal(Sharp.Id, family.Selection);
    }

    [Fact]
    public void Current_values_matching_no_preset_select_custom()
    {
        var store = new Store { Current = new Look(5, 0.5) };
        var family = FamilyOver(store);

        Assert.Null(family.Match());
        Assert.Equal(PresetFamily.Custom, family.Selection);
    }

    [Fact]
    public void Apply_writes_the_preset_values_and_the_family_then_matches_it()
    {
        var store = new Store { Current = new Look(5, 0.5) };
        var family = FamilyOver(store);

        Assert.True(family.Apply(Soft.Id));

        Assert.Equal(Soft.Values, store.Current);
        Assert.Equal(Soft, family.Match());
    }

    [Theory]
    [InlineData(PresetFamily.Custom)]
    [InlineData("unknown")]
    public void Apply_of_custom_or_an_unknown_id_writes_nothing(string id)
    {
        var before = new Look(5, 0.5);
        var store = new Store { Current = before };
        var family = FamilyOver(store);

        Assert.False(family.Apply(id));

        Assert.Equal(0, store.Writes);
        Assert.Equal(before, store.Current);
    }

    [Fact]
    public void Selector_options_list_the_presets_in_order_then_custom()
    {
        var family = FamilyOver(new Store());

        var options = family.SelectorOptions("PresetCustom");

        Assert.Equal(
            [(Soft.Id, Soft.LabelKey), (Sharp.Id, Sharp.LabelKey), (PresetFamily.Custom, "PresetCustom")],
            options);
    }

    [Fact]
    public void Constructor_rejects_duplicate_ids()
    {
        var twin = Soft with { LabelKey = "PresetTwin" };

        Assert.Throws<ArgumentException>(() => new PresetFamily<Look>([Soft, twin], () => default, _ => { }));
    }

    [Fact]
    public void Constructor_rejects_the_reserved_custom_id()
    {
        var custom = new Preset<Look>(PresetFamily.Custom, "PresetCustom", new Look(1, 1.0));

        Assert.Throws<ArgumentException>(() => new PresetFamily<Look>([Soft, custom], () => default, _ => { }));
    }

    [Fact]
    public void Constructor_rejects_an_empty_list()
    {
        Assert.Throws<ArgumentException>(() => new PresetFamily<Look>([], () => default, _ => { }));
    }
}
