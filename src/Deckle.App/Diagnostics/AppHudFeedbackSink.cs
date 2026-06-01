using Deckle.Diagnostics;

namespace Deckle.App.Diagnostics;

// Sink concret hôte qui reçoit chaque `FeedbackEntry` capturée par le
// `HudFeedbackEventListener` (un `UserFeedbackEmitted` émis par un provider
// `Deckle.*`) et la route vers la bonne surface HUD selon `Role` :
//
//   role 0 (Replacement) → surface principale `HudWindow.ShowUserFeedback`
//                          (le chrono est swappé out le temps du message).
//   role 1 (Overlay)     → carte stackée via `HudOverlayManager.Enqueue`
//                          (au-dessus / en-dessous du HUD principal sans
//                          interrompre le workflow en cours).
//
// La mécanique de marshalling vers le thread UI est portée par chaque
// méthode cible (`HudWindow.ShowUserFeedback` / `HudOverlayManager.Enqueue`
// font leur propre `EnqueueUI` / `_dispatcher.TryEnqueueOrLog`). Le sink
// est appelé sur le thread de l'EventListener — qui peut être n'importe
// quel thread métier, EventSource ne sérialise pas.
//
// Le sink consomme les champs primitifs de `FeedbackEntry` directement ;
// aucune dépendance au module HUD ne remonte dans Deckle.Diagnostics.
internal sealed class AppHudFeedbackSink : IHudFeedbackSink
{
    private readonly System.Action<int, string, string> _onReplacement;
    private readonly System.Action<int, string, string> _onOverlay;

    public AppHudFeedbackSink(
        System.Action<int, string, string> onReplacement,
        System.Action<int, string, string> onOverlay)
    {
        _onReplacement = onReplacement;
        _onOverlay = onOverlay;
    }

    public void Write(FeedbackEntry entry)
    {
        switch (entry.Role)
        {
            case 1: _onOverlay(entry.Severity, entry.Title, entry.Body); break;
            default: _onReplacement(entry.Severity, entry.Title, entry.Body); break;
        }
    }
}
