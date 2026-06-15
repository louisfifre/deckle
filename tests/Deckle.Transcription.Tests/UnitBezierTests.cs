using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// UnitBezier is the cubic-Bézier easing shared by the segmenter's hangover decay
// and the Playground plot. internal, reached through InternalsVisibleTo. Pure
// math : no state, no I/O — assert the curve contract directly.
[Trait("Category", "unit")]
public class UnitBezierTests
{
    [Fact]
    public void PassesThroughBothEndpoints()
    {
        var b = new UnitBezier(0.42, 0.30, 0.85, 0.50);
        Assert.Equal(0.0, b.Solve(0.0), 6);
        Assert.Equal(1.0, b.Solve(1.0), 6);
    }

    [Fact]
    public void DiagonalControlPointsGiveTheIdentity()
    {
        // Control points on the P0→P3 diagonal collapse the curve to y = x.
        var b = new UnitBezier(1.0 / 3.0, 1.0 / 3.0, 2.0 / 3.0, 2.0 / 3.0);
        for (double x = 0.0; x <= 1.0; x += 0.1)
            Assert.Equal(x, b.Solve(x), 4);
    }

    [Fact]
    public void IsMonotoneNonDecreasing()
    {
        var b = new UnitBezier(0.42, 0.30, 0.85, 0.50);
        double prev = -1.0;
        for (double x = 0.0; x <= 1.0; x += 0.02)
        {
            double y = b.Solve(x);
            Assert.True(y >= prev - 1e-9, $"y dipped at x={x}: {y} < {prev}");
            prev = y;
        }
    }

    [Fact]
    public void BottomRightCornerHugsZeroThenLeapsToOne()
    {
        // Both handles in the bottom-right corner → the curve stays near 0 across
        // most of x and snaps up only at the very end (a right angle there).
        var b = new UnitBezier(1.0, 0.0, 1.0, 0.0);
        Assert.True(b.Solve(0.5) < 0.05, $"expected near-zero at mid, got {b.Solve(0.5)}");
        Assert.True(b.Solve(0.95) < 0.5, $"expected still-low near the end, got {b.Solve(0.95)}");
    }

    [Fact]
    public void TopLeftCornerLeapsToOneThenFlattens()
    {
        // Both handles in the top-left corner → the mirror right angle: the curve
        // jumps up immediately and rides near 1 across most of x.
        var b = new UnitBezier(0.0, 1.0, 0.0, 1.0);
        Assert.True(b.Solve(0.5) > 0.95, $"expected near-one at mid, got {b.Solve(0.5)}");
        Assert.True(b.Solve(0.05) > 0.5, $"expected already-high near the start, got {b.Solve(0.05)}");
    }

    [Theory]
    // The shipping default tracks the previous slope-integral curve (contrast 3,
    // position 0.8, sharpness 20) to within ~0.03 across the ramp.
    [InlineData(0.2, 0.143)]
    [InlineData(0.5, 0.357)]
    [InlineData(0.7, 0.508)]
    [InlineData(0.9, 0.794)]
    public void DefaultReproducesThePreviousCurve(double x, double expected)
    {
        var b = new UnitBezier(0.42, 0.30, 0.85, 0.50);
        Assert.True(System.Math.Abs(b.Solve(x) - expected) < 0.035,
            $"x={x}: got {b.Solve(x):F3}, expected ≈ {expected:F3}");
    }
}
