using Deckle.Composition;
using Deckle.Playground;
using Xunit;
using static Deckle.Composition.HudComposition;

namespace Deckle.Playground.Tests;

[Trait("Category", "regression")]
public sealed class HudTargetControlsTests
{
    // Only the parts that paint the cone without a HUD state offer the
    // material choice: the states show their own material, the sweep mask
    // has none and the gauge shows Recording's.
    [Theory]
    [InlineData(HudTarget.Charging, false)]
    [InlineData(HudTarget.Recording, false)]
    [InlineData(HudTarget.Transcribing, false)]
    [InlineData(HudTarget.Rewriting, false)]
    [InlineData(HudTarget.Cone, true)]
    [InlineData(HudTarget.SweepMask, false)]
    [InlineData(HudTarget.MaskedCone, true)]
    [InlineData(HudTarget.Gauge, false)]
    [InlineData(HudTarget.DigitReveal, true)]
    public void OnlyTheConePartsOfferAMaterialChoice(HudTarget target, bool offered)
    {
        var model = new HudViewModel { CurrentTarget = target };
        Assert.Equal(offered, model.IsMaterialToggleVisible);
    }

    // The gauge is the Recording outline opened by the pause: its fades and
    // material are the Recording rows, tuned beside it, and the material
    // section shown is Recording's, whatever the parts' radio says.
    [Theory]
    [InlineData(HudMaterial.Grey, HudMaterial.Colour, HudTuningSection.GreyMaterial)]
    [InlineData(HudMaterial.Colour, HudMaterial.Grey, HudTuningSection.ColourMaterial)]
    public void TheGaugeSharesTheRecordingRowsAndShowsRecordingsMaterial(
        HudMaterial recording, HudMaterial radio, HudTuningSection expected)
    {
        var model = new HudViewModel { CurrentTarget = HudTarget.Gauge, PreviewMaterial = radio };
        var sections = model.ActiveTuningSections(new ConicArcStrokeConfig { RecordingMaterial = recording });

        Assert.Contains(HudTuningSection.Gauge, sections);
        Assert.Contains(HudTuningSection.Recording, sections);
        Assert.Contains(expected, sections);
        Assert.Single(sections, s => s is HudTuningSection.GreyMaterial or HudTuningSection.ColourMaterial);
    }

    [Fact]
    public void BareMaterialOffersItsOwnRotationAndMaskOffersItsOwnShape()
    {
        var tuning = new ConicArcStrokeConfig();
        var model = new HudViewModel { CurrentTarget = HudTarget.Cone };
        Assert.Contains(HudTuningSection.HueRotation, model.ActiveTuningSections(tuning));
        Assert.DoesNotContain(HudTuningSection.SweepMask, model.ActiveTuningSections(tuning));
        model.CurrentTarget = HudTarget.SweepMask;
        Assert.Contains(HudTuningSection.SweepMask, model.ActiveTuningSections(tuning));
        Assert.Contains(HudTuningSection.SweepRotation, model.ActiveTuningSections(tuning));
        Assert.DoesNotContain(HudTuningSection.ColourMaterial, model.ActiveTuningSections(tuning));
        Assert.DoesNotContain(HudTuningSection.GreyMaterial, model.ActiveTuningSections(tuning));
    }
    [Fact]
    public void FreshTuningUsesTheProductionTranscribingMaterialDefaults()
    {
        var expected = new Deckle.Composition.HudComposition.ConicArcStrokeConfig();
        var actual = new TuningModel().ToConfig();
        Assert.Equal(expected.GreyLightnessDark, actual.GreyLightnessDark);
        Assert.Equal(expected.GreyLightnessLight, actual.GreyLightnessLight);
        Assert.Equal(expected.HuePeriodSeconds, actual.HuePeriodSeconds);
        Assert.Equal(expected.StrokeThickness, actual.StrokeThickness);
        Assert.Equal(expected.InsetDip, actual.InsetDip);
    }
}
