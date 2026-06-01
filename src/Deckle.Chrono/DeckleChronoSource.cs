using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Chrono;

// EventSource pilote pour exercer toute la plomberie en amont :
// SessionId partagé, EventListener qui découvre dynamiquement le
// provider, sérialisation JSONL, alimentation LogWindow.
//
// Les events portent ici une simple sémantique de jalon — pas de
// payload structuré, pas de heartbeat agrégé. Quand un site d'appel
// applicatif voudra réellement instrumenter le chrono (par exemple
// au moment où la vague de surface refondra la HUD), on étendra
// avec des [Event] supplémentaires en respectant la doctrine du
// brief — un [Event] par opération distincte.
[EventSource(Name = "Deckle.Chrono")]
public sealed class DeckleChronoSource : DeckleEventSource
{
    public static readonly DeckleChronoSource Log = new();

    private DeckleChronoSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // Numérotation séquentielle à partir de 1. Les ids sont publics
    // dans le manifest ETW — ne pas réutiliser un id après suppression
    // d'un event (un consumer historique pourrait encore en avoir
    // besoin pour décoder un dump). Pour Deckle on commence frais à
    // la vague 1, donc pas d'historique à respecter.
    public const int EvtPilotEmitted = 1;

    // Boot validation event. Émis une fois au démarrage par l'App pour
    // exercer EventSource → JsonlEventListener → app.jsonl et
    // EventSource → LogWindowEventListener → LogWindow.
    [Event(EvtPilotEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Chrono pilot emission ({0})")]
    public void PilotEmitted(string note)
    {
        if (IsEnabled()) WriteEvent(EvtPilotEmitted, note);
    }
}
