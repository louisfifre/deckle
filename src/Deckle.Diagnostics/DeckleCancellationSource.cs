using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Sub-provider transverse — annulations applicatives explicites.
// Capter les OperationCanceledException sur les sites où elles sont
// sémantiquement intéressantes (moteur Whisper, capture Vision, rewrite
// Llm, polling Ollama) permet de répondre à la question « pourquoi
// l'opération s'est arrêtée » sans deviner — utilisateur, timeout,
// shutdown, hotkey, restart engine, propagation amont. Sans cet event
// transverse, un OCE silencieusement absorbé dans un catch ne laisse
// aucune trace, et une cascade d'annulations en chaîne est impossible
// à reconstituer après coup. La primitive est strictement non-métier
// et consommée par plusieurs modules avec le même set de paramètres —
// promotion en sub-provider transverse au sens du critère à deux
// clauses de la fiche `reference--eventsource-convention--1.2.md`
// §*Sub-providers transverses*.
//
// Vocabulaire fermé `operation` (à étendre ici si un nouveau site
// d'annulation émerge — pas d'opération ad-hoc côté call site) :
//   "whisp-transcribe" — annulation pendant la transcription (warmup,
//                        VAD, whisper_run)
//   "whisp-record"     — annulation pendant la capture audio
//   "vision-capture"   — annulation de la boucle screen capture
//   "vision-sample"    — annulation du frame sampler
//   "llm-rewrite"      — annulation du rewrite Ollama (timeout user,
//                        restart engine)
//   "llm-warmup"       — annulation pendant le polling /api/ps en
//                        attente prolongée
//   "llm-models"       — annulation pendant une opération admin sur la
//                        liste de modèles (delete, refresh)
//   "ambient-pipeline" — annulation de la boucle de push ambient
//                        (Stop user, capture lost upstream, external
//                        Hue interference, DisposeAsync shutdown)
//
// Vocabulaire fermé `reason` :
//   "user"           — l'utilisateur a déclenché l'annulation (Hide,
//                      hotkey, stop button, close de surface)
//   "timeout"        — un délai a expiré (REWRITE_HARD_CAP, etc.)
//   "shutdown"       — l'app se ferme
//   "hotkey"         — l'utilisateur a appuyé sur la hotkey de
//                      hold-to-talk pour stopper
//   "engine-restart" — l'engine est en cours de restart, annule les
//                      opérations en vol
//   "upstream"       — l'opération a été annulée parce qu'une étape
//                      précédente l'a demandée (propagation de ct)
//
// `age_ms` est calculé via Stopwatch.ElapsedMilliseconds entre le
// démarrage de l'opération et la levée de l'OCE quand un Stopwatch
// local est disponible au site d'appel. Quand l'age n'est pas
// mesurable (pas de Stopwatch en scope, opération sans ancre
// temporelle), passer -1 — c'est explicite et grep-able.
[EventSource(Name = "Deckle.Diagnostics.Cancellation")]
public sealed class DeckleCancellationSource : DeckleEventSource
{
    public static readonly DeckleCancellationSource Log = new();

    private DeckleCancellationSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtOperationCancelled = 1;

    // Émis sur tout site qui catche un OperationCanceledException (ou
    // TaskCanceledException qui en hérite) sur une opération applicative
    // dont l'annulation porte une intention. Verbose parce que les
    // annulations sont par nature fréquentes (un Stop user typique va
    // déclencher plusieurs OCE en cascade sur les threads worker) et
    // que la grep-abilité passe par les paramètres typés plutôt que par
    // le niveau. Keyword `Lifecycle` faute de keyword `Cancellation`
    // dédié — la nature « cancellation » est portée par le provider
    // lui-même (Deckle.Diagnostics.Cancellation), pas par un bit
    // keyword supplémentaire.
    [Event(EvtOperationCancelled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "operation cancelled | operation={0} | reason={1} | age_ms={2}")]
    public void OperationCancelled(string operation, string reason, int age_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtOperationCancelled, operation, reason, age_ms);
    }
}
