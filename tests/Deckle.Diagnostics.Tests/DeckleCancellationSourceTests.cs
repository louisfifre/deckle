using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Sub-provider transverse — annulations applicatives. L'event est sur le
// keyword Lifecycle (pas un keyword Cancellation dédié — la nature
// "cancellation" est portée par le nom du provider lui-même). Le test
// fige le keyword pour que toute migration future vers un keyword dédié
// soit délibérée et tracée.
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
