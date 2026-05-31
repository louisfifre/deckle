using Deckle.Vision;
using Xunit;

namespace Deckle.Tests.Vision;

// Tests unit purs sur CaptureStallDetector — la logique de décision du
// watchdog de capture, extraite de la boucle DXGI pour être testable
// sans matériel ni horloge réelle. Le détecteur ne connaît que des ticks
// monotones abstraits et un seuil ; la conversion Stopwatch.Frequency →
// ticks vit côté ScreenCaptureService. Couvre la frontière du seuil,
// l'émission unique en entrée de stall, la reprise sur acquire, le reset
// d'horloge à chaque acquire, et le re-stall après reprise.
[Trait("Category", "unit")]
public class CaptureStallDetectorTests
{
    [Fact]
    public void ObserveStaysHealthyJustBelowThreshold()
    {
        var detector = new CaptureStallDetector(thresholdTicks: 1000, startTicks: 0);

        Assert.Equal(CaptureStallTransition.None, detector.Observe(acquired: false, nowTicks: 999));
    }

    [Fact]
    public void ObserveSignalsStallWhenThresholdCrossedWithoutAcquire()
    {
        var detector = new CaptureStallDetector(thresholdTicks: 1000, startTicks: 0);

        Assert.Equal(CaptureStallTransition.Stalled, detector.Observe(acquired: false, nowTicks: 1000));
    }

    [Fact]
    public void ObserveSignalsStallOnlyOnceWhileStalled()
    {
        var detector = new CaptureStallDetector(thresholdTicks: 1000, startTicks: 0);
        detector.Observe(acquired: false, nowTicks: 1000); // entre en stall

        Assert.Equal(CaptureStallTransition.None, detector.Observe(acquired: false, nowTicks: 5000));
    }

    [Fact]
    public void ObserveSignalsRecoveryOnAcquireAfterStall()
    {
        var detector = new CaptureStallDetector(thresholdTicks: 1000, startTicks: 0);
        detector.Observe(acquired: false, nowTicks: 1000); // entre en stall

        Assert.Equal(CaptureStallTransition.Recovered, detector.Observe(acquired: true, nowTicks: 1500));
    }

    [Fact]
    public void ObserveReturnsNoneOnAcquireWhenNeverStalled()
    {
        var detector = new CaptureStallDetector(thresholdTicks: 1000, startTicks: 0);

        Assert.Equal(CaptureStallTransition.None, detector.Observe(acquired: true, nowTicks: 500));
    }

    [Fact]
    public void ObserveResetsTheClockOnEachAcquire()
    {
        var detector = new CaptureStallDetector(thresholdTicks: 1000, startTicks: 0);
        detector.Observe(acquired: true, nowTicks: 500); // dernier acquire = 500

        // 1400 - 500 = 900 < 1000 → pas de stall, alors que now dépasse le
        // seuil en valeur absolue. Prouve que l'horloge se recale sur le
        // dernier acquire, pas sur le start.
        Assert.Equal(CaptureStallTransition.None, detector.Observe(acquired: false, nowTicks: 1400));
    }

    [Fact]
    public void ObserveReStallsAfterRecovery()
    {
        var detector = new CaptureStallDetector(thresholdTicks: 1000, startTicks: 0);
        detector.Observe(acquired: false, nowTicks: 1000); // stall
        detector.Observe(acquired: true, nowTicks: 1100);  // reprise, horloge = 1100

        // 2100 - 1100 = 1000 >= seuil → re-stall après reprise.
        Assert.Equal(CaptureStallTransition.Stalled, detector.Observe(acquired: false, nowTicks: 2100));
    }
}
