using Deckle.Transcription.Parakeet.Segments;
using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Segments;

[Trait("Category", "unit")]
public sealed class SegmentBuilderTests
{
    private static DecodedToken T(string text, int frame, float p = 1f) => new(0, text, frame, p);

    [Fact]
    public void Build_NoTokens_ReturnsNoSegments()
    {
        Assert.Empty(SegmentBuilder.Build([]));
    }

    [Fact]
    public void Build_SingleSentence_ReturnsOneSegmentWithTrimmedText()
    {
        var segs = SegmentBuilder.Build([T("\u2581Hello", 2), T("\u2581world", 4), T(".", 5)]);

        var s = Assert.Single(segs);
        Assert.Equal("Hello world.", s.Text);
        Assert.Equal(3, s.TextTokens);
        Assert.Equal(0f, s.NoSpeechProb);
    }

    [Fact]
    public void Build_Times_AreFramesTimesEightCentiseconds()
    {
        var s = Assert.Single(SegmentBuilder.Build([T("\u2581Hi", 10), T(".", 12)]));

        Assert.Equal(80, s.T0Cs);
        Assert.Equal(13 * 8, s.T1Cs); // end of the last token's frame
    }

    [Fact]
    public void Build_Offset_ShiftsBothTimes()
    {
        var s = Assert.Single(SegmentBuilder.Build([T("\u2581Hi", 10), T(".", 12)], offsetCs: 1000));

        Assert.Equal(1080, s.T0Cs);
        Assert.Equal(1104, s.T1Cs);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("?")]
    [InlineData("!")]
    [InlineData("\u2026")]
    public void Build_SentenceEndingMark_CutsBeforeNextWord(string mark)
    {
        var segs = SegmentBuilder.Build([T("\u2581One", 1), T(mark, 2), T("\u2581Two", 3), T(mark, 4)]);

        Assert.Equal(2, segs.Count);
        Assert.Equal("One" + mark, segs[0].Text);
        Assert.Equal("Two" + mark, segs[1].Text);
    }

    [Fact]
    public void Build_DotInsideWord_DoesNotCut()
    {
        var segs = SegmentBuilder.Build([T("\u2581It", 1), T("\u2581costs", 2), T("\u25813", 3), T(".", 4), T("5", 5), T(".", 6)]);

        var s = Assert.Single(segs);
        Assert.Equal("It costs 3.5.", s.Text);
    }

    [Fact]
    public void Build_LeadingSpaceInsteadOfMarker_StartsWordToo()
    {
        var segs = SegmentBuilder.Build([T(" Hi", 1), T(".", 2), T(" Yo", 3), T(".", 4)]);

        Assert.Equal(["Hi.", "Yo."], segs.Select(s => s.Text));
    }

    [Fact]
    public void Build_NoPunctuation_CutsAtMaxLengthOnAWordBoundary()
    {
        // 8 cs per frame, max 40 cs = 5 frames: words on frames 0, 2, 4, 6, 8.
        var segs = SegmentBuilder.Build(
            [T("\u2581a", 0), T("b", 1), T("\u2581c", 2), T("\u2581d", 4), T("\u2581e", 6), T("\u2581f", 8)],
            maxSegmentCs: 40);

        Assert.Equal(["ab c d", "e f"], segs.Select(s => s.Text));
        Assert.Equal(6 * 8, segs[1].T0Cs); // timeline stays continuous
    }

    [Fact]
    public void Build_Confidence_IsMeanAndMinTokenProbabilityIsMin()
    {
        var s = Assert.Single(SegmentBuilder.Build([T("\u2581a", 0, 0.5f), T("\u2581b", 1, 1f), T(".", 2, 0.75f)]));

        Assert.Equal(0.75f, s.Confidence, 5);
        Assert.Equal(0.5f, s.MinTokenProbability);
    }

    [Fact]
    public void Build_WhitespaceOnlyTokens_YieldNoSegment()
    {
        Assert.Empty(SegmentBuilder.Build([T("\u2581", 0), T(" ", 1)]));
    }

    [Fact]
    public void Build_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentBuilder.Build([], offsetCs: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentBuilder.Build([], maxSegmentCs: 0));
    }
}
