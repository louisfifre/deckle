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
