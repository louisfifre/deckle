using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// The curve editor's own arithmetic: the easing the plot draws and the frame
// that places the unit square in the plot's data space. Both are pure, so the
// contract is checked without a control.
[Trait("Category", "unit")]
public class CubicBezierTests
{
    [Fact]
    public void TheEndpointsAreFixed()
    {
        var curve = new CubicBezier(0.42, 0.0, 0.58, 1.0);

        Assert.Equal(0.0, curve.Solve(0.0), 6);
        Assert.Equal(1.0, curve.Solve(1.0), 6);
    }

    [Fact]
    public void ControlPointsOnTheDiagonalGiveTheIdentity()
    {
        var curve = new CubicBezier(0.25, 0.25, 0.75, 0.75);

        for (double x = 0; x <= 1.0; x += 0.1)
            Assert.Equal(x, curve.Solve(x), 4);
    }

    [Fact]
    public void AnEaseInStaysUnderTheDiagonalThenAnEaseOutOverIt()
    {
        Assert.True(new CubicBezier(0.42, 0.0, 1.0, 1.0).Solve(0.5) < 0.5);
        Assert.True(new CubicBezier(0.0, 0.0, 0.58, 1.0).Solve(0.5) > 0.5);
    }

    [Fact]
    public void AnOvershootingYIsDrawnNotClamped()
    {
        var curve = new CubicBezier(0.5, 1.25, 0.5, 1.25);

        Assert.True(curve.Solve(0.5) > 1.0);
    }

    [Fact]
    public void AFrameMapsTheUnitSquareOntoItsBox()
    {
        // The dictation's decay: from (15 s, 5 s delay) down to (120 s, 0.5 s).
        var frame = new CurveFrame(OriginX: 15, OriginY: 5, EndX: 120, EndY: 0.5, XMax: 140, YMax: 5, Axes: true, "s", "s");

        Assert.Equal(15, frame.DataX(0));
        Assert.Equal(120, frame.DataX(1));
        Assert.Equal(5, frame.DataY(0));
        Assert.Equal(0.5, frame.DataY(1));
        Assert.Equal(0.5, frame.UnitU(67.5), 9);
        Assert.Equal(0.5, frame.UnitV(2.75), 9);
    }

    [Fact]
    public void AFrameHoldsTheCurveAtTheOriginBeforeTheBoxAndAtTheEndAfterIt()
    {
        var frame = new CurveFrame(15, 5, 120, 0.5, 140, 5, Axes: true, "s", "s");
        var linear = new CubicBezier(0.25, 0.25, 0.75, 0.75);

        Assert.Equal(5, frame.Sample(linear, 3));
        Assert.Equal(0.5, frame.Sample(linear, 130));
        Assert.Equal(2.75, frame.Sample(linear, 67.5), 3);
    }
}
