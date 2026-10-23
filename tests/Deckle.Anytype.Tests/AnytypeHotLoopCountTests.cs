using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Anytype.Tests;

// API requests and health probes are counted for the 60 s rollup, and the
// health probe is the one the stall watch reads: its count has to exist even
// when no listener asks for the event.
// The class joins a serialized collection: the events it emits belong to the
// process-wide Deckle-Anytype provider, which listeners of parallel tests hear.
[Collection(AnytypeHotLoopCollection.Name)]
[Trait("Category", "observability")]
public sealed class AnytypeHotLoopCountTests
{
    [Fact]
    public void AnApiRequestIsCountedWithoutAListener()
    {
        long before = HotLoops.AnytypeApiRequest.Total;

        DeckleAnytypeSource.Log.ApiRequestStarted("GET", "/v1/spaces");

        Assert.True(HotLoops.AnytypeApiRequest.Total >= before + 1);
    }

    [Fact]
    public void AHealthProbeIsCountedWithoutAListener()
    {
        long before = HotLoops.AnytypeHealthProbe.Total;

        DeckleAnytypeSource.Log.BackendHealthProbed(true, 200, "ok", 3.5);

        Assert.True(HotLoops.AnytypeHealthProbe.Total >= before + 1);
    }
}