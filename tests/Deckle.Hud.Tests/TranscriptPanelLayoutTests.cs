using Windows.Graphics;
using Xunit;

namespace Deckle.Hud.Tests;

[Trait("Category", "unit")]
public sealed class TranscriptPanelLayoutTests
{
    private const int Width = 360;
    private const int Margin = 96;
    private const int Padding = 24;
    private const double LineHeight = 20;

    private static readonly RectInt32 WorkArea = new(0, 0, 1920, 1032);

    [Fact]
    public void ComputeRectPx_RightSide_AtScaleOne()
    {
        var rect = TranscriptPanelLayout.ComputeRectPx(WorkArea, 1.0, rightSide: true, Width, Margin, Padding, LineHeight, 3);

        Assert.Equal(new RectInt32(1920 - 96 - 360, 1032 - 96 - 84, 360, 84), rect);
    }

    [Fact]
    public void ComputeRectPx_LeftSide_AtScaleOne()
    {
        var rect = TranscriptPanelLayout.ComputeRectPx(WorkArea, 1.0, rightSide: false, Width, Margin, Padding, LineHeight, 3);

        Assert.Equal(new RectInt32(96, 1032 - 96 - 84, 360, 84), rect);
    }

    [Theory]
    [InlineData(true, 2880 - 144 - 540)]
    [InlineData(false, 144)]
    public void ComputeRectPx_ScalesDipsToPx(bool rightSide, int expectedX)
    {
        var workArea = new RectInt32(0, 0, 2880, 1548);

        var rect = TranscriptPanelLayout.ComputeRectPx(workArea, 1.5, rightSide, Width, Margin, Padding, LineHeight, 3);

        // (24 + 3 * 20) * 1.5 = 126 px high, 360 * 1.5 = 540 px wide, margin 144 px.
        Assert.Equal(new RectInt32(expectedX, 1548 - 144 - 126, 540, 126), rect);
    }

    [Fact]
    public void ComputeRectPx_KeepsTheBottomEdgeAnchored_AsLinesGrow()
    {
        int expectedBottom = WorkArea.Height - Margin;

        for (int lines = 1; lines <= 10; lines++)
        {
            var rect = TranscriptPanelLayout.ComputeRectPx(WorkArea, 1.0, true, Width, Margin, Padding, LineHeight, lines);
            Assert.Equal(expectedBottom, rect.Y + rect.Height);
            Assert.Equal(Padding + lines * LineHeight, rect.Height);
        }
    }

    [Fact]
    public void ComputeRectPx_FollowsAWorkAreaWithANonZeroOrigin()
    {
        var secondMonitor = new RectInt32(-1920, 200, 1920, 1032);

        var right = TranscriptPanelLayout.ComputeRectPx(secondMonitor, 1.0, true, Width, Margin, Padding, LineHeight, 1);
        var left = TranscriptPanelLayout.ComputeRectPx(secondMonitor, 1.0, false, Width, Margin, Padding, LineHeight, 1);

        Assert.Equal(-96 - 360, right.X);
        Assert.Equal(-1920 + 96, left.X);
        Assert.Equal(200 + 1032 - 96, right.Y + right.Height);
    }

    [Fact]
    public void MaxVisibleLines_CountsTheWholeLinesBetweenTheMargins()
    {
        // 1032 - 2 * 96 - 24 = 816 dip available, 40.8 lines of 20 dip.
        Assert.Equal(40, TranscriptPanelLayout.MaxVisibleLines(1032, Margin, Padding, LineHeight));
    }

    [Fact]
    public void MaxVisibleLines_IsAtLeastOne_OnATinyWorkArea()
    {
        Assert.Equal(1, TranscriptPanelLayout.MaxVisibleLines(100, Margin, Padding, LineHeight));
    }

    [Theory]
    [InlineData(0, 40, 1)]
    [InlineData(3, 40, 3)]
    [InlineData(120, 40, 40)]
    public void VisibleLines_ClampsTheContentToTheMaximum(int contentLines, int maxLines, int expected)
    {
        Assert.Equal(expected, TranscriptPanelLayout.VisibleLines(contentLines, maxLines));
    }
}
