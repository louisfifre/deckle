using System.Text.Json;
using Deckle.Presets;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// The cut presets fix the eight hangover values of the energy segmenter. These
// tests cover the pure parts only: the preset table, the values record, its
// round trip through the segmenter POCO, the one-time derivation of a missing
// selection, and the JSON persistence of the stored selection and Custom
// values. WhisperViewModel is never built here (it touches the settings service
// on disk and WinUI), so the family runs over a small store around the POCO,
// the way the view model's store reaches it. Every expectation reads
// CutPresets.All or the POCO initializers, never a copy of the literals.
[Trait("Category", "unit")]
public class CutPresetsTests
{
    // Values through From / ApplyTo, Selection and CustomValues on the POCO's
    // two persisted properties: the same mapping as CutPresetStore, minus the
    // view model.
    private sealed class SettingsStore : IPresetStore<CutCurveValues>
    {
        private readonly EnergySegmenterSettings _settings;

        public SettingsStore(EnergySegmenterSettings settings) => _settings = settings;

        public CutCurveValues Values
        {
            get => CutCurveValues.From(_settings);
            set => value.ApplyTo(_settings);
        }

        public string? Selection
        {
            get => _settings.CutPreset;
            set => _settings.CutPreset = value;
        }

        public CutCurveValues? CustomValues
        {
            get => _settings.CutCustomValues;
            set => _settings.CutCustomValues = value;
        }
    }

    private static PresetFamily<CutCurveValues> FamilyOver(EnergySegmenterSettings settings) =>
        new(CutPresets.All, CutPresets.Default, new SettingsStore(settings));

    private static EnergySegmenterSettings HandTunedCurve() => new()
    {
        HangoverCurveX1 = 0.42,
        HangoverCurveY1 = 0.81,
        HangoverCurveX2 = 0.80,
        HangoverCurveY2 = 0.23,
    };

    [Fact]
    public void ShippedDefaultsAreTheBalancedPreset()
    {
        var settings = new EnergySegmenterSettings();

        var family = FamilyOver(settings);

        Assert.Equal(CutPresets.Balanced, family.Selection);
        Assert.Equal(CutPresets.Balanced, settings.CutPreset);
    }

    [Fact]
    public void TheDefaultPresetIsTheOneThatEqualsTheShippedDefaults()
    {
        var defaults = CutCurveValues.From(new EnergySegmenterSettings());

        var family = FamilyOver(new EnergySegmenterSettings());

        Assert.Equal(defaults, family.Default.Values);
    }

    [Fact]
    public void EveryPresetRoundTripsThroughTheSegmenterSettings()
    {
        foreach (var preset in CutPresets.All)
        {
            var settings = new EnergySegmenterSettings();
            preset.Values.ApplyTo(settings);

            Assert.Equal(preset.Values, CutCurveValues.From(settings));
            Assert.Equal(preset.Id, FamilyOver(settings).Selection);
        }
    }

    [Fact]
    public void AHandTunedStoredCurveWithNoStoredSelectionDerivesCustomAndKeepsItsValues()
    {
        var settings = HandTunedCurve();
        var tuned = CutCurveValues.From(settings);

        var family = FamilyOver(settings);

        Assert.Equal(PresetFamily.Custom, family.Selection);
        Assert.Equal(PresetFamily.Custom, settings.CutPreset);
        Assert.Equal(tuned, settings.CutCustomValues);
        Assert.Equal(tuned, CutCurveValues.From(settings));
    }

    [Fact]
    public void AStoredSelectionIsKeptEvenWhenTheCurveWasHandTuned()
    {
        var settings = HandTunedCurve();
        settings.CutPreset = CutPresets.Balanced;

        var family = FamilyOver(settings);

        Assert.Equal(CutPresets.Balanced, family.Selection);
        Assert.Null(settings.CutCustomValues);
    }

    [Fact]
    public void SelectingAPresetWritesItsValuesIntoTheSegmenterSettings()
    {
        var settings = new EnergySegmenterSettings();
        var family = FamilyOver(settings);

        foreach (var preset in CutPresets.All)
        {
            Assert.True(family.Select(preset.Id));

            Assert.Equal(preset.Values, CutCurveValues.From(settings));
            Assert.Equal(preset.Id, settings.CutPreset);
        }
    }

    [Fact]
    public void ACustomEditSurvivesAPresetAndComesBackWithCustom()
    {
        var settings = new EnergySegmenterSettings();
        var family = FamilyOver(settings);
        var other = CutPresets.All.First(p => p.Id != family.Selection);

        settings.HangoverMaxMs += 1_000;
        var handMade = CutCurveValues.From(settings);
        family.NotifyEdited();
        family.Select(other.Id);
        Assert.Equal(other.Values, CutCurveValues.From(settings));

        family.Select(PresetFamily.Custom);

        Assert.Equal(handMade, CutCurveValues.From(settings));
        Assert.Equal(PresetFamily.Custom, settings.CutPreset);
    }

    [Fact]
    public void PresetsAreDistinct()
    {
        var distinct = CutPresets.All.Select(p => p.Values).Distinct().Count();

        Assert.Equal(CutPresets.All.Count, distinct);
    }

    [Fact]
    public void TheStoredSelectionAndCustomValuesRoundTripThroughJson()
    {
        // The settings file is written with camelCase names; the record is
        // rebuilt through its constructor, so a rename of a parameter or a
        // property that breaks the matching would show here.
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var settings = HandTunedCurve();
        settings.CutPreset = PresetFamily.Custom;
        settings.CutCustomValues = CutCurveValues.From(settings);

        var json = JsonSerializer.Serialize(settings, options);
        var restored = JsonSerializer.Deserialize<EnergySegmenterSettings>(json, options);

        Assert.NotNull(restored);
        Assert.Equal(settings.CutPreset, restored.CutPreset);
        Assert.Equal(settings.CutCustomValues, restored.CutCustomValues);
    }

    [Fact]
    public void AFileWrittenBeforeTheSelectionExistedLoadsWithNullSelectionAndNoCustomValues()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var legacy = $$"""{ "marginMs": {{new EnergySegmenterSettings().MarginMs}} }""";

        var restored = JsonSerializer.Deserialize<EnergySegmenterSettings>(legacy, options);

        Assert.NotNull(restored);
        Assert.Null(restored.CutPreset);
        Assert.Null(restored.CutCustomValues);
    }
}
