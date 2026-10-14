using Deckle.Playground;
using Xunit;

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

    [Fact]
    public void TheGaugeSharesTheRecordingRowsAndOffersNoOtherPalette()
    {
        var model = new HudViewModel { CurrentTarget = HudTarget.Gauge };
        Assert.Contains(HudTuningSection.UtteranceCountdown, model.ActiveTuningSections);
        // The gauge is the Recording outline opened by the pause: its fades
        // and material are the Recording rows, tuned beside it.
        Assert.Contains(HudTuningSection.Recording, model.ActiveTuningSections);
        Assert.DoesNotContain(HudTuningSection.Palette, model.ActiveTuningSections);
    }

    [Fact]
    public void BareMaterialOffersItsOwnRotationAndMaskOffersItsOwnShape()
    {
        var model = new HudViewModel { CurrentTarget = HudTarget.Cone };
        Assert.Contains(HudTuningSection.HueRotation, model.ActiveTuningSections);
        Assert.DoesNotContain(HudTuningSection.ConicFade, model.ActiveTuningSections);
        model.CurrentTarget = HudTarget.SweepMask;
        Assert.Contains(HudTuningSection.ConicFade, model.ActiveTuningSections);
        Assert.Contains(HudTuningSection.ArcRotation, model.ActiveTuningSections);
        Assert.DoesNotContain(HudTuningSection.Palette, model.ActiveTuningSections);
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
