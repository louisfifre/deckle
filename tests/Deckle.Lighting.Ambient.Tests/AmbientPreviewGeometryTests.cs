using Deckle.Lighting;
using Deckle.Vision;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// While Ambient Light is off, the preview draws the display's frame and the
// zone bands from settings alone. The frame takes the captured display's
// size; the bands take the share of the frame the push loop averages, in whole
// cells of the grid that display gets.
[Trait("Category", "unit")]
public sealed class AmbientPreviewGeometryTests
{
    private static readonly CaptureMonitor Primary = new(@"\\.\DISPLAY1", IsPrimary: true, 1920, 1080);
    private static readonly CaptureMonitor Side = new(@"\\.\DISPLAY2", IsPrimary: false, 2560, 1440);

    [Fact]
    public void TheFrameFollowsTheChosenDisplayWhileItIsConnected()
    {
        var target = AmbientCaptureTarget.Resolve([Primary, Side], Side.DeviceName);

        Assert.Equal(Side, target);
    }

    [Fact]
    public void TheFrameFallsBackToThePrimaryDisplay()
    {
        Assert.Equal(Primary, AmbientCaptureTarget.Resolve([Side, Primary], selectedDeviceName: null));
        Assert.Equal(Primary, AmbientCaptureTarget.Resolve([Side, Primary], @"\\.\DISPLAY9"));
    }

    [Fact]
    public void NoDisplayGivesNoFrame()
    {
        Assert.Null(AmbientCaptureTarget.Resolve([], selectedDeviceName: null));
    }

    [Fact]
    public void ShareBandsRoundToTheCellsTheEngineAverages()
    {
        // 1920 × 1080 samples into 60 × 33 cells. A third of 33 rows is 11
        // rows; a third of 60 columns is 20 columns.
        var (cols, rows) = FrameSampler.GridFor(1920, 1080);

        var (topBottom, leftRight) = AmbientZoneSampler.ResolveBandShares(
            BorderThicknessMode.Share, depthShare: 0.33, cellsPerEdge: 8, cols, rows);

        Assert.Equal(11.0 / 33, topBottom, precision: 9);
        Assert.Equal(20.0 / 60, leftRight, precision: 9);
    }

    [Fact]
    public void CellBandsKeepTheSameCountOnEveryEdge()
    {
        var (topBottom, leftRight) = AmbientZoneSampler.ResolveBandShares(
            BorderThicknessMode.Cells, depthShare: 0.33, cellsPerEdge: 4, cols: 60, rows: 33);

        Assert.Equal(4.0 / 33, topBottom, precision: 9);
        Assert.Equal(4.0 / 60, leftRight, precision: 9);
    }

    [Fact]
    public void AHandEditedDepthIsClampedLikeTheEngineClampsIt()
    {
        var (topBottom, _) = AmbientZoneSampler.ResolveBandShares(
            BorderThicknessMode.Share, depthShare: 0.9, cellsPerEdge: 8, cols: 60, rows: 33);

        // 0.5 × 33 = 16.5, which rounds to 16 (banker's rounding, as in the engine).
        Assert.Equal(16.0 / 33, topBottom, precision: 9);
    }

    [Fact]
    public void NoGridGivesNoBands()
    {
        Assert.Equal((0.0, 0.0), AmbientZoneSampler.ResolveBandShares(
            BorderThicknessMode.Share, depthShare: 0.33, cellsPerEdge: 8, cols: 0, rows: 0));
    }
}
