using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "observability")]
public class DeckleThreadingSourceTests
{
    [Fact]
    public void MarshalQueuedEmitsVerboseOnThreadingKeyword()
    {
        using var listener = new TestEventListener("Deckle-Threading");

        DeckleThreadingSource.Log.MarshalQueued(
            operation: "log-append", caller: "log-window", queue_depth: -1);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleThreadingSource.EvtMarshalQueued, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Threading));
        Assert.Equal("log-append", ev.Payload?[0]);
        Assert.Equal("log-window", ev.Payload?[1]);
        Assert.Equal(-1, ev.Payload?[2]);
    }

    [Fact]
    public void MarshalCompletedCarriesWaitMsAndRunMsInOrder()
    {
        using var listener = new TestEventListener("Deckle-Threading");

        DeckleThreadingSource.Log.MarshalCompleted(
            operation: "ui-update", caller: "hud-window", wait_ms: 3, run_ms: 12);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleThreadingSource.EvtMarshalCompleted, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.Equal(3, ev.Payload?[2]);
        Assert.Equal(12, ev.Payload?[3]);
    }

    // A blocked UI thread makes every queued callback late at once: the
    // log must show one stall, not one Warning per victim callback.
    [Fact]
    public void MarshalStallWatchReportsOneIncidentPerStall()
    {
        using var listener = new TestEventListener("Deckle-Threading");
        var watch = new MarshalStallWatch();
        int late = MarshalStallWatch.StallThresholdMs;

        watch.Observe("ui-update", "hud-window", wait_ms: 5, nowMs: 0);
        watch.Observe("feedback-display", "hud-window", wait_ms: late + 400, nowMs: 1_000);
        watch.Observe("log-append", "log-window", wait_ms: late + 900, nowMs: 1_010);
        watch.Observe("ui-update", "hud-window", wait_ms: late, nowMs: 1_020);
        watch.Observe("ui-update", "hud-window", wait_ms: 2, nowMs: 1_500);
        watch.Observe("ui-update", "hud-window", wait_ms: 2, nowMs: 1_600);

        Assert.Equal(4, listener.Events.Count);
        var opened = listener.Events[0];
        Assert.Equal(DeckleThreadingSource.EvtMarshalStalled, opened.EventId);
        Assert.Equal(EventLevel.Warning, opened.Level);
        Assert.Equal("feedback-display", opened.Payload?[0]);
        Assert.Equal(late + 400, opened.Payload?[2]);
        Assert.Equal(DeckleThreadingSource.EvtMarshalStallRepeated, listener.Events[1].EventId);
        Assert.Equal(DeckleThreadingSource.EvtMarshalStallRepeated, listener.Events[2].EventId);
        var recovered = listener.Events[3];
        Assert.Equal(DeckleThreadingSource.EvtMarshalStallRecovered, recovered.EventId);
        Assert.Equal(EventLevel.Informational, recovered.Level);
        Assert.Equal(3, recovered.Payload?[0]);
        Assert.Equal(late + 900, recovered.Payload?[1]);
        Assert.Equal(500L, recovered.Payload?[2]);
    }

    [Fact]
    public void DispatcherEnqueueRejectedEmitsWarningThenVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Threading");

        DeckleThreadingSource.Log.DispatcherEnqueueRejected();
        DeckleThreadingSource.Log.DispatcherEnqueueRejectedDetail(
            caller_source: "LOGWIN", reason: "log entry");

        Assert.Equal(2, listener.Events.Count);
        var logged = listener.Events[0];
        Assert.Equal(DeckleThreadingSource.EvtDispatcherEnqueueRejected, logged.EventId);
        Assert.Equal(EventLevel.Warning, logged.Level);
        Assert.True(logged.HasKeyword(Keywords.Threading));
        var detail = listener.Events[1];
        Assert.Equal(DeckleThreadingSource.EvtDispatcherEnqueueRejectedDetail, detail.EventId);
        Assert.Equal(EventLevel.Verbose, detail.Level);
        Assert.Equal("LOGWIN", detail.Payload?[0]);
        Assert.Equal("log entry", detail.Payload?[1]);
    }
}
