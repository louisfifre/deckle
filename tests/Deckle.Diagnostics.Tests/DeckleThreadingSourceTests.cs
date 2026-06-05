using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Cross-cutting sub-provider: dispatcher marshalling. Four accepted events:
// MarshalQueued, MarshalCompleted, MarshalTimeout, DispatcherEnqueueRejected.
// The first two and the timeout are wired to active sites; the last one
// (DispatcherEnqueueRejected) migrated from DeckleShellSource, and its contract
// must stay frozen to avoid breaking existing callers.
// (cf. commentaires DeckleThreadingSource).
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

    [Fact]
    public void MarshalTimeoutEmitsWarningOnThreadingKeyword()
    {
        using var listener = new TestEventListener("Deckle-Threading");

        DeckleThreadingSource.Log.MarshalTimeout(
            operation: "feedback-display", caller: "hud-window", waited_ms: 5000);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleThreadingSource.EvtMarshalTimeout, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Threading));
    }

    [Fact]
    public void DispatcherEnqueueRejectedKeepsLegacyShellSignature()
    {
        using var listener = new TestEventListener("Deckle-Threading");

        DeckleThreadingSource.Log.DispatcherEnqueueRejected(
            caller_source: "LOGWIN", reason: "log entry");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleThreadingSource.EvtDispatcherEnqueueRejected, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Threading));
        Assert.Equal("LOGWIN", ev.Payload?[0]);
        Assert.Equal("log entry", ev.Payload?[1]);
    }
}
