using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Deckle.Shell;

// ─── DispatcherQueueExtensions ─────────────────────────────────────────────
//
// Wrappers autour de `DispatcherQueue.TryEnqueue` consommés par tous les
// sites de Deckle qui marshalent un callback vers le UI thread.
//
// `TryEnqueueOrLog` est le wrapper historique : émet un Warning
// `DispatcherEnqueueRejected` quand l'enqueue échoue (queue shut down).
// Sans ça, l'event UI est perdu en silence — typique au teardown de
// window où StatusChanged engine arrive alors que la dispatcher queue
// est déjà fermée. Le Warning sort sur `DeckleThreadingSource` (sub-
// provider transverse `Deckle.Diagnostics.Threading`) depuis la vague
// d'instrumentation transverse — l'event ne décrivait pas une opération
// shell, il décrivait un rejet de dispatcher transverse à tout module
// qui marshale vers le UI thread. La signature publique côté appelants
// reste identique (`source` et `what`) pour ne pas casser les sites
// existants.
//
// `TryEnqueueObserved` est le wrapper instrumenté de la vague transverse :
// émet en plus du rejet la paire `MarshalQueued` (avant le `TryEnqueue`)
// et `MarshalCompleted` (en fin de callback) avec les mesures `wait_ms`
// (latence de marshalling) et `run_ms` (durée d'exécution du callback).
// Gate strict `IsEnabled(Verbose, Threading)` en tête : quand aucun
// listener n'écoute, l'instrumentation a un coût net nul (un test ETW
// + un retour) — le wrapper retombe sur le comportement de
// `TryEnqueueOrLog`. `MarshalTimeout` reste déclaré côté provider mais
// non câblé activement dans cette passe — son contrat est figé pour une
// passe ultérieure qui détectera les callbacks restés trop longtemps en
// queue via un watchdog dédié.
//
// Garde anti-récursion `_logging` (warning path). Si LogWindow appelle
// l'un de ces wrappers et que sa propre queue est fermée, le Warning
// loggé route à nouveau vers LogWindow → re-TryEnqueue → re-fail →
// boucle. Un flag thread-static court-circuite la deuxième tentative.
// La garde reste pertinente après la migration EventSource :
// `LogWindowEventListener` reçoit toujours l'event Warning et le
// repousse dans la même `DispatcherQueue` côté LogWindow.
//
// Garde anti-récursion `_emittingMarshal` (verbose path). Même classe
// de boucle, déclenchée par l'émission *systématique* de `MarshalQueued`
// dans `TryEnqueueObserved` : un appel à `LogWindow.Write` depuis
// `LogWindowEventListener.OnEventWritten` (sur worker thread) traverse
// `TryEnqueueObserved` qui émet `MarshalQueued` synchronement, ce que
// le listener observe et re-route vers `LogWindow.Write` → nouvelle
// émission → récursion synchrone → stack overflow. Constaté empiriquement
// 2026-05-25, signature : tail JSONL inondé de `MarshalQueued
// operation=log-append caller=log-window` à plusieurs kHz puis crash.
// Quand la réentrance est détectée, on retombe sur le path froid
// `TryEnqueueOrLog` qui enqueue le callback sans émission — l'event
// utile (celui qui a déclenché la chaîne) atterrit bien dans la queue
// UI, seule l'observation du marshalling imbriqué est skippée.
//
// Pourquoi pas un simple `if (!queue.TryEnqueue(...)) _log.Warning(...)`
// inline à chaque site ? Centraliser réduit la duplication (8 sites
// rejet + 5 sites observed) et garantit que le pattern de garde anti-
// récursion est partout, sans risque d'oubli.

public static class DispatcherQueueExtensions
{
    [System.ThreadStatic]
    private static bool _logging;

    [System.ThreadStatic]
    private static bool _emittingMarshal;

    /// <summary>
    /// Enqueue le callback sur la dispatcher queue. Si l'enqueue échoue
    /// (queue fermée), émet un Warning sur DeckleThreadingSource avec la
    /// source caller et la description fournie, puis retourne false.
    /// </summary>
    /// <param name="queue">La dispatcher queue cible.</param>
    /// <param name="callback">Le delegate à exécuter sur le UI thread.</param>
    /// <param name="source">Identifiant libre de l'émetteur (ex. "HUD", "LOGWIN"). Passé en champ payload de l'event.</param>
    /// <param name="what">Description courte de l'event perdu (ex. "log entry", "recording state").</param>
    /// <param name="priority">Priority d'ordonnancement de la dispatcher queue. Défaut Normal. Passer Low pour différer le callback après le batch de layout courant (pattern de coordination utilisé par les Settings pages pour clearer `_initializing` après hydratation des contrôles).</param>
    public static bool TryEnqueueOrLog(
        this DispatcherQueue queue,
        DispatcherQueueHandler callback,
        string source,
        string what,
        DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
    {
        bool ok = queue.TryEnqueue(priority, callback);
        if (!ok && !_logging)
        {
            _logging = true;
            try
            {
                DeckleThreadingSource.Log.DispatcherEnqueueRejected(source, what);
            }
            finally { _logging = false; }
        }
        return ok;
    }

    /// <summary>
    /// Enqueue le callback sur la dispatcher queue en l'instrumentant
    /// pour le sub-provider transverse Threading. Émet `MarshalQueued`
    /// avant l'enqueue, mesure `wait_ms` (latence) et `run_ms` (exécution)
    /// autour du callback et émet `MarshalCompleted` en fin. Si l'enqueue
    /// échoue, émet `DispatcherEnqueueRejected` exactement comme
    /// `TryEnqueueOrLog`. Gate strict `IsEnabled(Verbose, Threading)` :
    /// quand aucun listener n'écoute, retombe sur `TryEnqueueOrLog`
    /// (zéro alloc supplémentaire pour l'instrumentation).
    /// </summary>
    /// <param name="queue">La dispatcher queue cible.</param>
    /// <param name="operation">Nom court de l'opération marshalée (vocabulaire fermé documenté sur DeckleThreadingSource).</param>
    /// <param name="caller">Nom court du site logique (ex. "log-window", "hud-window", "overlay-manager").</param>
    /// <param name="callback">Le delegate à exécuter sur le UI thread.</param>
    /// <param name="rejectSource">Identifiant libre passé à DispatcherEnqueueRejected si l'enqueue échoue (ex. "HUD", "LOGWIN").</param>
    /// <param name="rejectWhat">Description courte de l'event perdu (ex. "log entry", "overlay enqueue").</param>
    /// <param name="priority">Priority d'ordonnancement de la dispatcher queue, propagée au TryEnqueue sous-jacent. Défaut Normal. Passer Low pour différer le callback après le batch de layout courant — pattern de coordination utilisé par les Settings pages pour clearer `_initializing` après hydratation des contrôles et par HudWindow pour le warm pass différé d'une frame.</param>
    public static bool TryEnqueueObserved(
        this DispatcherQueue queue,
        string operation,
        string caller,
        DispatcherQueueHandler callback,
        string rejectSource,
        string rejectWhat,
        DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
    {
        bool verboseEnabled = DeckleThreadingSource.Log.IsEnabled(
            EventLevel.Verbose, (EventKeywords)Keywords.Threading);

        // Path froid — pas d'instrumentation Queued/Completed, juste
        // l'enqueue brut + la voie de rejet historique. La gate
        // Warning sur DispatcherEnqueueRejected reste ouverte
        // indépendamment du Verbose. Path emprunté aussi en réentrance
        // (cf. note `_emittingMarshal` en tête de fichier) : on enqueue
        // sans émission pour ne pas re-déclencher la chaîne synchrone
        // listener → Write → TryEnqueueObserved.
        if (!verboseEnabled || _emittingMarshal)
        {
            return queue.TryEnqueueOrLog(callback, rejectSource, rejectWhat, priority);
        }

        // Path chaud — instrumentation complète. Stopwatch capturé en
        // closure pour mesurer wait_ms (queue → début callback) et
        // run_ms (durée callback). Gardé par `_emittingMarshal` thread-
        // static : toute émission MarshalQueued/Completed qui ré-entre
        // synchronement dans ce wrapper sur le même thread voit la garde
        // posée et bascule sur le path froid au lieu de ré-émettre.
        _emittingMarshal = true;
        try
        {
            var sw = Stopwatch.StartNew();
            DeckleThreadingSource.Log.MarshalQueued(operation, caller, queue_depth: -1);

            bool ok = queue.TryEnqueue(priority, () =>
            {
                int wait_ms = (int)sw.ElapsedMilliseconds;
                sw.Restart();
                try
                {
                    callback();
                }
                finally
                {
                    int run_ms = (int)sw.ElapsedMilliseconds;
                    DeckleThreadingSource.Log.MarshalCompleted(
                        operation, caller, wait_ms, run_ms);
                }
            });

            if (!ok && !_logging)
            {
                _logging = true;
                try
                {
                    DeckleThreadingSource.Log.DispatcherEnqueueRejected(
                        rejectSource, rejectWhat);
                }
                finally { _logging = false; }
            }
            return ok;
        }
        finally { _emittingMarshal = false; }
    }
}
