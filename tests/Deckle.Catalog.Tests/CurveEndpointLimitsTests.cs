using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// The rule the curve's endpoint handles follow: a drag or a key lands on the
// anchors' grain, inside their bounds, and keeps the frame drawable. The frame
// and the limits are the cut curve's defaults: origin (15 s, 5 s), end
// (120 s, 0.5 s), the anchors' bounds and the grain MagnitudeGrain gives them.
[Trait("Category", "unit")]
public class CurveEndpointLimitsTests
{
    private static readonly CurveFrame Cut = new(15, 5, 120, 0.5, 135.75, 6.25, Axes: true);

    private static readonly CurveEndpointLimits Limits = new(
        OriginX: new AxisLimit(0, 200, 5),
        OriginY: new AxisLimit(0.5, 15, 0.5),
        EndX: new AxisLimit(30, 230, 5),
        EndY: new AxisLimit(0.1, 2, 0.05));

    [Fact]
    public void ADraggedOriginLandsOnTheAnchorsGrain()
    {
        CurveFrame moved = Limits.MoveOrigin(Cut, 17.3, 5.2);

        Assert.Equal(15, moved.OriginX);
        Assert.Equal(5.0, moved.OriginY);
    }

    [Fact]
    public void TheOriginStopsOneStepBeforeTheEnd()
    {
        CurveFrame moved = Limits.MoveOrigin(Cut, 300, 5);

        Assert.Equal(115, moved.OriginX);
    }

    [Fact]
    public void TheEndStopsOneStepAfterTheOrigin()
    {
        CurveFrame frame = Cut with { OriginX = 40 };

        CurveFrame moved = Limits.MoveEnd(frame, 10, 0.5);

        Assert.Equal(45, moved.EndX);
    }

    [Fact]
    public void TheMinimumWaitNeverRisesAboveTheMaximum()
    {
        CurveFrame frame = Cut with { OriginY = 1.5 };

        CurveFrame moved = Limits.MoveEnd(frame, 100, 1.9);

        Assert.Equal(1.5, moved.EndY);
    }

    [Fact]
    public void AnAxisWithNoLimitIsHeld()
    {
        var limits = new CurveEndpointLimits(OriginX: new AxisLimit(0, 200, 5));

        CurveFrame moved = limits.MoveOrigin(Cut, 20, 99);

        Assert.Equal(20, moved.OriginX);
        Assert.Equal(5, moved.OriginY);
        Assert.False(limits.EndMovable);
    }

    [Fact]
    public void AnArrowKeyMovesOneStepPerAxis()
    {
        CurveFrame moved = Limits.NudgeOrigin(Cut, 1, -1);

        Assert.Equal(20, moved.OriginX);
        Assert.Equal(4.5, moved.OriginY);
    }

    [Fact]
    public void TheMovesKeepTheRestOfTheFrame()
    {
        CurveFrame moved = Limits.MoveEnd(Cut, 100, 1.0);

        Assert.Equal(Cut.XMax, moved.XMax);
        Assert.Equal(Cut.YMax, moved.YMax);
        Assert.Equal(Cut.OriginX, moved.OriginX);
        Assert.True(moved.Axes);
    }

    [Theory]
    [InlineData(0.149999, 0.15)]
    [InlineData(0.174, 0.15)]
    [InlineData(0.176, 0.2)]
    [InlineData(-3, 0)]
    [InlineData(4, 1)]
    public void AValueIsSnappedThenBounded(double value, double expected)
    {
        Assert.Equal(expected, new AxisLimit(0, 1, 0.05).Apply(value));
    }
}
