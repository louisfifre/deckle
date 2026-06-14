using Deckle.Core;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// Tests de comportement sur CoverGeometry — le contrat observable : la
// zone de révélation part de l'arête d'écran où la taskbar est ancrée,
// s'étend de RevealZoneDepth vers l'intérieur, et reste bornée à
// l'étendue de la bande le long de l'arête (un curseur près de la même
// arête sur un autre moniteur n'y est pas).
[Trait("Category", "unit")]
public class CoverGeometryTests
{
    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom) =>
        new() { left = left, top = top, right = right, bottom = bottom };

    private static POINT Point(int x, int y) => new() { X = x, Y = y };

    // Bande verticale gauche 44 px de large sur un écran 2560×1440,
    // l'ancrage du standalone d'origine.
    private static readonly NativeMethods.RECT LeftBand = Rect(0, 0, 44, 1440);

    [Fact]
    public void LeftEdgeZoneExtendsInwardFromTheScreenEdge()
    {
        var zone = CoverGeometry.RevealZone(LeftBand, TaskbarEdge.Left, 192);

        // Sur la bande elle-même comme au-delà, jusqu'à la profondeur exclue.
        Assert.True(CoverGeometry.Contains(zone, Point(0, 700)));
        Assert.True(CoverGeometry.Contains(zone, Point(191, 700)));
        Assert.False(CoverGeometry.Contains(zone, Point(192, 700)));
    }

    [Fact]
    public void RightEdgeZoneExtendsInwardFromTheScreenEdge()
    {
        var band = Rect(2516, 0, 2560, 1440);
        var zone = CoverGeometry.RevealZone(band, TaskbarEdge.Right, 192);

        Assert.True(CoverGeometry.Contains(zone, Point(2559, 700)));
        Assert.True(CoverGeometry.Contains(zone, Point(2368, 700)));
        Assert.False(CoverGeometry.Contains(zone, Point(2367, 700)));
    }

    [Fact]
    public void TopEdgeZoneExtendsInwardFromTheScreenEdge()
    {
        var band = Rect(0, 0, 2560, 48);
        var zone = CoverGeometry.RevealZone(band, TaskbarEdge.Top, 192);

        Assert.True(CoverGeometry.Contains(zone, Point(1280, 0)));
        Assert.True(CoverGeometry.Contains(zone, Point(1280, 191)));
        Assert.False(CoverGeometry.Contains(zone, Point(1280, 192)));
    }

    [Fact]
    public void BottomEdgeZoneExtendsInwardFromTheScreenEdge()
    {
        var band = Rect(0, 1392, 2560, 1440);
        var zone = CoverGeometry.RevealZone(band, TaskbarEdge.Bottom, 192);

        Assert.True(CoverGeometry.Contains(zone, Point(1280, 1439)));
        Assert.True(CoverGeometry.Contains(zone, Point(1280, 1248)));
        Assert.False(CoverGeometry.Contains(zone, Point(1280, 1247)));
    }

    [Fact]
    public void ZoneStaysBoundedToTheBandExtentAlongTheEdge()
    {
        // Taskbar verticale sur le moniteur principal ; un second moniteur
        // au-dessus partagerait la même plage X près de l'arête gauche —
        // son curseur ne doit pas révéler la taskbar d'en bas.
        var zone = CoverGeometry.RevealZone(LeftBand, TaskbarEdge.Left, 192);

        Assert.False(CoverGeometry.Contains(zone, Point(100, -1)));
        Assert.False(CoverGeometry.Contains(zone, Point(100, 1440)));
        Assert.True(CoverGeometry.Contains(zone, Point(100, 0)));
        Assert.True(CoverGeometry.Contains(zone, Point(100, 1439)));
    }
}
