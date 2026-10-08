using Deckle.Presets;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// The cut presets fix the eight hangover values of the energy segmenter. These
// tests cover the pure parts only: the preset table, the values record and its
// round trip through the segmenter POCO. WhisperViewModel is never built here
// (it touches the settings service on disk and WinUI). Every expectation reads
// CutPresets.All or the POCO initializers, never a copy of the literals.
[Trait("Category", "unit")]
public class CutPresetsTests
{
    private static PresetFamily<CutCurveValues> FamilyOver(EnergySegmenterSettings settings) =>
        new(CutPresets.All, () => CutCurveValues.From(settings), v => v.ApplyTo(settings));

    [Fact]
    public void ShippedDefaultsAreTheBalancedPreset()
    {
        var current = CutCurveValues.From(new EnergySegmenterSettings());
        var family = new PresetFamily<CutCurveValues>(CutPresets.All, () => current, v => current = v);

        Assert.Equal(CutPresets.Balanced, family.Selection);
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
    public void ACustomizedStoredCurveReadsAsCustom()
    {
        // A curve tuned by hand matches no preset: it reads as Custom and is
        // left untouched (no migration rewrites it).
        var settings = new EnergySegmenterSettings
        {
            HangoverCurveX1 = 0.42,
            HangoverCurveY1 = 0.81,
            HangoverCurveX2 = 0.80,
            HangoverCurveY2 = 0.23,
        };

        Assert.Equal(PresetFamily.Custom, FamilyOver(settings).Selection);
    }

    [Fact]
    public void PresetsAreDistinct()
    {
        var distinct = CutPresets.All.Select(p => p.Values).Distinct().Count();

        Assert.Equal(CutPresets.All.Count, distinct);
    }
}
