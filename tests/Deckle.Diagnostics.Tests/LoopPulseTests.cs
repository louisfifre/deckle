using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// The stall and the recovery of a hot loop. The clock is a parameter
// of Evaluate, so a minute passes without waiting.
[Trait("Category", "observability")]
public sealed class LoopPulseTests
{
    private const long Minute = 60_000;

    private static long Start => Environment.TickCount64;

    private static List<EventEntryView> Of(TestEventListener listener, string loop)
        => listener.Events
            .Where(e => e.Payload is { Count: > 0 } && (string?)e.Payload[0] == loop)
            .Select(e => new EventEntryView(e.EventId, e.Level, (e.Tags & ObservationTags.Milestone) != 0, e.Payload!))
            .ToList();

    private sealed record EventEntryView(int Id, EventLevel Level, bool Milestone, IReadOnlyList<object?> Payload);

    [Fact]
    public void ASilentLoopThatShouldRunOpensOneWarningAfterTwoWindows()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_stall");
        using IDisposable armed = pulse.Arm();
        long t = Start;

        pulse.Evaluate(t + Minute);
        Assert.Empty(Of(listener, "test_stall"));

        pulse.Evaluate(t + 2 * Minute);
        pulse.Evaluate(t + 3 * Minute);

        var ev = Assert.Single(Of(listener, "test_stall"));
        Assert.Equal(DeckleHotLoopSource.EvtLoopStalled, ev.Id);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.True(ev.Milestone);
    }

    [Fact]
    public void ALoopThatTicksAgainClosesTheStallInTheLog()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_resume");
        using IDisposable armed = pulse.Arm();
        long t = Start;
        pulse.Evaluate(t + Minute);
        pulse.Evaluate(t + 2 * Minute);

        pulse.Tick();
        pulse.Evaluate(t + 3 * Minute);

        var events = Of(listener, "test_resume");
        Assert.Equal(
            [DeckleHotLoopSource.EvtLoopStalled, DeckleHotLoopSource.EvtLoopRecovered],
            events.Select(e => e.Id));
        Assert.True(events[1].Milestone);
        Assert.Equal("resumed", events[1].Payload[1]);
    }

    [Fact]
    public void ALoopThatIsNoLongerExpectedClosesTheStallAsStopped()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_stopped");
        IDisposable armed = pulse.Arm();
        long t = Start;
        pulse.Evaluate(t + Minute);
        pulse.Evaluate(t + 2 * Minute);

        armed.Dispose();
        pulse.Evaluate(t + 3 * Minute);
        pulse.Evaluate(t + 4 * Minute);

        var events = Of(listener, "test_stopped");
        Assert.Equal(
            [DeckleHotLoopSource.EvtLoopStalled, DeckleHotLoopSource.EvtLoopRecovered],
            events.Select(e => e.Id));
        Assert.Equal("stopped", events[1].Payload[1]);
    }

    [Fact]
    public void ALoopNobodyArmedNeverStalls()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_unarmed");
        long t = Start;

        pulse.Evaluate(t + Minute);
        pulse.Evaluate(t + 2 * Minute);
        pulse.Evaluate(t + 3 * Minute);

        Assert.Empty(Of(listener, "test_unarmed"));
    }

    // A loop that ticks writes nothing: only a stall and its recovery are lines.
    [Fact]
    public void AWorkingLoopWritesNothing()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_working");
        using IDisposable armed = pulse.Arm();
        long t = Start;

        for (int window = 1; window <= 3; window++)
        {
            pulse.Tick();
            pulse.Evaluate(t + window * Minute);
        }

        Assert.Empty(Of(listener, "test_working"));
    }

    [Fact]
    public void ADisposedArmingScopeIsCountedOnce()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_double_dispose");
        using IDisposable outer = pulse.Arm();
        IDisposable inner = pulse.Arm();
        inner.Dispose();
        inner.Dispose();
        long t = Start;

        pulse.Evaluate(t + Minute);
        pulse.Evaluate(t + 2 * Minute);

        Assert.Single(Of(listener, "test_double_dispose"));
    }
}