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

// Regression test for the `_emittingMarshal` guard in `TryEnqueueObserved`.
// `TryEnqueueObserved` emits `MarshalQueued` synchronously, and EventListener
// delivery is synchronous too, so a listener that marshals through
// `TryEnqueueObserved` again re-enters on the same thread and recurses until
// the stack overflows. With the guard, the re-entrant call takes the cold
// `TryEnqueueOrLog` path and the chain stops at two levels.
//
// The listener re-calls only on `MarshalQueued`, never on `MarshalCompleted`
// (emitted later on the queue thread), so the test does not depend on when the
// enqueued callback runs.
//
// The marshal detail also answers to the Windowing admission policy, which
// fails closed until configured: each test opens it, so the instrumented path
// the guard lives on is the one exercised.
[Trait("Category", "regression")]
[Trait("Category", "observability")]
public class DispatcherQueueExtensionsTests : IDisposable
{
    public DispatcherQueueExtensionsTests()
        => OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Windowing);

    public void Dispose()
        => OperationalLogAdmission.Configure(static _ => false);

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

            // The guard short-circuits the re-entrant emission. Without it the
            // stack overflows or MarshalQueuedCount reaches maxReentry.
            Assert.Equal(1, listener.MarshalQueuedCount);
            Assert.False(listener.HitMaxReentry);
        }
        finally
        {
            await controller.ShutdownQueueAsync();
        }
    }

    // The `priority` parameter must reach `DispatcherQueue.TryEnqueue`. The
    // API hides which priority a callback was enqueued with, so the test
    // observes execution order: four callbacks queued Low, Low, Normal, High
    // behind a gate must run High → Normal → Low (FIFO within a level). An
    // ignored priority would keep arrival order.
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

    // Same property for TryEnqueueOrLog: TryEnqueueObserved's cold path
    // delegates to it, so losing priority there would break the Settings
    // pages' Low-priority coordination whenever Verbose is off.
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

    // Calls `TryEnqueueObserved` again on each `MarshalQueued`.
    // `MarshalQueuedCount` counts the `MarshalQueued` events seen on the
    // caller's thread: the value the guard must bound to 1.
    private sealed class ReentrantThreadingListener : EventListener
    {
        private const string ProviderName = "Deckle-Threading";

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

            _queue.TryEnqueueObserved(
                operation: "reentrant",
                caller: "reentrant-listener",
                callback: () => { /* no-op */ },
                rejectSource: "TEST",
                rejectWhat: "reentrant enqueue");
        }
    }
}
