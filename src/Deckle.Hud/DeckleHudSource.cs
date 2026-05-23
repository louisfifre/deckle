using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Hud;

// Provider EventSource du module Deckle.Hud.
//
// Issu de la résolution du conflit obs ↔ carto : obs avait centralisé les
// observations HUD dans DeckleAppSource (côté host Deckle), mais carto a
// extrait Deckle.Hud en module séparé et un module ne peut pas dépendre
// du host. La doctrine modulaire (un provider par composant cohérent)
// commande qu'un module qui émet possède son provider — c'est ce que
// fait ce fichier, en suivant le pattern des autres providers Deckle.*.
//
// Pour l'instant ce provider porte uniquement le timeout warning du
// HideSync rendezvous (cas pathologique sur paste avec UI thread bloqué).
// Si plus d'événements Hud-internes émergent, ils s'ajoutent ici.
[EventSource(Name = "Deckle.Hud")]
public sealed class DeckleHudSource : DeckleEventSource
{
    public static readonly DeckleHudSource Log = new();

    public const int EvtHudWarning = 1;

    [Event(EvtHudWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void HudWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHudWarning, message);
    }
}
