using Deckle.Core;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (Louis, 2026-10-15): the approach zone is the band plus
// a fixed span set in device-independent pixels, scaled by the monitor's DPI,
// and the fade runs over that span. Band thickness no longer enters the span.
[Trait("Category", "unit")]
public class CoverGeometryTests
{
    private const double Dpi100 = 1.0;
    private const double Dpi150 = 1.5;

    // The default distance, 64 DIP, the one every edge is checked at.
    private const int Standard = ApproachDistance.DefaultDip;

    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom) =>
        new() { left = left, top = top, right = right, bottom = bottom };

    private static POINT Point(int x, int y) => new() { X = x, Y = y };

    private static NativeMethods.RECT Band(TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Left  => Rect(0, 0, 44, 1440),
        TaskbarEdge.Right => Rect(2516, 0, 2560, 1440),
        TaskbarEdge.Top   => Rect(0, 0, 2560, 48),
        _                 => Rect(0, 1392, 2560, 1440),
    };

    // A point in the middle of the band's extent, `pixels` away from the
    // anchored screen edge, moving inward.
    private static POINT FromScreenEdge(TaskbarEdge edge, int pixels)
    {
        var b = Band(edge);
        int midX = (b.left + b.right) / 2;
        int midY = (b.top + b.bottom) / 2;
        return edge switch
        {
            TaskbarEdge.Left  => Point(b.left + pixels, midY),
            TaskbarEdge.Right => Point(b.right - 1 - pixels, midY),
            TaskbarEdge.Top   => Point(midX, b.top + pixels),
            _                 => Point(midX, b.bottom - 1 - pixels),
        };
    }

    // A point `distance` pixels past the band's inner edge.
    private static POINT FromBand(TaskbarEdge edge, int distance)
        => FromScreenEdge(edge, CoverGeometry.Thickness(Band(edge), edge) + distance);

    // The default distance at 96 DPI, the shape every edge is checked in.
    private static int Span => CoverGeometry.ApproachSpan(Standard, Dpi100);

    private static int ZoneDepth(TaskbarEdge edge)
        => CoverGeometry.Thickness(Band(edge), edge) + Span;

    private static NativeMethods.RECT Zone(TaskbarEdge edge)
        => CoverGeometry.ApproachZone(Band(edge), edge, Standard, Dpi100);

    private static double Opacity(TaskbarEdge edge, POINT p)
        => CoverGeometry.Opacity(Band(edge), edge, p, Standard, Dpi100);

    public static TheoryData<TaskbarEdge> Edges =>
        new(TaskbarEdge.Left, TaskbarEdge.Top, TaskbarEdge.Right, TaskbarEdge.Bottom);

    // ── The span, from DIP to the monitor's pixels ───────────────────────────

    [Fact]
    public void ADipIsAPixelAt100Percent()
    {
        Assert.Equal(Standard, CoverGeometry.ApproachSpan(Standard, Dpi100));
    }

    [Theory]
    [InlineData(32,  1.5,  48)]
    [InlineData(64,  1.5,  96)]
    [InlineData(128, 1.5,  192)]
    [InlineData(64,  1.25, 80)]
    [InlineData(32,  1.25, 40)]
    // The maintainer's 4K display at 200 %: the former Far level is 256
    // pixels there, not the 128 its old label suggested.
    [InlineData(128, 2.0,  256)]
    // 17.5 pixels round half up, the Dpi rule.
    [InlineData(10,  1.75, 18)]
    [InlineData(ApproachDistance.MaximumDip, 2.5, 800)]
    public void SpanIsTheDistanceInTheMonitorsPixels(int approachDip, double dpiScale, int pixels)
    {
        Assert.Equal(pixels, CoverGeometry.ApproachSpan(approachDip, dpiScale));
    }

    [Fact]
    public void OneDistanceReachesAsFarByHandOnMonitorsOfDifferentScales()
    {
        // The same setting on two taskbars, one on a 100 % monitor and one
        // on a 200 % monitor: each zone is converted with its own monitor's
        // scale, so past its band it reaches the same distance in DIP.
        var band100 = Rect(0, 1040, 1920, 1080);
        var band200 = Rect(1920, 2080, 5760, 2160);

        var zone100 = CoverGeometry.ApproachZone(band100, TaskbarEdge.Bottom, Standard, Dpi100);
        var zone200 = CoverGeometry.ApproachZone(band200, TaskbarEdge.Bottom, Standard, 2.0);

        Assert.Equal(Standard,     band100.top - zone100.top);
        Assert.Equal(Standard * 2, band200.top - zone200.top);
    }

    // ── The zone ─────────────────────────────────────────────────────────────

    [Fact]
    public void ApproachZoneIsTheBandPlusTheSpan()
    {
        // The contract on the left 44 px band at Standard: 44 + 64 = 108, so
        // x = 107 is inside, x = 108 outside.
        var zone = Zone(TaskbarEdge.Left);

        Assert.True(CoverGeometry.Contains(zone, Point(107, 700)));
        Assert.False(CoverGeometry.Contains(zone, Point(108, 700)));
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void ApproachZoneReachesTheThicknessPlusTheSpanFromTheAnchoredEdge(TaskbarEdge edge)
    {
        var zone = Zone(edge);

        Assert.True(CoverGeometry.Contains(zone, FromScreenEdge(edge, 0)));
        Assert.True(CoverGeometry.Contains(zone, FromScreenEdge(edge, ZoneDepth(edge) - 1)));
        Assert.False(CoverGeometry.Contains(zone, FromScreenEdge(edge, ZoneDepth(edge))));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void ApproachZoneOfEachDistanceEndsItsSpanPastTheBand(int approachDip)
    {
        var band = Band(TaskbarEdge.Left);
        var zone = CoverGeometry.ApproachZone(band, TaskbarEdge.Left, approachDip, Dpi100);
        int depth = CoverGeometry.Thickness(band, TaskbarEdge.Left) + CoverGeometry.ApproachSpan(approachDip, Dpi100);

        Assert.True(CoverGeometry.Contains(zone, Point(depth - 1, 700)));
        Assert.False(CoverGeometry.Contains(zone, Point(depth, 700)));
    }

    [Fact]
    public void ApproachZoneDoesNotGrowWithTheBandThickness()
    {
        // The point of the rule: a thin and a thick bottom taskbar both end
        // their zone the same distance past the band.
        var thin  = Rect(0, 1400, 2560, 1440);
        var thick = Rect(0, 1380, 2560, 1440);

        var thinZone  = CoverGeometry.ApproachZone(thin,  TaskbarEdge.Bottom, Standard, Dpi100);
        var thickZone = CoverGeometry.ApproachZone(thick, TaskbarEdge.Bottom, Standard, Dpi100);

        Assert.Equal(thin.top  - Span, thinZone.top);
        Assert.Equal(thick.top - Span, thickZone.top);
    }

    [Fact]
    public void ApproachZoneScalesWithTheMonitorDpi()
    {
        // Left 44 px band at 150 %: 44 + 96 = 140.
        var zone = CoverGeometry.ApproachZone(Band(TaskbarEdge.Left), TaskbarEdge.Left, Standard, Dpi150);

        Assert.True(CoverGeometry.Contains(zone, Point(139, 700)));
        Assert.False(CoverGeometry.Contains(zone, Point(140, 700)));
    }

    [Fact]
    public void ApproachZoneStaysBoundedToTheBandExtentAlongTheEdge()
    {
        // Vertical taskbar on the primary monitor; a second monitor above it
        // would share the same X range near the left edge — its cursor must
        // not uncover the taskbar below.
        var zone = Zone(TaskbarEdge.Left);

        Assert.False(CoverGeometry.Contains(zone, Point(100, -1)));
        Assert.False(CoverGeometry.Contains(zone, Point(100, 1440)));
        Assert.True(CoverGeometry.Contains(zone, Point(100, 0)));
        Assert.True(CoverGeometry.Contains(zone, Point(100, 1439)));
    }

    // ── The fade ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Edges))]
    public void OpacityIsZeroOnTheBand(TaskbarEdge edge)
    {
        Assert.Equal(0.0, Opacity(edge, FromScreenEdge(edge, 0)));
        Assert.Equal(0.0, Opacity(edge, FromBand(edge, -1)));
        Assert.Equal(0.0, Opacity(edge, FromBand(edge, 0)));
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void OpacityIsOneAtTheZoneBoundaryAndBeyond(TaskbarEdge edge)
    {
        Assert.Equal(1.0, Opacity(edge, FromBand(edge, Span)));
        Assert.Equal(1.0, Opacity(edge, FromBand(edge, Span + 500)));
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void OpacityIsHalfAtMidDistance(TaskbarEdge edge)
    {
        Assert.Equal(0.5, Opacity(edge, FromBand(edge, Span / 2)), precision: 9);
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void OpacityRisesStrictlyWithDistance(TaskbarEdge edge)
    {
        double near = Opacity(edge, FromBand(edge, Span / 4));
        double mid  = Opacity(edge, FromBand(edge, Span / 2));
        double far  = Opacity(edge, FromBand(edge, 3 * Span / 4));

        Assert.True(0.0 < near && near < mid && mid < far && far < 1.0);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void OpacityOfEachDistanceFadesOverItsSpan(int approachDip)
    {
        var band = Band(TaskbarEdge.Bottom);
        int span = CoverGeometry.ApproachSpan(approachDip, Dpi100);

        Assert.Equal(0.0, CoverGeometry.Opacity(band, TaskbarEdge.Bottom, FromBand(TaskbarEdge.Bottom, 0), approachDip, Dpi100));
        Assert.Equal(0.5, CoverGeometry.Opacity(band, TaskbarEdge.Bottom, FromBand(TaskbarEdge.Bottom, span / 2), approachDip, Dpi100), precision: 9);
        Assert.Equal(1.0, CoverGeometry.Opacity(band, TaskbarEdge.Bottom, FromBand(TaskbarEdge.Bottom, span), approachDip, Dpi100));
    }

    [Fact]
    public void OpacityFadesOverTheScaledSpan()
    {
        // Bottom band at 150 %: the Standard fade runs over 96 px, so 48 px
        // past the band is the midpoint and 64 px (the 100 % span) is not yet
        // opaque.
        var band = Band(TaskbarEdge.Bottom);

        Assert.Equal(0.5, CoverGeometry.Opacity(band, TaskbarEdge.Bottom, FromBand(TaskbarEdge.Bottom, 48), Standard, Dpi150), precision: 9);
        Assert.True(CoverGeometry.Opacity(band, TaskbarEdge.Bottom, FromBand(TaskbarEdge.Bottom, 64), Standard, Dpi150) < 1.0);
        Assert.Equal(1.0, CoverGeometry.Opacity(band, TaskbarEdge.Bottom, FromBand(TaskbarEdge.Bottom, 96), Standard, Dpi150));
    }

    [Fact]
    public void OpacityIsOneBesideTheBandOutsideItsExtent()
    {
        Assert.Equal(1.0, Opacity(TaskbarEdge.Left, Point(10, 1440)));
        Assert.Equal(1.0, Opacity(TaskbarEdge.Left, Point(10, -1)));
    }
}
