using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// The rollup, the stall and the recovery of a hot loop. The clock is a parameter
// of Evaluate, so a minute passes without waiting.
[Trait("Category", "observability")]
public sealed class LoopPulseTests
{
    private const long Minute = 60_000;

    private static long Start => Environment.TickCount64;

    private static List<EventEntryView> Of(TestEventListener listener, string loop)
        => listener.Events
            .Where(e => e.Payload is { Count: > 0 } && (string?)e.Payload[0] == loop)
            .Select(e => new EventEntryView(e.EventId, e.Level, (e.Tags & ObservationTags.Journal) != 0, e.Payload!))
            .ToList();

    private sealed record EventEntryView(int Id, EventLevel Level, bool Journal, IReadOnlyList<object?> Payload);

    [Fact]
    public void AWindowWithTicksWritesOneRollupThatStaysOutOfTheJournal()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_rollup", watchesStall: false);
        long t = Start;

        for (int i = 0; i < 5; i++) pulse.Tick();
        pulse.Evaluate(t + Minute);

        var ev = Assert.Single(Of(listener, "test_rollup"));
        Assert.Equal(DeckleHotLoopSource.EvtLoopRollup, ev.Id);
        Assert.Equal(EventLevel.Informational, ev.Level);
        Assert.False(ev.Journal);
        Assert.Equal(5L, ev.Payload[1]);
    }

    [Fact]
    public void AQuietWindowWritesNothing()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_quiet", watchesStall: false);
        long t = Start;

        pulse.Evaluate(t + Minute);
        pulse.Evaluate(t + 2 * Minute);

        Assert.Empty(Of(listener, "test_quiet"));
    }

    [Fact]
    public void ASilentLoopThatShouldRunOpensOneWarningAfterTwoWindows()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_stall", watchesStall: true);
        using IDisposable armed = pulse.Arm();
        long t = Start;

        pulse.Evaluate(t + Minute);
        Assert.Empty(Of(listener, "test_stall"));

        pulse.Evaluate(t + 2 * Minute);
        pulse.Evaluate(t + 3 * Minute);

        var ev = Assert.Single(Of(listener, "test_stall"));
        Assert.Equal(DeckleHotLoopSource.EvtLoopStalled, ev.Id);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.True(ev.Journal);
    }

    [Fact]
    public void ALoopThatTicksAgainClosesTheStallInTheJournal()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_resume", watchesStall: true);
        using IDisposable armed = pulse.Arm();
        long t = Start;
        pulse.Evaluate(t + Minute);
        pulse.Evaluate(t + 2 * Minute);

        pulse.Tick();
        pulse.Evaluate(t + 3 * Minute);

        var events = Of(listener, "test_resume");
        Assert.Equal(
            [DeckleHotLoopSource.EvtLoopStalled, DeckleHotLoopSource.EvtLoopRecovered, DeckleHotLoopSource.EvtLoopRollup],
            events.Select(e => e.Id));
        Assert.True(events[1].Journal);
        Assert.Equal("resumed", events[1].Payload[1]);
    }

    [Fact]
    public void ALoopThatIsNoLongerExpectedClosesTheStallAsStopped()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_stopped", watchesStall: true);
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
    public void AnEventDrivenLoopNeverStalls()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_event_driven", watchesStall: false);
        using IDisposable armed = pulse.Arm();
        long t = Start;

        pulse.Evaluate(t + Minute);
        pulse.Evaluate(t + 2 * Minute);
        pulse.Evaluate(t + 3 * Minute);

        Assert.Empty(Of(listener, "test_event_driven"));
    }

    [Fact]
    public void ADisposedArmingScopeIsCountedOnce()
    {
        using var listener = new TestEventListener("Deckle-HotLoop");
        var pulse = new LoopPulse("test_double_dispose", watchesStall: true);
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