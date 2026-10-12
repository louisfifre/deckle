using System;
using System.Collections.Generic;
using Deckle.Presets;
using Xunit;

namespace Deckle.Presets.Tests;

// Behaviour tests for a preset family against a store it does not own. The
// store is a plain object implementing IPresetStore, the way a module hands
// over its settings POCO. The contract under test is Louis's rule: a hand
// edit switches to Custom and keeps the values, a preset writes its values,
// Custom brings the kept values back.
[Trait("Category", "unit")]
public class PresetFamilyTests
{
    private sealed record Look(int Radius, double Opacity);

    private static readonly Preset<Look> Soft = new("soft", "PresetSoft", new Look(8, 0.4));
    private static readonly Preset<Look> Sharp = new("sharp", "PresetSharp", new Look(2, 0.9));
    private static readonly IReadOnlyList<Preset<Look>> Declared = [Soft, Sharp];

    private sealed class Store : IPresetStore<Look>
    {
        public Look Values { get; set; } = new(0, 0);
        public string? Selection { get; set; }
        public Look? CustomValues { get; set; }
    }

    // A store whose value setter reports every write, the way a view model's
    // property setters do.
    private sealed class ReportingStore : IPresetStore<Look>
    {
        private Look _values = new(0, 0);

        public Action? OnWrite { get; set; }

        public Look Values
        {
            get => _values;
            set { _values = value; OnWrite?.Invoke(); }
        }

        public string? Selection { get; set; }
        public Look? CustomValues { get; set; }
    }

    private static PresetFamily<Look> FamilyOver(IPresetStore<Look> store) => new(Declared, Soft.Id, store);

    [Fact]
    public void Selecting_a_preset_writes_its_values_and_stores_its_id()
    {
        var store = new Store { Values = new Look(5, 0.5), Selection = PresetFamily.Custom };
        var family = FamilyOver(store);

        Assert.True(family.Select(Sharp.Id));

        Assert.Equal(Sharp.Values, store.Values);
        Assert.Equal(Sharp.Id, store.Selection);
        Assert.Equal(Sharp.Id, family.Selection);
    }

    [Fact]
    public void A_hand_edit_switches_to_custom_and_keeps_the_edited_values()
    {
        var store = new Store { Values = Soft.Values, Selection = Soft.Id };
        var family = FamilyOver(store);

        store.Values = new Look(5, 0.5);
        family.NotifyEdited();

        Assert.Equal(PresetFamily.Custom, family.Selection);
        Assert.Equal(new Look(5, 0.5), store.CustomValues);
    }

    [Fact]
    public void Custom_then_a_preset_then_custom_brings_the_hand_made_values_back()
    {
        var store = new Store { Values = Soft.Values, Selection = Soft.Id };
        var family = FamilyOver(store);
        var handMade = new Look(5, 0.5);

        store.Values = handMade;
        family.NotifyEdited();
        family.Select(Sharp.Id);
        Assert.Equal(Sharp.Values, store.Values);

        family.Select(PresetFamily.Custom);

        Assert.Equal(handMade, store.Values);
        Assert.Equal(PresetFamily.Custom, family.Selection);
    }

    [Fact]
    public void Selecting_custom_with_no_kept_values_keeps_the_live_values_and_remembers_them()
    {
        var store = new Store { Values = Soft.Values, Selection = Soft.Id };
        var family = FamilyOver(store);

        family.Select(PresetFamily.Custom);

        Assert.Equal(Soft.Values, store.Values);
        Assert.Equal(Soft.Values, store.CustomValues);
        Assert.Equal(PresetFamily.Custom, family.Selection);
    }

    [Fact]
    public void Edits_reported_while_a_preset_is_applied_are_ignored()
    {
        var store = new ReportingStore { Values = new Look(5, 0.5), Selection = PresetFamily.Custom };
        var family = FamilyOver(store);
        store.OnWrite = family.NotifyEdited;

        family.Select(Sharp.Id);

        Assert.Equal(Sharp.Id, family.Selection);
        Assert.False(family.IsApplying);
    }

    [Fact]
    public void An_unknown_id_writes_nothing()
    {
        var before = new Look(5, 0.5);
        var store = new Store { Values = before, Selection = PresetFamily.Custom };
        var family = FamilyOver(store);

        Assert.False(family.Select("unknown"));

        Assert.Equal(before, store.Values);
        Assert.Equal(PresetFamily.Custom, store.Selection);
    }

    [Fact]
    public void A_store_without_selection_derives_the_matching_preset_once()
    {
        var store = new Store { Values = Sharp.Values };

        var family = FamilyOver(store);

        Assert.Equal(Sharp.Id, family.Selection);
        Assert.Equal(Sharp.Id, store.Selection);
        Assert.Null(store.CustomValues);
    }

    [Fact]
    public void A_store_without_selection_and_matching_no_preset_derives_custom_and_keeps_its_values()
    {
        var handMade = new Look(5, 0.5);
        var store = new Store { Values = handMade };

        var family = FamilyOver(store);

        Assert.Equal(PresetFamily.Custom, family.Selection);
        Assert.Equal(handMade, store.CustomValues);
        Assert.Equal(handMade, store.Values);
    }

    [Fact]
    public void A_stored_selection_is_kept_even_when_the_values_differ_from_the_preset()
    {
        var store = new Store { Values = new Look(5, 0.5), Selection = Soft.Id };

        var family = FamilyOver(store);

        Assert.Equal(Soft.Id, family.Selection);
        Assert.Equal(new Look(5, 0.5), store.Values);
    }

    [Fact]
    public void A_stored_selection_naming_no_preset_is_derived_again()
    {
        var store = new Store { Values = Sharp.Values, Selection = "retired" };

        var family = FamilyOver(store);

        Assert.Equal(Sharp.Id, family.Selection);
    }

    [Fact]
    public void Selector_options_list_the_presets_in_order_then_custom()
    {
        var family = FamilyOver(new Store { Selection = PresetFamily.Custom });

        var options = family.SelectorOptions("PresetCustom");

        Assert.Equal(
            [(Soft.Id, Soft.LabelKey), (Sharp.Id, Sharp.LabelKey), (PresetFamily.Custom, "PresetCustom")],
            options);
    }

    [Fact]
    public void Default_names_the_declared_default_preset()
    {
        var family = FamilyOver(new Store { Selection = PresetFamily.Custom });

        Assert.Equal(Soft, family.Default);
    }

    [Fact]
    public void Constructor_rejects_duplicate_ids()
    {
        var twin = Soft with { LabelKey = "PresetTwin" };

        Assert.Throws<ArgumentException>(() => new PresetFamily<Look>([Soft, twin], Soft.Id, new Store()));
    }

    [Fact]
    public void Constructor_rejects_the_reserved_custom_id()
    {
        var custom = new Preset<Look>(PresetFamily.Custom, "PresetCustom", new Look(1, 1.0));

        Assert.Throws<ArgumentException>(() => new PresetFamily<Look>([Soft, custom], Soft.Id, new Store()));
    }

    [Fact]
    public void Constructor_rejects_an_empty_list()
    {
        Assert.Throws<ArgumentException>(() => new PresetFamily<Look>([], Soft.Id, new Store()));
    }

    [Fact]
    public void Constructor_rejects_a_default_that_names_no_preset()
    {
        Assert.Throws<ArgumentException>(() => new PresetFamily<Look>(Declared, "none", new Store()));
    }
}
