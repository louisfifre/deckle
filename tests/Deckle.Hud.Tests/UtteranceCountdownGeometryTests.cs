using System;
using System.Numerics;
using Deckle.Composition;
using Xunit;

namespace Deckle.Hud.Tests;

public sealed class UtteranceCountdownGeometryTests
{
    private const float Tolerance = 0.0001f;
    private static readonly Vector2 Centre = new(150, 150);

    // Canonical mask boundary rays: the two edges of the [0°, 90°] slice.
    private static readonly Vector2 FirstBoundary = new(100, 0);
    private static readonly Vector2 SecondBoundary = new(0, 100);

    private static Vector2 Image(Vector2 offset, Matrix3x2 transform)
        => Vector2.Transform(Centre + offset, transform) - Centre;

    private static float Ratio(Vector2 v) => MathF.Abs(v.X) / MathF.Abs(v.Y);

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void FrontReachGrowsLinearlyAlongTheTopEdge(float progress)
    {
        var hud = new Vector2(272, 78);
        var transform = HudComposition.GetUtteranceCountdownTransform(Centre, hud, 0f, 1f, progress);
        float expected = progress * (hud.X / 2) / (hud.Y / 2);

        foreach (var boundary in new[] { FirstBoundary, SecondBoundary })
        {
            var ray = Image(boundary, transform);
            Assert.True(ray.Y < 0, "The front wedge must point up.");
            Assert.InRange(MathF.Abs(Ratio(ray) - expected), 0f, Tolerance);
        }
    }

    [Theory]
    [Trait("Category", "regression")]
    [InlineData(272f, 78f)]
    [InlineData(400f, 120f)]
    [InlineData(300f, 120f)]
    public void FullReachLightsTheFrameCorners(float width, float height)
    {
        var hud = new Vector2(width, height);
        var transform = HudComposition.GetUtteranceCountdownTransform(Centre, hud, 0f, 1f, 1f);
        float corner = (width / 2) / (height / 2);

        foreach (var boundary in new[] { FirstBoundary, SecondBoundary })
            Assert.InRange(MathF.Abs(Ratio(Image(boundary, transform)) - corner), 0f, Tolerance);
    }

    private static bool IsFinite(Matrix3x2 m)
        => float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M21)
        && float.IsFinite(m.M22) && float.IsFinite(m.M31) && float.IsFinite(m.M32);

    // A negative start reach delays the opening: the wedge stays at its
    // smallest spread until the lerped reach crosses zero, then grows.
    [Fact]
    public void NegativeStartReachWaitsClosedUntilTheCrossover()
    {
        var hud = new Vector2(272, 78);
        float startReach = -0.5f, endReach = 1f;
        float crossover = -startReach / (endReach - startReach);

        float RatioAt(float progress)
        {
            var transform = HudComposition.GetUtteranceCountdownTransform(Centre, hud, startReach, endReach, progress);
            Assert.True(IsFinite(transform), $"Transform at progress {progress} is not finite.");
            return Ratio(Image(FirstBoundary, transform));
        }

        float closed = RatioAt(0f);
        Assert.False(float.IsNaN(closed));
        Assert.InRange(MathF.Abs(RatioAt(crossover * 0.9f) - closed), 0f, Tolerance);
        Assert.InRange(MathF.Abs(RatioAt(crossover) - closed), 0f, Tolerance);
        Assert.True(RatioAt((crossover + 1f) / 2f) > closed, "The wedge must open after the crossover.");
    }

    // An end reach above 1 runs the boundary rays past the frame corners.
    [Fact]
    public void EndReachAboveOneRunsPastTheCorners()
    {
        var hud = new Vector2(272, 78);
        var atCorners = HudComposition.GetUtteranceCountdownTransform(Centre, hud, 0f, 1f, 1f);
        var pastCorners = HudComposition.GetUtteranceCountdownTransform(Centre, hud, 0f, 2f, 1f);

        Assert.True(IsFinite(pastCorners));
        float corner = Ratio(Image(FirstBoundary, atCorners));
        float wider = Ratio(Image(FirstBoundary, pastCorners));
        Assert.True(wider > corner, $"Reach 2 ratio {wider} must exceed the corner ratio {corner}.");
        Assert.InRange(MathF.Abs(wider - 2f * corner), 0f, Tolerance);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(1f)]
    public void BottomWedgeMirrorsTheTopOne(float progress)
    {
        var hud = new Vector2(272, 78);
        var transform = HudComposition.GetUtteranceCountdownTransform(Centre, hud, 0f, 1f, progress);

        foreach (var boundary in new[] { FirstBoundary, SecondBoundary })
        {
            var top = Image(boundary, transform);
            var bottom = Image(-boundary, transform);
            Assert.True(bottom.Y > 0, "The opposite wedge must point down.");
            Assert.InRange(MathF.Abs(Ratio(bottom) - Ratio(top)), 0f, Tolerance);
        }
    }
}
