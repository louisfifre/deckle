using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "integration")]
public sealed class BackendReconcilerTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The shipped readiness window and probe cadence (private to BackendReconciler,
    // so mirrored here on purpose): the reconciler polls once per interval until the
    // window closes. A retune of either is a contract change this test should flag.
    private const int ReadinessTimeoutMs = 20_000;
    private const int ProbeIntervalMs = 500;

    [Fact]
    [Trait("Category", "regression")]
    public async Task Successor_adopts_warming_backend_without_spawning_duplicate()
    {
        string executable = Path.GetFullPath("anytype.exe");
        const int processId = 41;
        string mutexName = $"Deckle.Anytype.Reconciliation.Tests.{Guid.NewGuid():N}";
        var processes = new ConcurrentWarmingProcessHost(processId, executable);
        var listener = new ScriptedBackendListener();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var successorWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeBackendProvider(executable);
        var first = new BackendReconciler(
            provider, processes, listener, new FakeBackendHealth(),
            new BackendReconciliationLease(mutexName),
            new ControlledBackendTime
            {
                FirstDelayEntered = entered,
                ReleaseFirstDelay = release,
            });
        var successor = new BackendReconciler(
            provider, processes, listener, new FakeBackendHealth(),
            new BackendReconciliationLease(mutexName, () => successorWaiting.TrySetResult()),
            new ControlledBackendTime());

        Task<BackendReconciliationResult> firstPending = first.ReconcileAsync("startup", Ct);
        await entered.Task.WaitAsync(Ct);
        Task<BackendReconciliationResult> successorPending = successor.ReconcileAsync("startup", Ct);
        await successorWaiting.Task.WaitAsync(Ct);

        Assert.False(successorPending.IsCompleted);
        Assert.Equal(1, processes.SpawnCount);

        listener.Current = new(BackendListenerState.Owned, processId);
        release.TrySetResult();

        BackendReconciliationResult firstResult = await firstPending;
        BackendReconciliationResult successorResult = await successorPending;

        Assert.Equal(BackendStartOutcome.Started, firstResult.Outcome);
        Assert.Equal(BackendStartOutcome.AlreadyRunning, successorResult.Outcome);
        Assert.Equal(processId, successorResult.Process?.Id);
        Assert.Equal(1, processes.SpawnCount);
        firstResult.Process?.Dispose();
        successorResult.Process?.Dispose();
    }

    [Fact]
    public async Task Unknown_listener_owner_prevents_spawn()
    {
        string executable = Path.GetFullPath("anytype.exe");
        string unknown = Path.GetFullPath("other.exe");
        var owner = new FakeBackendProcess(73, unknown);
        var processes = new FakeBackendProcessHost();
        processes.Opened.Add(owner.Id, owner);
        var listener = new ScriptedBackendListener
        {
            Current = new(BackendListenerState.Owned, owner.Id),
        };
        var reconciler = Create(
            executable, processes, listener, new FakeBackendHealth(), new ControlledBackendTime());

        BackendReconciliationResult result = await reconciler.ReconcileAsync("startup", Ct);

        Assert.Equal(BackendStartOutcome.EndpointConflict, result.Outcome);
        Assert.Equal(0, processes.SpawnCount);
    }

    // A failed read of the TCP owner table says nothing about who owns the
    // endpoint, so it is not a verdict on the endpoint: the reconciliation
    // looks again instead of ending the supervision for the session.
    [Fact]
    [Trait("Category", "regression")]
    public async Task Transient_listener_inspection_failure_is_inspected_again()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var owner = new FakeBackendProcess(61, executable);
        var processes = new FakeBackendProcessHost();
        processes.Opened.Add(owner.Id, owner);
        var listener = new ScriptedBackendListener
        {
            Current = new(BackendListenerState.Owned, owner.Id),
        };
        listener.Enqueue(new BackendListenerSnapshot(
            BackendListenerState.Failed, Error: "GetExtendedTcpTable returned 5"));
        var reconciler = Create(
            executable, processes, listener, new FakeBackendHealth(), new ControlledBackendTime());

        BackendReconciliationResult result = await reconciler.ReconcileAsync("startup", Ct);

        Assert.Equal(BackendStartOutcome.AlreadyRunning, result.Outcome);
        Assert.Equal(owner.Id, result.Process?.Id);
        Assert.Equal(0, processes.SpawnCount);
        result.Process?.Dispose();
    }

    // When the table cannot be read for the whole window the outcome stays
    // retryable (the supervisor backs off and asks again) and names its cause;
    // nothing is spawned blind, since the listener may well be owned.
    [Fact]
    [Trait("Category", "regression")]
    public async Task Persistent_listener_inspection_failure_stays_retryable_and_spawns_nothing()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var processes = new FakeBackendProcessHost();
        var listener = new ScriptedBackendListener
        {
            Current = new(BackendListenerState.Failed, Error: "GetExtendedTcpTable returned 5"),
        };
        var reconciler = Create(
            executable, processes, listener, new FakeBackendHealth(), new ControlledBackendTime());

        BackendReconciliationResult result = await reconciler.ReconcileAsync("startup", Ct);

        Assert.Equal(BackendStartOutcome.TimedOut, result.Outcome);
        Assert.StartsWith("listener_query_failed", result.Decision, StringComparison.Ordinal);
        Assert.Equal(0, processes.SpawnCount);
    }
    [Fact]
    public async Task Exited_spawn_cannot_report_started()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var spawned = new FakeBackendProcess(88, executable) { HasExited = true, ExitCode = 1 };
        var processes = new FakeBackendProcessHost { SpawnResult = spawned };
        var reconciler = Create(
            executable, processes, new ScriptedBackendListener(),
            new FakeBackendHealth(), new ControlledBackendTime());

        BackendReconciliationResult result = await reconciler.ReconcileAsync("startup", Ct);

        Assert.Equal(BackendStartOutcome.StartRejected, result.Outcome);
        Assert.Null(result.Process);
        Assert.Equal(1, processes.SpawnCount);
    }

    [Fact]
    public async Task Timed_out_warming_process_does_not_cause_second_spawn()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var warming = new FakeBackendProcess(52, executable);
        var processes = new FakeBackendProcessHost();
        processes.Running.Add(warming);
        var time = new ControlledBackendTime();
        var reconciler = Create(
            executable, processes, new ScriptedBackendListener(),
            new FakeBackendHealth(), time);

        BackendReconciliationResult result = await reconciler.ReconcileAsync("startup", Ct);

        Assert.Equal(BackendStartOutcome.TimedOut, result.Outcome);
        Assert.Null(result.Process);
        Assert.Equal(0, processes.SpawnCount);
        Assert.Equal(ReadinessTimeoutMs / ProbeIntervalMs, time.DelayCalls);
        // Waiting is the default: only an explicit request ever ends a serve.
        Assert.Equal(0, warming.TerminateCalls);
    }

    // The serve the supervisor found unready for too long: it is proven again
    // under the lease, ended, and the usual loop spawns its replacement.
    [Fact]
    [Trait("Category", "regression")]
    public async Task A_replace_request_ends_the_unready_serve_once_its_identity_is_proven_and_spawns_a_fresh_one()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var wedged = new FakeBackendProcess(52, executable);
        var fresh = new FakeBackendProcess(60, executable);
        BackendTestWorld world = StuckServe(executable, wedged);
        world.Processes.SpawnResult = fresh;
        // The re-proof reads the listener once and the loop reads it once before
        // it spawns; the fresh serve then owns it.
        world.Listener.Enqueue(new(BackendListenerState.Unbound), new(BackendListenerState.Unbound));
        world.Listener.Current = new(BackendListenerState.Owned, fresh.Id);

        BackendReconciliationResult result = await world.Reconciler.ReconcileAsync(
            BackendTriggers.StartFailed, Ct, replaceProcessId: wedged.Id);

        Assert.Equal(1, wedged.TerminateCalls);
        Assert.True(wedged.HasExited);
        Assert.Equal(BackendReplaceOutcome.Ended, result.Replacement?.Outcome);
        Assert.Equal(BackendStartOutcome.Started, result.Outcome);
        Assert.Same(fresh, result.Process);
        Assert.Equal(1, world.Processes.SpawnCount);
    }

    // A serve that bound the listener in the meantime is ready, not stuck: ending
    // it would take down a working Anytype. The ask is old news by the time the
    // lease is held, which is why identity is proven again here.
    [Fact]
    [Trait("Category", "regression")]
    public async Task A_replace_request_leaves_alone_a_serve_that_now_owns_the_listener()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var serving = new FakeBackendProcess(52, executable);
        BackendTestWorld world = StuckServe(executable, serving);
        world.Listener.Current = new(BackendListenerState.Owned, serving.Id);

        BackendReconciliationResult result = await world.Reconciler.ReconcileAsync(
            BackendTriggers.StartFailed, Ct, replaceProcessId: serving.Id);

        Assert.Equal(0, serving.TerminateCalls);
        Assert.False(serving.HasExited);
        Assert.Equal(BackendReplaceOutcome.Declined, result.Replacement?.Outcome);
        Assert.Equal(BackendStartOutcome.AlreadyRunning, result.Outcome);
        Assert.Equal(0, world.Processes.SpawnCount);
        result.Process?.Dispose();
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task A_replace_request_never_ends_a_process_that_is_not_a_trusted_backend()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var stranger = new FakeBackendProcess(52, Path.GetFullPath("other.exe"));
        var processes = new FakeBackendProcessHost();
        processes.Opened.Add(stranger.Id, stranger);
        var reconciler = Create(
            executable, processes, new ScriptedBackendListener(),
            new FakeBackendHealth(), new ControlledBackendTime());

        BackendReconciliationResult result = await reconciler.ReconcileAsync(
            BackendTriggers.StartFailed, Ct, replaceProcessId: stranger.Id);

        Assert.Equal(0, stranger.TerminateCalls);
        Assert.False(stranger.HasExited);
        Assert.Equal(BackendReplaceOutcome.Declined, result.Replacement?.Outcome);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task A_replace_request_for_a_pid_that_is_gone_ends_nothing()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var reconciler = Create(
            executable, new FakeBackendProcessHost(), new ScriptedBackendListener(),
            new FakeBackendHealth(), new ControlledBackendTime());

        BackendReconciliationResult result = await reconciler.ReconcileAsync(
            BackendTriggers.StartFailed, Ct, replaceProcessId: 52);

        Assert.Equal(BackendReplaceOutcome.Declined, result.Replacement?.Outcome);
    }

    // An unreadable port table cannot show that the serve does not own the port.
    [Fact]
    [Trait("Category", "regression")]
    public async Task A_replace_request_waits_when_the_listener_table_cannot_be_read()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var serve = new FakeBackendProcess(52, executable);
        BackendTestWorld world = StuckServe(executable, serve);
        world.Listener.Current = new(BackendListenerState.Failed, Error: "GetExtendedTcpTable returned 5");

        BackendReconciliationResult result = await world.Reconciler.ReconcileAsync(
            BackendTriggers.StartFailed, Ct, replaceProcessId: serve.Id);

        Assert.Equal(0, serve.TerminateCalls);
        Assert.Equal(BackendReplaceOutcome.Declined, result.Replacement?.Outcome);
        Assert.Equal(0, world.Processes.SpawnCount);
    }

    // The anti-duplicate rule outlives a refusal: a serve that could not be ended
    // is still alive and trusted, so no second one is spawned beside it.
    [Fact]
    [Trait("Category", "regression")]
    public async Task A_refused_ending_reports_its_cause_and_spawns_no_second_serve()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var elevated = new FakeBackendProcess(52, executable)
        {
            TerminateRefusal = ("Win32Exception", "Access is denied."),
        };
        BackendTestWorld world = StuckServe(executable, elevated);

        BackendReconciliationResult result = await world.Reconciler.ReconcileAsync(
            BackendTriggers.StartFailed, Ct, replaceProcessId: elevated.Id);

        Assert.Equal(BackendReplaceOutcome.Refused, result.Replacement?.Outcome);
        Assert.Equal("Win32Exception", result.Replacement?.ExType);
        Assert.Equal("Access is denied.", result.Replacement?.ExMessage);
        Assert.Equal(BackendStartOutcome.TimedOut, result.Outcome);
        Assert.Equal(elevated.Id, result.PendingProcessId);
        Assert.Equal(0, world.Processes.SpawnCount);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task A_serve_that_outlives_its_ending_is_a_refusal_not_a_wait_without_end()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var stubborn = new FakeBackendProcess(52, executable) { SurvivesTermination = true };
        BackendTestWorld world = StuckServe(executable, stubborn);

        BackendReconciliationResult result = await world.Reconciler.ReconcileAsync(
            BackendTriggers.StartFailed, Ct, replaceProcessId: stubborn.Id);

        Assert.Equal(BackendReplaceOutcome.Refused, result.Replacement?.Outcome);
        Assert.Equal("TimeoutException", result.Replacement?.ExType);
        Assert.Equal(0, world.Processes.SpawnCount);
    }

    // One unready serve, reachable the way the real host reaches it: through
    // enumeration and through Open, with the listener empty.
    private static BackendTestWorld StuckServe(string executable, FakeBackendProcess serve)
    {
        var processes = new FakeBackendProcessHost();
        processes.Running.Add(serve);
        processes.Opened.Add(serve.Id, serve);
        var listener = new ScriptedBackendListener();
        return new BackendTestWorld(
            processes, listener,
            Create(executable, processes, listener, new FakeBackendHealth(), new ControlledBackendTime()));
    }

    private sealed record BackendTestWorld(
        FakeBackendProcessHost Processes,
        ScriptedBackendListener Listener,
        BackendReconciler Reconciler);

    [Fact]
    [Trait("Category", "regression")]
    public async Task Timed_out_reconciliation_names_the_live_serve_it_waited_on()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var wedged = new FakeBackendProcess(52, executable);
        var processes = new FakeBackendProcessHost();
        processes.Running.Add(wedged);
        var reconciler = Create(
            executable, processes, new ScriptedBackendListener(),
            new FakeBackendHealth(), new ControlledBackendTime());

        BackendReconciliationResult result = await reconciler.ReconcileAsync("start-failed", Ct);

        Assert.Equal(BackendStartOutcome.TimedOut, result.Outcome);
        Assert.Equal(wedged.Id, result.PendingProcessId);
        Assert.False(wedged.HasExited);
    }

    private static BackendReconciler Create(
        string executable,
        IBackendProcessHost processes,
        IBackendListenerOwner listener,
        IBackendHealthProbe health,
        IBackendTime time) => new(
            new FakeBackendProvider(executable),
            processes,
            listener,
            health,
            new ImmediateBackendCoordinator(),
            time);

    private sealed class ConcurrentWarmingProcessHost(int processId, string executablePath)
        : IBackendProcessHost
    {
        private int _spawnCount;
        private volatile bool _running;
        public int SpawnCount => _spawnCount;

        public IReadOnlyList<IBackendProcess> FindRunning(
            IReadOnlyCollection<string> executablePaths) =>
            _running ? [new FakeBackendProcess(processId, executablePath)] : [];

        public IBackendProcess? Open(int requestedProcessId) =>
            _running && requestedProcessId == processId
                ? new FakeBackendProcess(processId, executablePath)
                : null;

        public IBackendProcess? Spawn(BackendProcessSpec spec, bool inRestart)
        {
            Interlocked.Increment(ref _spawnCount);
            _running = true;
            return new FakeBackendProcess(processId, executablePath);
        }
    }
}
