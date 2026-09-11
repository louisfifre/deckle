using Deckle.Composition;
using Xunit;

namespace Deckle.Hud.Tests;

[Trait("Category", "regression")]
public sealed class HudGeometryTuningTests
{
    [Fact]
    public void SharedThicknessPreservesMaterialMasksAndCountdownGeometry()
    {
        var original = HudGeometryTuning.Current;
        var countdown = UtteranceCountdownTuning.Current;
        try
        {
            HudGeometryTuning.Apply(original with { StrokeThickness = 2.5f, InsetDip = 0f });
            var source = new HudComposition.ConicArcStrokeConfig
            {
                StrokeThickness = 7f, OklchChroma = 0.12f,
                ConicSpanTurns = 0.25f, ArcPeriodSeconds = 9,
            };
            var actual = HudGeometryTuning.ApplyTo(source);
            Assert.Equal(2.5f, actual.StrokeThickness);
            Assert.Equal(0f, actual.InsetDip);
            Assert.Equal(source.OklchChroma, actual.OklchChroma);
            Assert.Equal(source.ConicSpanTurns, actual.ConicSpanTurns);
            Assert.Equal(source.ArcPeriodSeconds, actual.ArcPeriodSeconds);
            Assert.Same(countdown, UtteranceCountdownTuning.Current);
        }
        finally { HudGeometryTuning.Apply(original); }
    }
}
