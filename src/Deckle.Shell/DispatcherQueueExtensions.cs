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
// Garde anti-récursion : si LogWindow appelle l'un de ces wrappers et
// que sa propre queue est fermée, le Warning loggé route à nouveau vers
// LogWindow → re-TryEnqueue → re-fail → boucle. Un flag thread-static
// court-circuite la deuxième tentative. La garde reste pertinente après
// la migration EventSource : `LogWindowEventListener` reçoit toujours
// l'event Warning et le repousse dans la même `DispatcherQueue` côté
// LogWindow.
//
// Pourquoi pas un simple `if (!queue.TryEnqueue(...)) _log.Warning(...)`
// inline à chaque site ? Centraliser réduit la duplication (8 sites
// rejet + 5 sites observed) et garantit que le pattern de garde anti-
// récursion est partout, sans risque d'oubli.

public static class DispatcherQueueExtensions
{
    [System.ThreadStatic]
    private static bool _logging;

    /// <summary>
    /// Enqueue le callback sur la dispatcher queue. Si l'enqueue échoue
    /// (queue fermée), émet un Warning sur DeckleThreadingSource avec la
    /// source caller et la description fournie, puis retourne false.
    /// </summary>
    /// <param name="queue">La dispatcher queue cible.</param>
    /// <param name="callback">Le delegate à exécuter sur le UI thread.</param>
    /// <param name="source">Identifiant libre de l'émetteur (ex. "HUD", "LOGWIN"). Passé en champ payload de l'event.</param>
    /// <param name="what">Description courte de l'event perdu (ex. "log entry", "recording state").</param>
    public static bool TryEnqueueOrLog(
        this DispatcherQueue queue,
        DispatcherQueueHandler callback,
        string source,
        string what)
    {
        bool ok = queue.TryEnqueue(callback);
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
    public static bool TryEnqueueObserved(
        this DispatcherQueue queue,
        string operation,
        string caller,
        DispatcherQueueHandler callback,
        string rejectSource,
        string rejectWhat)
    {
        bool verboseEnabled = DeckleThreadingSource.Log.IsEnabled(
            EventLevel.Verbose, (EventKeywords)Keywords.Threading);

        if (!verboseEnabled)
        {
            // Path froid — pas d'instrumentation Queued/Completed, juste
            // l'enqueue brut + la voie de rejet historique. La gate
            // Warning sur DispatcherEnqueueRejected reste ouverte
            // indépendamment du Verbose.
            return queue.TryEnqueueOrLog(callback, rejectSource, rejectWhat);
        }

        // Path chaud — instrumentation complète. Stopwatch capturé en
        // closure pour mesurer wait_ms (queue → début callback) et
        // run_ms (durée callback).
        var sw = Stopwatch.StartNew();
        DeckleThreadingSource.Log.MarshalQueued(operation, caller, queue_depth: -1);

        bool ok = queue.TryEnqueue(() =>
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
}
