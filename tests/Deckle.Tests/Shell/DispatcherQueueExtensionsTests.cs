using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Diagnostics;
using Deckle.Shell;
using Microsoft.UI.Dispatching;
using Xunit;

namespace Deckle.Tests.Shell;

// ── DispatcherQueueExtensionsTests ──────────────────────────────────────────
//
// Test régression sur la garde anti-récursion `_emittingMarshal` posée dans
// `TryEnqueueObserved`. Reproduit le scénario qui a crashé l'app en session
// 2026-05-25 :
//
//   1. Sur un thread worker, un appel à `queue.TryEnqueueObserved(...)`
//      émet `MarshalQueued` synchronement.
//   2. Un EventListener installé sur `Deckle.Diagnostics.Threading` reçoit
//      l'event synchrone sur le même worker thread (OnEventWritten est
//      synchrone côté EventSource).
//   3. Le listener route vers un sink qui re-appelle `queue.TryEnqueueObserved`
//      sur le même worker thread (mimic LogWindowEventListener →
//      LogWindow.Write → DispatcherQueue.TryEnqueueObserved côté prod).
//   4. Sans garde : la 2e entrée ré-émet `MarshalQueued` → re-route → re-call
//      → récursion synchrone → StackOverflowException.
//   5. Avec la garde `_emittingMarshal` thread-static : la 2e entrée bascule
//      sur le path froid `TryEnqueueOrLog` qui enqueue sans émettre — la
//      chaîne synchrone se termine en deux niveaux.
//
// Le test fait tenir cette propriété sans dépendre du timing du callback
// enqueué : le listener réentrant ne re-call que pour `MarshalQueued`
// (EventId 1), ignore `MarshalCompleted` (qui s'émet plus tard sur le thread
// de la queue) — la boucle qu'on protège est strictement synchrone côté
// thread caller.
[Trait("Category", "regression")]
[Trait("Category", "observability")]
public class DispatcherQueueExtensionsTests
{
    [Fact]
    public async Task TryEnqueueObservedDoesNotRecurseSynchronouslyOnReentrantListener()
    {
        var controller = DispatcherQueueController.CreateOnDedicatedThread();
        var queue = controller.DispatcherQueue;
        try
        {
            using var listener = new ReentrantThreadingListener(queue, maxReentry: 50);

            bool ok = queue.TryEnqueueObserved(
                operation: "test",
                caller: "test-caller",
                callback: () => { /* no-op — propriété testée vit côté caller. */ },
                rejectSource: "TEST",
                rejectWhat: "test enqueue");

            Assert.True(ok);

            // Propriété centrale du fix _emittingMarshal : la 2e tentative
            // d'émission MarshalQueued, déclenchée par le listener réentrant
            // sur le même thread, est court-circuitée par la garde thread-
            // static. Sans le fix, on aurait soit StackOverflow, soit
            // listener.MarshalQueuedCount == maxReentry (50).
            Assert.Equal(1, listener.MarshalQueuedCount);
            Assert.False(listener.HitMaxReentry);
        }
        finally
        {
            await controller.ShutdownQueueAsync();
        }
    }

    // Propriété : le paramètre `priority` exposé par TryEnqueueObserved
    // (et par TryEnqueueOrLog en miroir) doit être propagé au
    // `DispatcherQueue.TryEnqueue(priority, callback)` sous-jacent.
    // L'API DispatcherQueue ne permet pas d'inspecter directement à
    // quelle priority un callback a été enqueueé — on observe la
    // propriété indirectement par l'ordre d'exécution : queue 4
    // callbacks avec priorities Low, Low, Normal, High pendant que le
    // UI thread est bloqué sur une gate, puis libère la gate. La queue
    // doit dispatcher dans l'ordre High → Normal → Low (FIFO intra-
    // niveau). Si priority est ignoré (tout posté en Normal), l'ordre
    // serait celui d'arrivée Low, Low, Normal, High.
    [Fact]
    [Trait("Category", "observability")]
    public async Task TryEnqueueObservedHonorsPriorityParameter()
    {
        var controller = DispatcherQueueController.CreateOnDedicatedThread();
        var queue = controller.DispatcherQueue;
        try
        {
            var order = new ConcurrentQueue<string>();
            var allDone = new CountdownEvent(4);
            using var gate = new ManualResetEventSlim(false);
            var testCt = TestContext.Current.CancellationToken;

            // Bloque le UI thread avant l'enqueue des 4 callbacks à
            // tester. Sans ça, le premier enqueueé pourrait s'exécuter
            // avant que les suivants soient en queue, et la dispatcher
            // n'aurait jamais à arbitrer entre niveaux de priority.
            queue.TryEnqueue(() => gate.Wait(TimeSpan.FromSeconds(5), testCt));

            queue.TryEnqueueObserved(
                operation: "priority-test", caller: "test-low-1",
                callback: () => { order.Enqueue("low-1"); allDone.Signal(); },
                rejectSource: "TEST", rejectWhat: "low 1",
                priority: DispatcherQueuePriority.Low);

            queue.TryEnqueueObserved(
                operation: "priority-test", caller: "test-low-2",
                callback: () => { order.Enqueue("low-2"); allDone.Signal(); },
                rejectSource: "TEST", rejectWhat: "low 2",
                priority: DispatcherQueuePriority.Low);

            queue.TryEnqueueObserved(
                operation: "priority-test", caller: "test-normal",
                callback: () => { order.Enqueue("normal"); allDone.Signal(); },
                rejectSource: "TEST", rejectWhat: "normal");

            queue.TryEnqueueObserved(
                operation: "priority-test", caller: "test-high",
                callback: () => { order.Enqueue("high"); allDone.Signal(); },
                rejectSource: "TEST", rejectWhat: "high",
                priority: DispatcherQueuePriority.High);

            gate.Set();
            Assert.True(allDone.Wait(TimeSpan.FromSeconds(5), testCt));

            Assert.Equal(
                new[] { "high", "normal", "low-1", "low-2" },
                order.ToArray());
        }
        finally
        {
            await controller.ShutdownQueueAsync();
        }
    }

    // EventListener réentrant — observe `Deckle.Diagnostics.Threading` et
    // re-appelle `queue.TryEnqueueObserved` à chaque `MarshalQueued` reçu.
    // C'est exactement le pattern qui a déclenché la récursion en prod :
    // le sink (`LogWindow.Write`) appelle `DispatcherQueue.TryEnqueueObserved`
    // pour marshaller la mise à jour ListView vers le UI thread, et cette
    // chaîne synchrone partait en boucle sans la garde.
    //
    // Le compteur `MarshalQueuedCount` reflète combien de fois `MarshalQueued`
    // est entré dans `OnEventWritten` sur le worker thread (i.e. directement
    // depuis le caller, pas via la queue) — c'est exactement la grandeur que
    // la garde doit borner à 1.
    private sealed class ReentrantThreadingListener : EventListener
    {
        private const string ProviderName = "Deckle.Diagnostics.Threading";

        private readonly DispatcherQueue _queue;
        private readonly int _maxReentry;
        private int _reentryCount;
        public int MarshalQueuedCount;
        public bool HitMaxReentry;

        public ReentrantThreadingListener(DispatcherQueue queue, int maxReentry)
        {
            _queue = queue;
            _maxReentry = maxReentry;

            // Rattrapage des sources déjà créées avant l'instanciation du
            // listener (OnEventSourceCreated a couru pendant base() avec les
            // champs encore non-assignés).
            foreach (var source in EventSource.GetSources())
            {
                if (source.Name == ProviderName)
                    EnableEvents(source, EventLevel.LogAlways, EventKeywords.All);
            }
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == ProviderName)
                EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventSource.Name != ProviderName) return;
            if (e.EventId != DeckleThreadingSource.EvtMarshalQueued) return;

            Interlocked.Increment(ref MarshalQueuedCount);

            int n = Interlocked.Increment(ref _reentryCount);
            if (n > _maxReentry)
            {
                HitMaxReentry = true;
                return;
            }

            // Le re-call qui déclencherait la boucle synchrone sans garde.
            // L'appel se fait sur le même thread que celui qui a émis
            // l'event original — la garde `_emittingMarshal` thread-static
            // est précisément posée pour bloquer cette branche.
            _queue.TryEnqueueObserved(
                operation: "reentrant",
                caller: "reentrant-listener",
                callback: () => { /* no-op */ },
                rejectSource: "TEST",
                rejectWhat: "reentrant enqueue");
        }
    }
}
