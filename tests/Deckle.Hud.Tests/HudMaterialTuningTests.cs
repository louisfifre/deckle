using Deckle.Composition;
using Xunit;

namespace Deckle.Hud.Tests;

[Trait("Category", "regression")]
[Collection(HudLiveTuningCollection.Name)]
public sealed class HudMaterialTuningTests
{
    [Fact]
    public void SharedMaterialKeepsPaletteRotationAndGradingWithoutReplacingTheMask()
    {
        var original = HudMaterialTuning.Current;
        try
        {
            var source = original with
            {
                OklchChroma = 0.2f, HuePeriodSeconds = 19,
                TranscribingExposureDark = 0.4f, ConicSpanTurns = 0.1f,
            };
            HudMaterialTuning.Apply(source);
            var target = new HudComposition.ConicArcStrokeConfig { ConicSpanTurns = 0.35f, ArcPeriodSeconds = 6 };
            var actual = HudMaterialTuning.ApplyTo(target);
            Assert.Equal(source.OklchChroma, actual.OklchChroma);
            Assert.Equal(source.HuePeriodSeconds, actual.HuePeriodSeconds);
            Assert.Equal(source.TranscribingExposureDark, actual.TranscribingExposureDark);
            Assert.Equal(target.ConicSpanTurns, actual.ConicSpanTurns);
            Assert.Equal(target.ArcPeriodSeconds, actual.ArcPeriodSeconds);
        }
        finally { HudMaterialTuning.Apply(original); }
    }

    [Fact]
    public void EditingASweepMaskDoesNotChangeTheSharedMaterial()
    {
        var original = HudMaterialTuning.Current;
        int changes = 0;
        void OnChanged() => changes++;
        HudMaterialTuning.Changed += OnChanged;
        try
        {
            HudMaterialTuning.Apply(original with { ConicSpanTurns = 0.123f, ArcPeriodSeconds = 3 });
            Assert.Equal(original, HudMaterialTuning.Current);
            Assert.Equal(0, changes);
        }
        finally
        {
            HudMaterialTuning.Changed -= OnChanged;
            HudMaterialTuning.Apply(original);
        }
    }
}
