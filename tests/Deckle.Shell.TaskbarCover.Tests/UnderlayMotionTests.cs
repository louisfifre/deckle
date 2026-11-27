using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (Louis, 2026-11-26): every change of the strip under
// the faded taskbar dissolves, quickly, its blur rising and falling with its
// opacity; a slide was tried and dropped.
[Trait("Category", "unit")]
public class UnderlayMotionTests
{
    [Fact]
    public void AStripFadingInGoesFromNothingToOpaque()
    {
        Assert.Equal(0, UnderlayMotion.FadeAlpha(0, 255, 0));
        Assert.InRange(UnderlayMotion.FadeAlpha(0, 255, UnderlayMotion.FadeMs / 2), 1, 254);
        Assert.Equal(255, UnderlayMotion.FadeAlpha(0, 255, UnderlayMotion.FadeMs));
        Assert.True(UnderlayMotion.FadeDone(UnderlayMotion.FadeMs));
    }

    [Fact]
    public void AStripFadingOutGoesFromOpaqueToGone()
    {
        Assert.Equal(255, UnderlayMotion.FadeAlpha(255, 0, 0));
        Assert.Equal(0, UnderlayMotion.FadeAlpha(255, 0, UnderlayMotion.FadeMs));
    }

    [Fact]
    public void AFadeTurnedRoundMidwayStartsFromWhereTheStripStands()
    {
        byte halfway = UnderlayMotion.FadeAlpha(255, 0, UnderlayMotion.FadeMs / 2);

        Assert.Equal(halfway, UnderlayMotion.FadeAlpha(halfway, 255, 0));
        Assert.Equal(255, UnderlayMotion.FadeAlpha(halfway, 255, UnderlayMotion.FadeMs));
    }

    // Windows' Fast Out, Slow In, cubic-bezier(0, 0, 0, 1): at x = 0.125 the
    // curve's parameter is 0.5, where its y is exactly 0.5.
    [Fact]
    public void TheCurveIsWindowsFastOutSlowIn()
    {
        Assert.Equal(0.5, UnderlayMotion.FastOutSlowIn(0.125), 9);
        Assert.Equal(0, UnderlayMotion.FastOutSlowIn(0));
        Assert.Equal(1, UnderlayMotion.FastOutSlowIn(1), 9);
    }

    [Fact]
    public void AFadeChangesFastestAtItsStart()
    {
        long third = UnderlayMotion.FadeMs / 3;
        int first = UnderlayMotion.FadeAlpha(0, 255, third);
        int second = UnderlayMotion.FadeAlpha(0, 255, 2 * third) - first;

        Assert.True(first > second);
    }
    [Fact]
    public void AColourFadesChannelByChannelOnTheSameCurve()
    {
        const uint from = 0x00102030, to = 0x00F0E0D0;
        long half = UnderlayMotion.FadeMs / 2;

        Assert.Equal(from, UnderlayMotion.FadeColor(from, to, 0));
        Assert.Equal(to, UnderlayMotion.FadeColor(from, to, UnderlayMotion.FadeMs));
        uint mid = UnderlayMotion.FadeColor(from, to, half);
        Assert.Equal(UnderlayMotion.FadeAlpha(0x30, 0xD0, half), (byte)mid);
        Assert.Equal(UnderlayMotion.FadeAlpha(0x10, 0xF0, half), (byte)(mid >> 16));
    }
}

// The strip's single path (Louis, 2026-11-27): every change, appearing,
// leaving or a new colour, is the same fade from what is shown.
[Trait("Category", "unit")]
public class UnderlayFadeTests
{
    private static readonly UnderlayLook Green = new(255, 0x0040A020, 255);
    private static readonly UnderlayLook Orange = new(255, 0x0000A0E0, 255);

    [Fact]
    public void AStripThatAppearsOnlyFadesItsOpacity()
    {
        var fade = new UnderlayFade();
        fade.MoveTo(Green, animate: true, now: 1000);

        Assert.Equal(new UnderlayLook(0, Green.Color, 255), fade.Shown);
        fade.Tick(1000 + UnderlayMotion.FadeMs / 2);
        Assert.Equal(Green.Color, fade.Shown.Color);
        Assert.InRange(fade.Shown.Alpha, 1, 254);
        fade.Tick(1000 + UnderlayMotion.FadeMs);
        Assert.Equal(Green, fade.Shown);
        Assert.False(fade.Moving);
    }

    [Fact]
    public void AStripThatLeavesKeepsItsColourAndFadesAway()
    {
        var fade = new UnderlayFade();
        fade.MoveTo(Green, animate: false, now: 0);
        fade.MoveTo(Green with { Alpha = 0 }, animate: true, now: 100);

        fade.Tick(100 + UnderlayMotion.FadeMs);
        Assert.Equal(Green with { Alpha = 0 }, fade.Shown);
    }

    [Fact]
    public void ANewColourDissolvesFromTheOneShown()
    {
        var fade = new UnderlayFade();
        fade.MoveTo(Green, animate: false, now: 0);
        fade.MoveTo(Orange, animate: true, now: 100);

        Assert.Equal(Green, fade.Shown);
        fade.Tick(100 + UnderlayMotion.FadeMs / 2);
        Assert.Equal(255, fade.Shown.Alpha);
        Assert.Equal(UnderlayMotion.FadeColor(Green.Color, Orange.Color, UnderlayMotion.FadeMs / 2), fade.Shown.Color);
    }

    [Fact]
    public void AChangeAskedMidFadeStartsWhereTheStripStands()
    {
        var fade = new UnderlayFade();
        fade.MoveTo(Green, animate: true, now: 0);
        fade.Tick(UnderlayMotion.FadeMs / 2);
        var halfway = fade.Shown;

        fade.MoveTo(Green with { Alpha = 0 }, animate: true, now: UnderlayMotion.FadeMs / 2);
        fade.Tick(UnderlayMotion.FadeMs / 2);
        Assert.Equal(halfway, fade.Shown);
    }

    [Fact]
    public void TheSameAskRepeatedLeavesTheFadeRunning()
    {
        var fade = new UnderlayFade();
        fade.MoveTo(Green, animate: true, now: 0);
        fade.MoveTo(Green, animate: true, now: 200);

        fade.Tick(UnderlayMotion.FadeMs);
        Assert.Equal(Green, fade.Shown);
    }

    [Fact]
    public void WithoutAnimationsEveryChangeIsInstant()
    {
        var fade = new UnderlayFade();
        fade.MoveTo(Green, animate: false, now: 0);
        Assert.Equal(Green, fade.Shown);
        fade.MoveTo(Orange, animate: false, now: 10);
        Assert.Equal(Orange, fade.Shown);
        Assert.False(fade.Moving);
    }
}
