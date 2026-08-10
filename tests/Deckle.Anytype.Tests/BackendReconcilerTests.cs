using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

public sealed class BackendReconcilerTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        Assert.Equal(40, time.DelayCalls);
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

        public IBackendProcess? Spawn(BackendProcessSpec spec)
        {
            Interlocked.Increment(ref _spawnCount);
            _running = true;
            return new FakeBackendProcess(processId, executablePath);
        }
    }
}
