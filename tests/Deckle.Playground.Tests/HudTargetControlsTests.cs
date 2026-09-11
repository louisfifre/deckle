using Deckle.Playground;
using Xunit;

namespace Deckle.Playground.Tests;

[Trait("Category", "regression")]
public sealed class HudTargetControlsTests
{
    [Theory]
    [InlineData(HudTarget.ArcMask)]
    [InlineData(HudTarget.UtteranceCountdown)]
    public void UncoloredMaskAndCountdownDoNotOfferAColorVariant(HudTarget target)
    {
        var model = new HudViewModel { CurrentTarget = target };
        Assert.False(model.IsVariantToggleVisible);
    }

    [Fact]
    public void CountdownDoesNotOfferItsOwnPalette()
    {
        var model = new HudViewModel { CurrentTarget = HudTarget.UtteranceCountdown };
        Assert.Contains(HudTuningSection.UtteranceCountdown, model.ActiveTuningSections);
        Assert.DoesNotContain(HudTuningSection.Palette, model.ActiveTuningSections);
    }

    [Fact]
    public void BareMaterialOffersItsOwnRotationAndMaskOffersItsOwnShape()
    {
        var model = new HudViewModel { CurrentTarget = HudTarget.Conic };
        Assert.Contains(HudTuningSection.HueRotation, model.ActiveTuningSections);
        Assert.DoesNotContain(HudTuningSection.ConicFade, model.ActiveTuningSections);
        model.CurrentTarget = HudTarget.ArcMask;
        Assert.Contains(HudTuningSection.ConicFade, model.ActiveTuningSections);
        Assert.Contains(HudTuningSection.ArcRotation, model.ActiveTuningSections);
        Assert.DoesNotContain(HudTuningSection.Palette, model.ActiveTuningSections);
    }
    [Fact]
    public void FreshTuningUsesTheProductionTranscribingMaterialDefaults()
    {
        var expected = new Deckle.Composition.HudComposition.ConicArcStrokeConfig();
        var actual = new TuningModel().ToConfig();
        Assert.Equal(expected.TranscribingExposureDark, actual.TranscribingExposureDark);
        Assert.Equal(expected.TranscribingExposureLight, actual.TranscribingExposureLight);
        Assert.Equal(expected.HuePeriodSeconds, actual.HuePeriodSeconds);
    }
}
