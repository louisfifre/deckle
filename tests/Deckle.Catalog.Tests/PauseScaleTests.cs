using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// The pause strip's zoom: a burst that holds the minimum-speech bracket, the
// pause up to the cut, and a ruler that ends on a round label.
[Trait("Category", "unit")]
public class PauseScaleTests
{
    [Fact]
    public void TheDefaultsFitARulerOfOnePointTwoSeconds()
    {
        // Minimum speech 0.25 s, margin 0.15 s, shortest cut 0.5 s.
        PauseScale scale = PauseScale.For(0.25, 0.15, 0.5);

        Assert.Equal(0.5, scale.SpeechEnd);
        Assert.Equal(0.2, scale.TickStep, 9);
        Assert.Equal(1.2, scale.Span, 9);
    }

    [Theory]
    [InlineData(0.25, 0.15, 0.5)]
    [InlineData(1.0, 0.0, 2.0)]
    [InlineData(0.0, 1.0, 0.1)]
    [InlineData(0.05, 0.05, 0.1)]
    public void TheSpanHoldsTheWholePauseAndEndsOnATick(double minimumSpeech, double margin, double cut)
    {
        PauseScale scale = PauseScale.For(minimumSpeech, margin, cut);

        Assert.True(scale.Span >= scale.SpeechEnd + System.Math.Max(cut, margin));
        double ticks = scale.Span / scale.TickStep;
        Assert.Equal(System.Math.Round(ticks), ticks, 6);
    }

    [Fact]
    public void TheBurstIsTwiceTheMinimumSpeechAndAtLeastHalfASecond()
    {
        Assert.Equal(0.8, PauseScale.For(0.4, 0.15, 0.5).SpeechEnd, 9);
        Assert.Equal(0.5, PauseScale.For(0.1, 0.15, 0.5).SpeechEnd, 9);
    }

    [Fact]
    public void ABracketPastTheBurstMarksItAsABlip()
    {
        PauseScale scale = PauseScale.For(0.25, 0.15, 0.5);

        Assert.False(scale.IsBlip(0.5));
        Assert.True(scale.IsBlip(0.55));
    }

    [Fact]
    public void FractionAndSecondsAreInverse()
    {
        PauseScale scale = PauseScale.For(0.25, 0.15, 0.5);

        Assert.Equal(0.65, scale.Seconds(scale.Fraction(0.65)), 9);
        Assert.Equal(1.0, scale.Fraction(99));
    }
}
