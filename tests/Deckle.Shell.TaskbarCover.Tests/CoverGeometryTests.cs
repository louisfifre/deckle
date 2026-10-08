using Deckle.Core;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// Behaviour of CoverGeometry, the observable contract: the approach zone
// starts at the screen edge the taskbar is anchored to, reaches
// ApproachZoneFactor × the band thickness inward (band included), stays
// bounded to the band's extent along the edge (a cursor near the same edge
// on another monitor is not in it), and the band's opacity eases from 0 on
// the band to 1 at the zone's outer boundary.
[Trait("Category", "unit")]
public class CoverGeometryTests
{
    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom) =>
        new() { left = left, top = top, right = right, bottom = bottom };

    private static POINT Point(int x, int y) => new() { X = x, Y = y };

    // One band per edge on a 2560×1440 screen: the 44 px vertical bar of the
    // original standalone, a 48 px horizontal bar.
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

    private static int ZoneDepth(TaskbarEdge edge)
        => CoverGeometry.ApproachZoneFactor * CoverGeometry.Thickness(Band(edge), edge);

    private static int Span(TaskbarEdge edge)
        => ZoneDepth(edge) - CoverGeometry.Thickness(Band(edge), edge);

    private static double Opacity(TaskbarEdge edge, POINT p)
        => CoverGeometry.Opacity(Band(edge), edge, p);

    public static TheoryData<TaskbarEdge> Edges =>
        new(TaskbarEdge.Left, TaskbarEdge.Top, TaskbarEdge.Right, TaskbarEdge.Bottom);

    [Fact]
    public void ApproachZoneIsFourTimesTheBandThickness()
    {
        // The contract Louis set: 4 × the bar, the bar included. For the
        // left 44 px band that is x = 175 inside, x = 176 outside.
        var band = Band(TaskbarEdge.Left);
        var zone = CoverGeometry.ApproachZone(band, TaskbarEdge.Left);

        Assert.Equal(4, CoverGeometry.ApproachZoneFactor);
        Assert.True(CoverGeometry.Contains(zone, Point(175, 700)));
        Assert.False(CoverGeometry.Contains(zone, Point(176, 700)));
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void ApproachZoneReachesTheFactorTimesTheThicknessFromTheAnchoredEdge(TaskbarEdge edge)
    {
        var zone = CoverGeometry.ApproachZone(Band(edge), edge);

        Assert.True(CoverGeometry.Contains(zone, FromScreenEdge(edge, 0)));
        Assert.True(CoverGeometry.Contains(zone, FromScreenEdge(edge, ZoneDepth(edge) - 1)));
        Assert.False(CoverGeometry.Contains(zone, FromScreenEdge(edge, ZoneDepth(edge))));
    }

    [Fact]
    public void ApproachZoneStaysBoundedToTheBandExtentAlongTheEdge()
    {
        // Vertical taskbar on the primary monitor; a second monitor above it
        // would share the same X range near the left edge — its cursor must
        // not uncover the taskbar below.
        var zone = CoverGeometry.ApproachZone(Band(TaskbarEdge.Left), TaskbarEdge.Left);

        Assert.False(CoverGeometry.Contains(zone, Point(100, -1)));
        Assert.False(CoverGeometry.Contains(zone, Point(100, 1440)));
        Assert.True(CoverGeometry.Contains(zone, Point(100, 0)));
        Assert.True(CoverGeometry.Contains(zone, Point(100, 1439)));
    }

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
        Assert.Equal(1.0, Opacity(edge, FromBand(edge, Span(edge))));
        Assert.Equal(1.0, Opacity(edge, FromBand(edge, Span(edge) + 500)));
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void OpacityIsHalfAtMidDistance(TaskbarEdge edge)
    {
        Assert.Equal(0.5, Opacity(edge, FromBand(edge, Span(edge) / 2)), precision: 9);
    }

    [Theory]
    [MemberData(nameof(Edges))]
    public void OpacityRisesStrictlyWithDistance(TaskbarEdge edge)
    {
        int span = Span(edge);
        double near = Opacity(edge, FromBand(edge, span / 4));
        double mid  = Opacity(edge, FromBand(edge, span / 2));
        double far  = Opacity(edge, FromBand(edge, 3 * span / 4));

        Assert.True(0.0 < near && near < mid && mid < far && far < 1.0);
    }

    [Fact]
    public void OpacityIsOneBesideTheBandOutsideItsExtent()
    {
        // Close to the left screen edge but past the band's bottom end: the
        // cover stays opaque, the cursor is not approaching this taskbar.
        Assert.Equal(1.0, Opacity(TaskbarEdge.Left, Point(10, 1440)));
        Assert.Equal(1.0, Opacity(TaskbarEdge.Left, Point(10, -1)));
    }
}
