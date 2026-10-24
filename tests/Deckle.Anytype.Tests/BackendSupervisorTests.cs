using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "unit")]
public sealed class BackendSupervisorTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Disposal_cancels_and_drains_inflight_reconciliation()
    {
        var reconciler = new BlockingBackendReconciler();
        var health = new FakeBackendHealth();
        var supervisor = new BackendSupervisor(reconciler, health, new ControlledBackendTime());

        Task<BackendStartOutcome> initialization = supervisor.EnsureRunningAsync(Ct);
        await reconciler.Entered.Task.WaitAsync(Ct);

        await supervisor.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialization);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task Caller_cancellation_does_not_cancel_shared_initialization()
    {
        var reconciler = new BlockingBackendReconciler();
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());
        using var callerCts = new CancellationTokenSource();

        Task<BackendStartOutcome> abandoned = supervisor.EnsureRunningAsync(callerCts.Token);
        await reconciler.Entered.Task.WaitAsync(Ct);
        callerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        reconciler.Completion.TrySetResult(new(
            BackendStartOutcome.NotProvisioned, null, "test", "not-provisioned"));

        Assert.Equal(
            BackendStartOutcome.NotProvisioned,
            await supervisor.EnsureRunningAsync(Ct));
        Assert.Equal(1, reconciler.Calls);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task Faulted_initialization_cannot_bypass_owned_resource_cleanup()
    {
        var reconciler = new BlockingBackendReconciler();
        var health = new FakeBackendHealth();
        var supervisor = new BackendSupervisor(reconciler, health, new ControlledBackendTime());
        Task<BackendStartOutcome> initialization = supervisor.EnsureRunningAsync(Ct);
        await reconciler.Entered.Task.WaitAsync(Ct);
        reconciler.Completion.TrySetException(new InvalidOperationException("reconciliation failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => initialization);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => supervisor.DisposeAsync().AsTask());

        Assert.True(health.Disposed);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task Concurrent_disposals_share_the_same_lifecycle_drain()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconciler = new BlockingBackendReconciler { ReleaseCancellation = release };
        var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());
        Task<BackendStartOutcome> initialization = supervisor.EnsureRunningAsync(Ct);
        await reconciler.Entered.Task.WaitAsync(Ct);

        Task first = supervisor.DisposeAsync().AsTask();
        await reconciler.CancellationObserved.Task.WaitAsync(Ct);
        Task second = supervisor.DisposeAsync().AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        release.TrySetResult();
        await Task.WhenAll(first, second);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialization);
    }

    [Fact]
    public async Task First_restart_uses_first_backoff()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var process = new FakeBackendProcess(17, executable);
        var reconciler = new SequenceBackendReconciler(
            new(BackendStartOutcome.Started, process, "first", "spawned"),
            new(BackendStartOutcome.EndpointConflict, null, "second", "endpoint-conflict"));
        var time = new ControlledBackendTime();
        await using var supervisor = new BackendSupervisor(reconciler, new FakeBackendHealth(), time);

        Assert.Equal(BackendStartOutcome.Started, await supervisor.EnsureRunningAsync(Ct));
        process.Exit(1);
        await reconciler.SecondCallEntered.Task.WaitAsync(Ct);

        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(time.Delays));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task Recovery_after_a_start_that_never_served_is_not_reported_as_a_process_exit()
    {
        var reconciler = new SequenceBackendReconciler(
            new(BackendStartOutcome.TimedOut, null, "first", "timed-out", PendingProcessId: 52),
            new(BackendStartOutcome.EndpointConflict, null, "second", "endpoint-conflict"));
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        Assert.Equal(BackendStartOutcome.TimedOut, await supervisor.EnsureRunningAsync(Ct));
        await reconciler.SecondCallEntered.Task.WaitAsync(Ct);

        Assert.Equal(new[] { "startup", "start-failed" }, reconciler.Triggers);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task Restart_ladder_survives_a_faulted_reconciliation()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var process = new FakeBackendProcess(17, executable);
        var reconciler = new FaultOnSecondCallReconciler(process);
        var time = new ControlledBackendTime();
        await using var supervisor = new BackendSupervisor(reconciler, new FakeBackendHealth(), time);

        Assert.Equal(BackendStartOutcome.Started, await supervisor.EnsureRunningAsync(Ct));
        process.Exit(1);
        await reconciler.ThirdCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // The fault cost one rung of the ladder, not the supervision itself.
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)], time.Delays);
    }

    // The restart ladder waits 2, 5, 15 then 60 s: after n attempts the controlled
    // clock reads 2, 7, 22, 82, 142, 202, 262, 322, 382, 442 s. Five minutes of
    // grace are therefore passed by the eighth attempt, the ninth call overall.
    private static BackendReconciliationResult StillUnready(
        int pid, BackendReplacement? replacement = null) =>
        new(BackendStartOutcome.TimedOut, null, "wait", "timed-out", pid, replacement);

    private static BackendReconciliationResult Serving(int pid, BackendReplacement? replacement = null) =>
        new(BackendStartOutcome.Started, new FakeBackendProcess(pid, Path.GetFullPath("anytype.exe")),
            "spawned", "spawned", Replacement: replacement);

    private static BackendReconciliationResult[] Unready(int pid, int count) =>
        Enumerable.Range(0, count).Select(_ => StillUnready(pid)).ToArray();

    private static async Task<SequenceBackendReconciler> RunToTheEnd(
        params BackendReconciliationResult[] results)
    {
        var reconciler = new SequenceBackendReconciler(results);
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());
        Assert.Equal(results[0].Outcome, await supervisor.EnsureRunningAsync(Ct));
        await reconciler.Drained.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        return reconciler;
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task A_serve_unready_past_the_grace_is_asked_to_be_replaced_exactly_once()
    {
        // The startup wait and seven attempts name pid 52; the ninth call is the
        // one asked to replace it. It leaves the serve in place (declined), and the
        // two calls after it must not ask again.
        SequenceBackendReconciler reconciler = await RunToTheEnd(
            [
                .. Unready(52, 8),
                StillUnready(52, new BackendReplacement(BackendReplaceOutcome.Declined)),
                .. Unready(52, 2),
                Serving(60),
            ]);

        int[] expected = new int[12];
        expected[8] = 52;
        Assert.Equal(expected, reconciler.ReplaceRequests);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task The_reconciliation_asked_to_replace_a_serve_hands_back_the_one_that_recovers()
    {
        using var listener = new Deckle.TestSupport.TestEventListener("Deckle-Anytype");
        var reconciler = new SequenceBackendReconciler(
            [.. Unready(52, 8), Serving(60, new BackendReplacement(BackendReplaceOutcome.Ended))]);
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        Assert.Equal(BackendStartOutcome.TimedOut, await supervisor.EnsureRunningAsync(Ct));
        Assert.True(
            SpinWait.SpinUntil(
                () => listener.Events.Any(e => e.EventId == DeckleAnytypeSource.EvtBackendRecovered),
                TimeSpan.FromSeconds(10)));

        Assert.Equal(52, reconciler.ReplaceRequests[8]);
        Assert.Equal(9, reconciler.Calls);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task A_serve_unready_below_the_grace_is_never_asked_to_be_replaced()
    {
        // Seven attempts, the last after 262 s: still inside the five minutes.
        SequenceBackendReconciler reconciler = await RunToTheEnd(
            [.. Unready(52, 7), Serving(60)]);

        Assert.All(reconciler.ReplaceRequests, pid => Assert.Equal(0, pid));
        Assert.Equal(8, reconciler.Calls);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task A_new_pid_starts_the_grace_again()
    {
        // Pid 52 for the startup and three attempts, then pid 53 from the clock's
        // 82 s: its grace ends at 382 s, which is the tenth call, not the ninth.
        SequenceBackendReconciler reconciler = await RunToTheEnd(
            [.. Unready(52, 4), .. Unready(53, 6), Serving(60)]);

        int[] expected = new int[11];
        expected[9] = 53;
        Assert.Equal(expected, reconciler.ReplaceRequests);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task An_attempt_that_is_not_a_wait_on_a_live_serve_starts_the_grace_again()
    {
        // The rejected attempt at 82 s breaks the streak: pid 52 is seen anew at
        // 142 s and is asked after 442 s, the eleventh call.
        SequenceBackendReconciler reconciler = await RunToTheEnd(
            [
                .. Unready(52, 4),
                new BackendReconciliationResult(BackendStartOutcome.StartRejected, null, "r", "start-rejected"),
                .. Unready(52, 6),
                Serving(60),
            ]);

        int[] expected = new int[12];
        expected[10] = 52;
        Assert.Equal(expected, reconciler.ReplaceRequests);
    }

    // Startup succeeds, the first restart throws, the second ends the ladder.
    private sealed class FaultOnSecondCallReconciler(FakeBackendProcess process) : IBackendReconciler
    {
        private int _calls;
        public TaskCompletionSource ThirdCallEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<BackendReconciliationResult> ReconcileAsync(
            string trigger, CancellationToken ct, int replaceProcessId = 0)
        {
            ct.ThrowIfCancellationRequested();
            switch (Interlocked.Increment(ref _calls))
            {
                case 1:
                    return Task.FromResult(new BackendReconciliationResult(
                        BackendStartOutcome.Started, process, "first", "spawned"));
                case 2:
                    return Task.FromException<BackendReconciliationResult>(
                        new InvalidOperationException("listener inspection failed"));
                default:
                    ThirdCallEntered.TrySetResult();
                    return Task.FromResult(new BackendReconciliationResult(
                        BackendStartOutcome.EndpointConflict, null, "third", "endpoint-conflict"));
            }
        }
    }
}
