using System.Diagnostics.Tracing;
using Deckle.Chrono;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Chrono.Tests;

// DeckleChronoSource is a process-wide singleton. Each test subscribes its own
// TestEventListener by the ETW name "Deckle-Chrono" and disposes it, so tests
// do not share captured events.
[Trait("Category", "observability")]
public class DeckleChronoSourceTests
{
    [Fact]
    public void PilotEmittedProducesOneInformationalEventOnTheChronoProvider()
    {
        using var listener = new TestEventListener("Deckle-Chrono");

        DeckleChronoSource.Log.PilotEmitted();

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleChronoSource.EvtPilotEmitted, ev.EventId);
        Assert.Equal(EventLevel.Informational, ev.Level);
    }

    [Fact]
    public void PilotEmittedDetailCarriesTheNoteAsFirstPayload()
    {
        using var listener = new TestEventListener("Deckle-Chrono");

        DeckleChronoSource.Log.PilotEmittedDetail("payload-content");

        var ev = Assert.Single(listener.Events);
        var note = Assert.IsType<string>(ev.Payload?[0]);
        Assert.Equal("payload-content", note);
    }
}
