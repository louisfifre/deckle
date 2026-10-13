using Deckle.Chrono;
using Xunit;

namespace Deckle.Chrono.Tests;

[Trait("Category", "unit")]
public class ChronoFormatterTests
{
    [Fact]
    public void DecomposeReturnsZeroForTimeSpanZero()
    {
        var d = ChronoFormatter.Decompose(TimeSpan.Zero);

        Assert.Equal(0, d.Minutes);
        Assert.Equal(0, d.Seconds);
        Assert.Equal(0, d.Centiseconds);
    }

    [Fact]
    public void DecomposeReturnsExpectedComponentsForMixedDuration()
    {
        // 1h 23m 45s 670ms: covers minutes above 60, non-zero seconds,
        // centiseconds = milliseconds / 10 (670ms → 67cs).
        var elapsed = new TimeSpan(0, 1, 23, 45, 670);

        var d = ChronoFormatter.Decompose(elapsed);

        Assert.Equal(83, d.Minutes);
        Assert.Equal(45, d.Seconds);
        Assert.Equal(67, d.Centiseconds);
    }

    [Fact]
    public void DecomposeWrapsMinutesPastOneHundred()
    {
        // Keeps the two-digit slot, and the HUD's width, stable for long sessions.
        var elapsed = TimeSpan.FromMinutes(105);

        var d = ChronoFormatter.Decompose(elapsed);

        Assert.Equal(5, d.Minutes);
    }

    [Fact]
    public void DecomposeWithCapZeroAppliesNoCap()
    {
        var elapsed = TimeSpan.FromSeconds(120);

        var d = ChronoFormatter.Decompose(elapsed, capSeconds: 0);

        Assert.Equal(2, d.Minutes);
        Assert.Equal(0, d.Seconds);
    }

    [Fact]
    public void DecomposeClampsElapsedWhenAboveCap()
    {
        var elapsed = TimeSpan.FromSeconds(120);

        var d = ChronoFormatter.Decompose(elapsed, capSeconds: 60);

        Assert.Equal(1, d.Minutes);
        Assert.Equal(0, d.Seconds);
    }

    [Fact]
    public void DecomposeLeavesElapsedUnchangedWhenBelowCap()
    {
        var elapsed = TimeSpan.FromSeconds(30);

        var d = ChronoFormatter.Decompose(elapsed, capSeconds: 60);

        Assert.Equal(0, d.Minutes);
        Assert.Equal(30, d.Seconds);
    }

    [Fact]
    public void FormatMmSsCsPadsAllComponentsToTwoDigits()
    {
        var elapsed = new TimeSpan(0, 0, 3, 4, 50);

        Assert.Equal("03:04.05", ChronoFormatter.FormatMmSsCs(elapsed));
    }

    [Fact]
    public void FormatMmSsCsReturnsZeroFormattedForZero()
    {
        Assert.Equal("00:00.00", ChronoFormatter.FormatMmSsCs(TimeSpan.Zero));
    }
}
