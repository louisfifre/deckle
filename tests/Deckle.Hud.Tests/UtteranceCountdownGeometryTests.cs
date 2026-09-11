using System;
using System.Numerics;
using Deckle.Composition;
using Xunit;

namespace Deckle.Hud.Tests;

[Trait("Category", "regression")]
public sealed class UtteranceCountdownGeometryTests
{
    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void OpeningKeepsItsBaseFixedAndMovesItsFrontLinearly(float progress)
    {
        var centre = new Vector2(150, 150);
        var matrix = HudComposition.GetUtteranceCountdownTransform(centre, 12, -12, progress, false);
        var front = Vector2.Transform(centre + Vector2.UnitX * 100, matrix) - centre;
        var bottom = Vector2.Transform(centre + Vector2.UnitY * 100, matrix) - centre;
        float startSlope = MathF.Tan(12 * MathF.PI / 180);
        float endSlope = -startSlope;
        Assert.InRange(MathF.Abs(bottom.Y / bottom.X - startSlope), 0, 0.00001f);
        Assert.InRange(MathF.Abs(front.Y / front.X - (startSlope + (endSlope - startSlope) * progress)), 0, 0.00001f);
    }

    [Fact]
    public void MirroredMasksMeetTheSameHeightFromAnArbitraryStart()
    {
        var centre = new Vector2(150, 150);
        var right = HudComposition.GetUtteranceCountdownTransform(centre, 3, -10, 0.4f, false);
        var left = HudComposition.GetUtteranceCountdownTransform(centre, 3, -10, 0.4f, true);
        var rightRay = Vector2.Transform(centre + Vector2.UnitX * 100, right) - centre;
        var leftRay = Vector2.Transform(centre + Vector2.UnitX * 100, left) - centre;
        Assert.InRange(MathF.Abs(rightRay.Y - leftRay.Y), 0, 0.0001f);
        Assert.InRange(MathF.Abs(rightRay.X + leftRay.X), 0, 0.0001f);
        Assert.True(rightRay.X > 0);
        Assert.True(leftRay.X < 0);
    }

    [Fact]
    public void AllowedStartAnglesMeetTheVisibleSidesInsteadOfEnteringFromOutside()
    {
        var size = new Vector2(272, 78);
        float limit = HudComposition.UtteranceCountdownAngleLimit(size);
        float edgeHeight = size.X / 2 * MathF.Tan(limit * MathF.PI / 180);
        Assert.InRange(edgeHeight, 0, size.Y / 2 - 1);
        var defaults = new UtteranceCountdownConfig();
        Assert.InRange(defaults.StartDegrees, -limit, limit);
        Assert.InRange(defaults.EndDegrees, -limit, limit);
    }
}
