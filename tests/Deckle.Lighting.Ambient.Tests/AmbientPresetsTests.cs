using System.Text.Json;
using Deckle.Presets;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The Ambient presets fix the eight tuning values of AmbientTuning and leave
// the floor alone. These tests cover the pure parts: the preset table, the
// values record and its round trip through the settings POCO, the family
// over a store adapter, and the JSON shape the settings file keeps. The
// service singleton is never built here (it touches the disk). Every
// expectation reads AmbientPresets.All or the POCO initializers, never a
// copy of the literals.
[Trait("Category", "unit")]
public sealed class AmbientPresetsTests
{
    private static PresetFamily<AmbientTuning> FamilyOver(AmbientSettings settings) =>
        new(AmbientPresets.All, AmbientPresets.Default, new AmbientPresetStore(() => settings));

    [Fact]
    public void ShippedDefaultsAreTheDefaultPreset()
    {
        // A fresh file has no selection: the family derives it from the
        // initializers, which must equal the default preset.
        var settings = new AmbientSettings();

        var family = FamilyOver(settings);

        Assert.Equal(AmbientPresets.Default, family.Selection);
        Assert.Equal(AmbientPresets.Default, settings.Preset);
        Assert.Null(settings.CustomTuning);
    }

    [Fact]
    public void EveryPresetRoundTripsThroughTheSettings()
    {
        foreach (var preset in AmbientPresets.All)
        {
            var settings = new AmbientSettings();
            preset.Values.ApplyTo(settings);

            Assert.Equal(preset.Values, AmbientTuning.From(settings));
        }
    }

    [Fact]
    public void PresetsAreDistinctInIdAndValues()
    {
        Assert.Equal(AmbientPresets.All.Count, AmbientPresets.All.Select(p => p.Id).Distinct().Count());
        Assert.Equal(AmbientPresets.All.Count, AmbientPresets.All.Select(p => p.Values).Distinct().Count());
    }

    [Fact]
    public void NoPresetTouchesTheBrightnessFloor()
    {
        // The floor is a room choice, outside the presets: applying any
        // preset leaves it where the user put it.
        foreach (var preset in AmbientPresets.All)
        {
            var settings = new AmbientSettings { MinBrightnessEnabled = false, MinBrightness = 12 };
            FamilyOver(settings).Select(preset.Id);

            Assert.False(settings.MinBrightnessEnabled);
            Assert.Equal(12, settings.MinBrightness);
        }
    }

    [Fact]
    public void AnOlderFileWithHandTunedValuesDerivesCustomAndKeepsThem()
    {
        var settings = new AmbientSettings { SmoothingAlpha = 0.5, ChangeThreshold = 15 };
        var handMade = AmbientTuning.From(settings);

        var family = FamilyOver(settings);

        Assert.Equal(PresetFamily.Custom, family.Selection);
        Assert.Equal(handMade, settings.CustomTuning);
        Assert.Equal(handMade, AmbientTuning.From(settings));
    }

    [Fact]
    public void PickingAPresetThenCustomBringsTheHandMadeValuesBack()
    {
        var settings = new AmbientSettings();
        var family = FamilyOver(settings);
        settings.SmoothingAlpha = 0.5;
        family.NotifyEdited();
        var handMade = AmbientTuning.From(settings);
        var game = AmbientPresets.All.Single(p => p.Id == AmbientPresets.RealisticFast);

        family.Select(AmbientPresets.RealisticFast);
        Assert.Equal(game.Values, AmbientTuning.From(settings));

        family.Select(PresetFamily.Custom);

        Assert.Equal(handMade, AmbientTuning.From(settings));
        Assert.Equal(PresetFamily.Custom, settings.Preset);
    }

    [Fact]
    public void TheSelectionAndTheCustomTuningSurviveJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        var settings = new AmbientSettings { SmoothingAlpha = 0.5 };
        FamilyOver(settings).NotifyEdited();

        var json = JsonSerializer.Serialize(settings, options);
        var back = JsonSerializer.Deserialize<AmbientSettings>(json, options)!;

        Assert.Equal(PresetFamily.Custom, back.Preset);
        Assert.Equal(settings.CustomTuning, back.CustomTuning);
    }

    // ── The two axes: rendering × speed (values PROVISIONAL, 2026-11-07) ──

    [Fact]
    public void TheSelectorListsVividThenRealisticEachFastThenSlowThenCustom()
    {
        var order = FamilyOver(new AmbientSettings()).SelectorOptions("AmbientPresetCustom").Select(o => o.Value);

        Assert.Equal(
            ["vivid-fast", "vivid-slow", "realistic-fast", "realistic-slow", PresetFamily.Custom],
            order);
    }

    [Theory]
    [InlineData(AmbientPresets.VividFast,     0.40, 6, 1.10)]
    [InlineData(AmbientPresets.VividSlow,     0.15, 8, 1.10)]
    [InlineData(AmbientPresets.RealisticFast, 0.40, 6, 1.00)]
    [InlineData(AmbientPresets.RealisticSlow, 0.15, 8, 1.00)]
    public void EachPresetCombinesItsSpeedAndItsRendering(string id, double alpha, int threshold, double saturation)
    {
        var values = AmbientPresets.All.Single(p => p.Id == id).Values;

        Assert.Equal(alpha, values.SmoothingAlpha);
        Assert.Equal(threshold, values.ChangeThreshold);
        Assert.Equal(saturation, values.SaturationBoost);
        Assert.Equal(0.0, values.ExposureEv);
    }

    [Fact]
    public void TheDefaultIsRealisticSlow()
    {
        Assert.Equal("realistic-slow", AmbientPresets.Default);
    }

    [Theory]
    [InlineData("game",    AmbientPresets.RealisticFast)]
    [InlineData("movie",   AmbientPresets.RealisticSlow)]
    [InlineData("vivid",   AmbientPresets.VividFast)]
    [InlineData("ambient", AmbientPresets.RealisticSlow)]
    public void ARetiredPresetIdFollowsItsReplacementValuesIncluded(string retired, string replacement)
    {
        var settings = new AmbientSettings { Preset = retired, SmoothingAlpha = 0.99 };

        string? replaced = AmbientPresets.MigrateRetiredSelection(settings);

        Assert.Equal(retired, replaced);
        Assert.Equal(replacement, settings.Preset);
        Assert.Equal(AmbientPresets.All.Single(p => p.Id == replacement).Values, AmbientTuning.From(settings));
        Assert.Equal(replacement, FamilyOver(settings).Selection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("custom")]
    [InlineData("realistic-fast")]
    [InlineData("no-such-preset")]
    public void OtherSelectionsAreNotMigrated(string? stored)
    {
        var settings = new AmbientSettings { Preset = stored, SmoothingAlpha = 0.99 };

        Assert.Null(AmbientPresets.MigrateRetiredSelection(settings));
        Assert.Equal(stored, settings.Preset);
        Assert.Equal(0.99, settings.SmoothingAlpha);
    }

    [Fact]
    public void AnUnknownSelectionBecomesCustomWithTheValuesKept()
    {
        var settings = new AmbientSettings { Preset = "no-such-preset", SmoothingAlpha = 0.99 };
        var kept = AmbientTuning.From(settings);

        var family = FamilyOver(settings);

        Assert.Equal(PresetFamily.Custom, family.Selection);
        Assert.Equal(kept, settings.CustomTuning);
        Assert.Equal(0.99, settings.SmoothingAlpha);
    }

    [Fact]
    public void AFileWithTheRetiredIntegerModeStillLoads()
    {
        // Older files store "mode" as an integer. It is ignored; the values
        // decide the selection.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        var json = """{ "enabled": false, "mode": 3, "smoothingAlpha": 0.5 }""";

        var settings = JsonSerializer.Deserialize<AmbientSettings>(json, options)!;
        Assert.Null(settings.Preset);

        var family = FamilyOver(settings);

        Assert.Equal(PresetFamily.Custom, family.Selection);
        Assert.Equal(PresetFamily.Custom, settings.Preset);
        Assert.Equal(0.5, settings.SmoothingAlpha);
    }
}
