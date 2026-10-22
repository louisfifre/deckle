using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Pins the Lifecycle keyword: moving cancellations to a dedicated keyword must
// be a deliberate change. A cancellation is a normal end, so it is Informational
// and travels in the journal, never a Warning.
[Trait("Category", "observability")]
public class DeckleCancellationSourceTests
{
    [Fact]
    public void OperationCancelledIsAJournalInformationalOnLifecycleKeyword()
    {
        using var listener = new TestEventListener("Deckle-Cancellation");

        DeckleCancellationSource.Log.OperationCancelled(
            operation: "whisp-transcribe", reason: "user", age_ms: 1500);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleCancellationSource.EvtOperationCancelled, ev.EventId);
        Assert.Equal(EventLevel.Informational, ev.Level);
        Assert.Equal(ObservationTags.Journal, ev.Tags & ObservationTags.Journal);
        Assert.True(ev.HasKeyword(Keywords.Lifecycle));
        Assert.Equal("whisp-transcribe", ev.Payload?[0]);
        Assert.Equal("user", ev.Payload?[1]);
        Assert.Equal(1500, ev.Payload?[2]);
    }

    [Fact]
    public void OperationCancelledAcceptsUnknownAgeSentinel()
    {
        using var listener = new TestEventListener("Deckle-Cancellation");

        DeckleCancellationSource.Log.OperationCancelled(
            operation: "llm-warmup", reason: "shutdown", age_ms: -1);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(-1, ev.Payload?[2]);
    }
}
