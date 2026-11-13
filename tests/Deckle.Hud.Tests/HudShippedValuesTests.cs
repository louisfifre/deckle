using Deckle.Composition;
using Xunit;
using static Deckle.Composition.HudComposition;

namespace Deckle.Hud.Tests;

// The HUD reads no tuning file any more: its look is the compiled defaults.
// These are the values Louis tuned in the Playground and kept (2026-11-13),
// listed in docs/hud-tuning-reference.md. A change here is a change of the
// HUD's look, so it must be deliberate and land in that document too.
[Trait("Category", "regression")]
public sealed class HudShippedValuesTests
{
    [Fact]
    public void TheOutlineKeepsTheTunedGeometryAndColour()
    {
        var stroke = new ConicArcStrokeConfig();

        Assert.Equal(5f, stroke.StrokeThickness);
        Assert.Equal(-3f, stroke.InsetDip);
        Assert.Equal(0.65f, stroke.OklchLightness);
        Assert.Equal(3.5f, stroke.RecordingConicFadeCurve);
    }

    [Fact]
    public void TheGaugeKeepsTheTunedReachCourseAndPace()
    {
        var countdown = new UtteranceCountdownConfig();

        Assert.Equal(-0.25f, countdown.StartReach);
        Assert.Equal(1f, countdown.EndReach);
        Assert.Equal(0.66f, countdown.CourseEaseP1X);
        Assert.Equal(0f, countdown.CourseEaseP1Y);
        Assert.Equal(1f, countdown.CourseEaseP2X);
        Assert.Equal(0.33f, countdown.CourseEaseP2Y);
        Assert.Equal(0.1, countdown.FollowSeconds);
        Assert.Equal(0.33, countdown.RetreatSeconds);
    }
}
