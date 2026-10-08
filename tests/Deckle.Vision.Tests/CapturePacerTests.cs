using Deckle.Vision;
using Xunit;

namespace Deckle.Vision.Tests;

[Trait("Category", "unit")]
public sealed class CapturePacerTests
{
    private const long TicksPerSecond = 1_000_000;

    private static int CountDelivered(CapturePacer pacer, double desktopHz, double seconds, double jitterSeconds = 0)
    {
        int delivered = 0;
        int frames = (int)(desktopHz * seconds);
        for (int k = 0; k < frames; k++)
        {
            // Alternate early/late arrivals around each refresh boundary.
            double jitter = (k % 2 == 0 ? -1 : 1) * jitterSeconds;
            long now = (long)((k / desktopHz + jitter) * TicksPerSecond) + TicksPerSecond;
            if (pacer.TryAdmit(now)) delivered++;
        }
        return delivered;
    }

    [Fact]
    public void DesktopAtTheTargetRateDeliversEveryFrameDespiteJitter()
    {
        var pacer = new CapturePacer(60, TicksPerSecond);

        int delivered = CountDelivered(pacer, desktopHz: 60, seconds: 5, jitterSeconds: 0.0008);

        Assert.Equal(300, delivered);
    }

    [Fact]
    public void FasterDesktopAveragesTheTargetRate()
    {
        var pacer = new CapturePacer(60, TicksPerSecond);

        int delivered = CountDelivered(pacer, desktopHz: 144, seconds: 10);

        Assert.InRange(delivered, 595, 605);
    }

    [Theory]
    [InlineData(30, 150)]
    [InlineData(15, 75)]
    public void LowerTargetDeliversAProportionalShare(int targetHz, int expected)
    {
        var pacer = new CapturePacer(targetHz, TicksPerSecond);

        int delivered = CountDelivered(pacer, desktopHz: 60, seconds: 5);

        Assert.Equal(expected, delivered);
    }

    [Fact]
    public void FrameAfterAStaticGapDoesNotOpenACatchUpBurst()
    {
        var pacer = new CapturePacer(60, TicksPerSecond);
        Assert.True(pacer.TryAdmit(0));

        // One second without desktop updates, then a 144 Hz burst.
        Assert.True(pacer.TryAdmit(TicksPerSecond));
        Assert.False(pacer.TryAdmit(TicksPerSecond + TicksPerSecond / 144));
    }
}
