using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Sub-provider transverse — marshalling dispatcher (`DispatcherQueue.
// TryEnqueue`) significatif vers le UI thread. Sans cet event transverse,
// un deadlock UI ou une latence anormale de marshalling se devine au mieux
// par corrélation indirecte avec les jalons métier ; un site qui rate son
// enqueue silencieusement (queue déjà fermée) n'a pas non plus de trace
// systématique. La primitive est strictement non-métier (un wiring de
// plateforme côté `Microsoft.UI.Dispatching`) et consommée par plusieurs
// modules avec exactement le même set de paramètres — promotion en sub-
// provider transverse au sens du critère à deux clauses de la fiche
// `reference--eventsource-convention--1.2.md` §*Sub-providers transverses*.
//
// Hérite aussi de l'event historique `DispatcherEnqueueRejected` migré
// depuis `DeckleShellSource` — l'event ne décrivait pas une opération
// shell, il décrivait un rejet de dispatcher transverse à tout module qui
// marshale vers le UI thread. Sa place naturelle est ici, à côté du
// tronc `MarshalQueued` / `MarshalCompleted`.
//
// Pattern « tronc commun + events spécialisés » (cf. 1.2 §*Convention*).
// `MarshalQueued` + `MarshalCompleted` sont le tronc émis par tout site
// significatif autour d'un `TryEnqueue`. `MarshalTimeout` est l'event
// spécialisé du cas anormal où le callback n'a jamais couru dans un
// délai borné — aucun site câblé activement dans cette passe, déclaré
// pour figer la signature avant que la détection ne soit ajoutée.
// `DispatcherEnqueueRejected` est l'event spécialisé du cas où le
// `TryEnqueue` retourne false (queue shut down) — l'event existait déjà
// en legacy sous `DeckleShellSource`, simplement repositionné ici.
//
// Vocabulaire fermé `operation` (à étendre ici si un nouveau site
// significatif émerge — pas d'opération ad-hoc côté call site) :
//   "ui-update"        — mise à jour d'un contrôle XAML depuis un
//                        thread non-UI
//   "window-show"      — affichage d'une fenêtre depuis un thread non-UI
//   "feedback-display" — affichage HUD ou overlay depuis un thread non-UI
//   "log-append"       — append d'une entrée dans la LogWindow
//   "settings-reload"  — rechargement des settings UI suite à un Changed
//
// Convention `caller` : nom court du site logique
// ("transcription-engine", "ambient-pipeline", "hue-driver", "hud-window",
// "log-window", "settings-window", etc.). Différencie deux marshallings
// du même `operation` sur des sites distincts sans gonfler le schéma.
//
// Convention `queue_depth` : approximation de la profondeur de la queue
// au moment du `TryEnqueue`. `DispatcherQueue` n'expose pas de getter
// public — quand non-mesurable, passer `-1` comme sentinelle "unknown".
//
// Convention `wait_ms` / `run_ms` :
//   - `wait_ms` mesure le temps entre l'appel à `TryEnqueue` (Stopwatch
//     démarré juste avant) et le début d'exécution du callback (Stopwatch
//     lu en début de callback).
//   - `run_ms` mesure le temps d'exécution du callback (Stopwatch
//     redémarré en début de callback et lu en fin de callback).
[EventSource(Name = "Deckle.Diagnostics.Threading")]
public sealed class DeckleThreadingSource : DeckleEventSource
{
    public static readonly DeckleThreadingSource Log = new();

    private DeckleThreadingSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtMarshalQueued              = 1;
    public const int EvtMarshalCompleted           = 2;
    public const int EvtMarshalTimeout             = 3;
    public const int EvtDispatcherEnqueueRejected  = 4;

    // Tronc — émis juste avant le `TryEnqueue` côté site appelant.
    // Verbose parce que le marshalling est par nature fréquent (un Stop
    // user typique enchaîne plusieurs marshals par état engine) et que
    // la grep-abilité passe par les paramètres typés plutôt que par le
    // niveau.
    [Event(EvtMarshalQueued,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "marshal queued | operation={0} | caller={1} | queue_depth={2}")]
    public void MarshalQueued(string operation, string caller, int queue_depth)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalQueued, operation, caller, queue_depth);
    }

    // Tronc — émis en fin d'exécution du callback. `wait_ms` capture la
    // latence de marshalling (temps que le callback a passé en queue),
    // `run_ms` capture le temps d'exécution propre du callback. Une
    // dérive de `wait_ms` signale un UI thread sous charge ; une dérive
    // de `run_ms` signale un callback lourd qui devrait être découpé.
    [Event(EvtMarshalCompleted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "marshal completed | operation={0} | caller={1} | wait_ms={2} | run_ms={3}")]
    public void MarshalCompleted(string operation, string caller, int wait_ms, int run_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalCompleted, operation, caller, wait_ms, run_ms);
    }

    // Spécialisé — cas anormal où le callback n'a jamais couru dans un
    // délai borné (le marshal est resté en queue plus longtemps qu'un
    // seuil applicatif). Warning parce que c'est une anomalie qui mérite
    // une remontée même quand le Verbose n'est pas écouté. Aucun site
    // actif aujourd'hui — déclaré pour figer la signature avant que la
    // détection (timer dédié, watchdog par opération) ne soit câblée.
    [Event(EvtMarshalTimeout,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "marshal timeout | operation={0} | caller={1} | waited_ms={2}")]
    public void MarshalTimeout(string operation, string caller, int waited_ms)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtMarshalTimeout, operation, caller, waited_ms);
    }

    // Spécialisé — `TryEnqueue` a retourné false (queue shut down).
    // Migré depuis `DeckleShellSource.DispatcherEnqueueRejected` (event id
    // 15 en legacy Shell). La signature publique reste identique pour ne
    // pas casser les appelants existants — `caller_source` est le label
    // libre que `DispatcherQueueExtensions.TryEnqueueOrLog` propage
    // (ex. "HUD", "LOGWIN"), `reason` décrit la cause ou le contexte de
    // l'enqueue perdu (ex. "queue-rejected", description courte de
    // l'event qu'on tentait de marshaler).
    [Event(EvtDispatcherEnqueueRejected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Threading,
           Message = "dispatcher enqueue rejected | caller_source={0} | reason={1}")]
    public void DispatcherEnqueueRejected(string caller_source, string reason)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Threading)) return;
        WriteEvent(EvtDispatcherEnqueueRejected, caller_source, reason);
    }
}
