using Xunit;

namespace Deckle.Diagnostics.Tests;

// A marshal onto the UI thread is counted for the 60 s rollup, listener or not.
// The class joins a serialized collection: MarshalQueued is an event of the
// process-wide Deckle-Threading provider, and a listener of a parallel test that
// asserts a single event would hear this one.
[Collection(DiagnosticsProviderCollection.Name)]
[Trait("Category", "observability")]
public sealed class ThreadingHotLoopCountTests
{
    [Fact]
    public void AQueuedMarshalIsCountedWithoutAListener()
    {
        long before = HotLoops.ThreadingMarshal.Total;

        DeckleThreadingSource.Log.MarshalQueued("test", "caller", 1);

        Assert.True(HotLoops.ThreadingMarshal.Total >= before + 1);
    }
}