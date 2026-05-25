using System.Diagnostics.Tracing;
using Deckle.Chrono;
using Deckle.Tests.Shared;
using Xunit;

namespace Deckle.Tests.Chrono;

// Test d'observabilité — exerce la chaîne EventSource depuis l'émission
// jusqu'à la collecte par EventListener. Sert aussi de premier exemple
// du pattern TestEventListener qui sera réutilisé pour tous les futurs
// providers Deckle.* (Audio, Vision, Whisp, etc.).
//
// Le provider DeckleChronoSource est un singleton process-wide. Le
// listener s'abonne via son nom ETW "Deckle.Chrono" — il ne dépend pas
// d'une instance, ce qui rend les tests isolés naturellement (chaque
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
