using Xunit;

namespace Deckle.Hud.Tests;

[Trait("Category", "unit")]
public sealed class ProximityFadeTests
{
    private const double Near = 10;
    private const double Far = 128;

    [Theory]
    [InlineData(40, 255)]   // HUD: fades down as the cursor approaches
    [InlineData(255, 40)]   // panel: opaque near, near-invisible far
    public void Alpha_ReturnsEndpointsAtAndBeyondTheRadii(byte nearAlpha, byte farAlpha)
    {
        Assert.Equal(nearAlpha, ProximityFade.Alpha(0, 1.0, Near, Far, nearAlpha, farAlpha));
        Assert.Equal(nearAlpha, ProximityFade.Alpha(Near, 1.0, Near, Far, nearAlpha, farAlpha));
        Assert.Equal(farAlpha, ProximityFade.Alpha(Far, 1.0, Near, Far, nearAlpha, farAlpha));
        Assert.Equal(farAlpha, ProximityFade.Alpha(Far * 10, 1.0, Near, Far, nearAlpha, farAlpha));
    }

    [Fact]
    public void Alpha_AtMidpoint_IsTheMeanOfTheEndpoints()
    {
        double mid = (Near + Far) / 2;

        Assert.Equal(100, ProximityFade.Alpha(mid, 1.0, Near, Far, 0, 200));
        Assert.Equal(100, ProximityFade.Alpha(mid, 1.0, Near, Far, 200, 0));
    }

    [Theory]
    [InlineData(40, 255)]
    [InlineData(255, 40)]
    public void Alpha_MovesMonotonicallyFromNearToFar(byte nearAlpha, byte farAlpha)
    {
        int direction = farAlpha > nearAlpha ? 1 : -1;
        byte previous = ProximityFade.Alpha(0, 1.0, Near, Far, nearAlpha, farAlpha);
        for (double d = 1; d <= Far + 10; d += 1)
        {
            byte current = ProximityFade.Alpha(d, 1.0, Near, Far, nearAlpha, farAlpha);
            Assert.True((current - previous) * direction >= 0, $"not monotonic at {d}px");
            previous = current;
        }
    }

    [Fact]
    public void Alpha_ScalesTheRadiiByDpi()
    {
        // At 1.5x the far radius sits at 192 px: 128 px is still inside the
        // ramp, 192 px reaches the far endpoint.
        Assert.NotEqual((byte)255, ProximityFade.Alpha(Far, 1.5, Near, Far, 40, 255));
        Assert.Equal(255, ProximityFade.Alpha(Far * 1.5, 1.5, Near, Far, 40, 255));
        Assert.Equal(40, ProximityFade.Alpha(Near * 1.5, 1.5, Near, Far, 40, 255));
    }

    [Theory]
    [InlineData(150, 150)]  // inside
    [InlineData(100, 120)]  // on the left edge
    [InlineData(300, 200)]  // bottom-right corner
    public void DistanceToRectPx_IsZeroInsideTheRect(int x, int y)
    {
        Assert.Equal(0, ProximityFade.DistanceToRectPx(x, y, 100, 100, 300, 200));
    }

    [Fact]
    public void DistanceToRectPx_OutsideACorner_IsEuclidean()
    {
        // 30 px left and 40 px above the top-left corner.
        Assert.Equal(50, ProximityFade.DistanceToRectPx(70, 60, 100, 100, 300, 200), 9);
    }

    [Fact]
    public void DistanceToRectPx_BesideAnEdge_IsTheGapToThatEdge()
    {
        Assert.Equal(25, ProximityFade.DistanceToRectPx(325, 150, 100, 100, 300, 200), 9);
    }
}
