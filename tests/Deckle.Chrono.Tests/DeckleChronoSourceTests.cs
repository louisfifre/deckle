using System.Diagnostics.Tracing;
using Deckle.Chrono;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Chrono.Tests;

// Observability test: exercises the EventSource chain from emission to
// collection by EventListener. Also serves as the first example of the
// TestEventListener pattern that will be reused for all future
// providers Deckle.* (Audio, Vision, Whisp, etc.).
//
// Le provider DeckleChronoSource est un singleton process-wide. Le
// listener subscribes through its ETW name "Deckle.Chrono"; it does not depend
// on an instance, which makes tests naturally isolated (each
// test instancie son propre listener via using).
[Trait("Category", "observability")]
public class DeckleChronoSourceTests
{
    [Fact]
    public void PilotEmittedProducesOneInformationalEventOnTheChronoProvider()
    {
        using var listener = new TestEventListener("Deckle.Chrono");

        DeckleChronoSource.Log.PilotEmitted("hello-test");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleChronoSource.EvtPilotEmitted, ev.EventId);
        Assert.Equal(EventLevel.Informational, ev.Level);
    }

    [Fact]
    public void PilotEmittedCarriesTheNoteAsFirstPayload()
    {
        using var listener = new TestEventListener("Deckle.Chrono");

        DeckleChronoSource.Log.PilotEmitted("payload-content");

        var ev = Assert.Single(listener.Events);
        var note = Assert.IsType<string>(ev.Payload?[0]);
        Assert.Equal("payload-content", note);
    }
}
