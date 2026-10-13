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
        var game = AmbientPresets.All.Single(p => p.Id == AmbientPresets.Game);

        family.Select(AmbientPresets.Game);
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
