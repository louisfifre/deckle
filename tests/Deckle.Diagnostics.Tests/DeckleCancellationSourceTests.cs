using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Cross-cutting sub-provider: application cancellations. The event is on the
// Lifecycle keyword (not a dedicated Cancellation keyword: the "cancellation"
// nature is carried by the provider name itself). The test freezes the keyword
// so any future migration to a dedicated keyword is deliberate and traced.
[Trait("Category", "observability")]
public class DeckleCancellationSourceTests
{
    [Fact]
    public void OperationCancelledEmitsVerboseOnLifecycleKeyword()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Cancellation");

        DeckleCancellationSource.Log.OperationCancelled(
            operation: "whisp-transcribe", reason: "user", age_ms: 1500);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleCancellationSource.EvtOperationCancelled, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Lifecycle));
        Assert.Equal("whisp-transcribe", ev.Payload?[0]);
        Assert.Equal("user", ev.Payload?[1]);
        Assert.Equal(1500, ev.Payload?[2]);
    }

    [Fact]
    public void OperationCancelledAcceptsUnknownAgeSentinel()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Cancellation");

        DeckleCancellationSource.Log.OperationCancelled(
            operation: "llm-warmup", reason: "shutdown", age_ms: -1);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(-1, ev.Payload?[2]);
    }
}
