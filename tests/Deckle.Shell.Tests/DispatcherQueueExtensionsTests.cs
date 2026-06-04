using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Diagnostics;
using Deckle.Shell;
using Microsoft.UI.Dispatching;
using Xunit;

namespace Deckle.Shell.Tests;

// ── DispatcherQueueExtensionsTests ──────────────────────────────────────────
//
// Regression test for the `_emittingMarshal` anti-recursion guard placed in
// `TryEnqueueObserved`. Reproduces the scenario that crashed the app in session
// 2026-05-25 :
//
//   1. On a worker thread, a call to `queue.TryEnqueueObserved(...)` emits
//      `MarshalQueued` synchronously.
//   2. An EventListener installed on `Deckle.Diagnostics.Threading` receives
//      the synchronous event on the same worker thread (OnEventWritten is
//      synchronous on the EventSource side).
//   3. The listener routes to a sink that calls `queue.TryEnqueueObserved`
//      again on the same worker thread (mimics LogWindowEventListener →
//      LogWindow.Write → DispatcherQueue.TryEnqueueObserved in production).
//   4. Without guard: the 2nd entry re-emits `MarshalQueued` → re-route →
//      re-call → synchronous recursion → StackOverflowException.
//   5. With the thread-static `_emittingMarshal` guard: the 2nd entry switches
//      to the cold `TryEnqueueOrLog` path, which enqueues without emitting; the
//      synchronous chain terminates in two levels.
//
// The test holds this property without depending on the timing of the enqueued
// callback: the reentrant listener only re-calls for `MarshalQueued` (EventId
// 1), ignores `MarshalCompleted` (which is emitted later on the queue thread);
// the loop being protected is strictly synchronous on the caller thread side.
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
                callback: () => { /* no-op: tested property lives caller-side. */ },
                rejectSource: "TEST",
                rejectWhat: "test enqueue");

            Assert.True(ok);

            // Central property of the _emittingMarshal fix: the 2nd
            // MarshalQueued emission attempt, triggered by the reentrant
            // listener on the same thread, is short-circuited by the
            // thread-static guard. Without the fix, we would have either
            // StackOverflow or listener.MarshalQueuedCount == maxReentry (50).
            Assert.Equal(1, listener.MarshalQueuedCount);
            Assert.False(listener.HitMaxReentry);
        }
        finally
        {
            await controller.ShutdownQueueAsync();
        }
    }

    // Property: the `priority` parameter exposed by TryEnqueueObserved (and by
    // mirrored TryEnqueueOrLog) must be propagated to the underlying
    // `DispatcherQueue.TryEnqueue(priority, callback)`. DispatcherQueue API
    // does not allow directly inspecting which priority a callback was
    // enqueued with; observe the property indirectly through execution order:
    // queue 4 callbacks with Low, Low, Normal, High priorities while the UI
    // thread is blocked on a gate, then release the gate. The queue must
    // dispatch in High → Normal → Low order (FIFO within level). If priority is
    // ignored (everything posted as Normal), order would be arrival order:
    // Low, Low, Normal, High.
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

            // Blocks the UI thread before enqueuing the 4 callbacks under
            // test. Without this, the first enqueued callback could execute
            // before the next ones are in the queue, and the dispatcher would
            // never have to arbitrate between priority levels.
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

    // Mirror of TryEnqueueObservedHonorsPriorityParameter for the historical
    // TryEnqueueOrLog wrapper. Extended to priority in the same wave as
    // TryEnqueueObserved because TryEnqueueObserved's cold path delegates to it,
    // and losing priority on the cold path would break Settings page UI
    // coordination when Verbose is not listened to.
    [Fact]
    [Trait("Category", "observability")]
    public async Task TryEnqueueOrLogHonorsPriorityParameter()
    {
        var controller = DispatcherQueueController.CreateOnDedicatedThread();
        var queue = controller.DispatcherQueue;
        try
        {
            var order = new ConcurrentQueue<string>();
            var allDone = new CountdownEvent(3);
            using var gate = new ManualResetEventSlim(false);
            var testCt = TestContext.Current.CancellationToken;

            queue.TryEnqueue(() => gate.Wait(TimeSpan.FromSeconds(5), testCt));

            queue.TryEnqueueOrLog(
                () => { order.Enqueue("low"); allDone.Signal(); },
                "TEST", "low", DispatcherQueuePriority.Low);
            queue.TryEnqueueOrLog(
                () => { order.Enqueue("normal"); allDone.Signal(); },
                "TEST", "normal");
            queue.TryEnqueueOrLog(
                () => { order.Enqueue("high"); allDone.Signal(); },
                "TEST", "high", DispatcherQueuePriority.High);

            gate.Set();
            Assert.True(allDone.Wait(TimeSpan.FromSeconds(5), testCt));

            Assert.Equal(
                new[] { "high", "normal", "low" },
                order.ToArray());
        }
        finally
        {
            await controller.ShutdownQueueAsync();
        }
    }

    // Reentrant EventListener: observes `Deckle.Diagnostics.Threading` and
    // calls `queue.TryEnqueueObserved` again on each received `MarshalQueued`.
    // This is exactly the pattern that triggered recursion in production: the
    // sink (`LogWindow.Write`) calls `DispatcherQueue.TryEnqueueObserved` to
    // marshal the ListView update to the UI thread, and this synchronous chain
    // looped without the guard.
    //
    // `MarshalQueuedCount` reflects how many times `MarshalQueued` entered
    // `OnEventWritten` on the worker thread (i.e. directly from the caller, not
    // via the queue): exactly the value the guard must bound to 1.
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

            // Catch up sources already created before listener instantiation
            // (OnEventSourceCreated ran during base() with fields still
            // unassigned).
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

            // The re-call that would trigger the synchronous loop without the
            // guard. The call happens on the same thread that emitted the
            // original event; the thread-static `_emittingMarshal` guard is
            // placed precisely to block this branch.
            _queue.TryEnqueueObserved(
                operation: "reentrant",
                caller: "reentrant-listener",
                callback: () => { /* no-op */ },
                rejectSource: "TEST",
                rejectWhat: "reentrant enqueue");
        }
    }
}
