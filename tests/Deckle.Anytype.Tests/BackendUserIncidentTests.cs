using Deckle.Anytype;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Anytype.Tests;

// The tray shows a backend that Deckle cannot bring back on its own, from the
// point the log marks it (a failing streak, a give-up, a held port) to the
// recovery. A backend that comes back within a restart or two shows nothing.
[Trait("Category", "observability")]
[Collection(AnytypeUserIncidentCollection.Name)]
public sealed class BackendUserIncidentTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool IsOpen => UserIncidentRegistry.Instance.IsOpen(UserIncidents.AnytypeBackend);

    private static BackendReconciliationResult Rejected(string id)
        => new(BackendStartOutcome.StartRejected, null, id, "start-rejected");

    [Fact]
    public async Task AFailingRestartStreakIsAUserIncidentUntilTheBackendRecovers()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        string executable = Path.GetFullPath("anytype.exe");
        var first = new FakeBackendProcess(17, executable);
        var second = new FakeBackendProcess(18, executable);
        var time = new ControlledBackendTime();
        var reconciler = new SequenceBackendReconciler(
            new(BackendStartOutcome.Started, first, "start", "spawned"),
            Rejected("rejected-0"),
            Rejected("rejected-1"),
            Rejected("rejected-2"),
            new(BackendStartOutcome.Started, second, "back", "spawned"));
        var opened = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (IsOpen) opened.TrySetResult(UserIncidentRegistry.Instance.Snapshot()
                .Single(i => i.Id == UserIncidents.AnytypeBackend).Reason);
        }
        UserIncidentRegistry.Instance.Changed += OnChanged;
        try
        {
            await using var supervisor = new BackendSupervisor(reconciler, new FakeBackendHealth(), time);

            Assert.Equal(BackendStartOutcome.Started, await supervisor.EnsureRunningAsync(Ct));
            Assert.False(IsOpen);
            first.Exit(1);

            Assert.Equal("restarts_failing", await opened.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct));
            Assert.True(SpinWait.SpinUntil(() => !IsOpen, TimeSpan.FromSeconds(10)));
        }
        finally
        {
            UserIncidentRegistry.Instance.Changed -= OnChanged;
        }
    }

    [Fact]
    public async Task ABackendThatComesBackAtTheFirstRestartIsNoUserIncident()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        string executable = Path.GetFullPath("anytype.exe");
        var first = new FakeBackendProcess(17, executable);
        var second = new FakeBackendProcess(18, executable);
        var reconciler = new SequenceBackendReconciler(
            new(BackendStartOutcome.Started, first, "start", "spawned"),
            new(BackendStartOutcome.Started, second, "back", "spawned"));
        int changes = 0;
        void OnChanged() => Interlocked.Increment(ref changes);
        UserIncidentRegistry.Instance.Changed += OnChanged;
        try
        {
            await using var supervisor = new BackendSupervisor(
                reconciler, new FakeBackendHealth(), new ControlledBackendTime());

            Assert.Equal(BackendStartOutcome.Started, await supervisor.EnsureRunningAsync(Ct));
            first.Exit(1);
            Assert.True(SpinWait.SpinUntil(() => reconciler.Calls == 2, TimeSpan.FromSeconds(10)));
            // Disposal drains the restart, so whatever it would report is in.
            await supervisor.DisposeAsync();

            Assert.Equal(0, Volatile.Read(ref changes));
        }
        finally
        {
            UserIncidentRegistry.Instance.Changed -= OnChanged;
        }
    }

    [Fact]
    public async Task APortHeldByAnotherProgramAtStartIsAUserIncident()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        var reconciler = new SequenceBackendReconciler(
            new BackendReconciliationResult(BackendStartOutcome.EndpointConflict, null, "start", "endpoint-conflict"));
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        Assert.Equal(BackendStartOutcome.EndpointConflict, await supervisor.EnsureRunningAsync(Ct));

        Assert.Equal(
            "endpoint_conflict",
            UserIncidentRegistry.Instance.Snapshot().Single(i => i.Id == UserIncidents.AnytypeBackend).Reason);
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
    }

    [Fact]
    public async Task AFirstStartWithNoBackendInstalledIsNoUserIncident()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        var reconciler = new SequenceBackendReconciler(
            new BackendReconciliationResult(BackendStartOutcome.NotProvisioned, null, "start", "not-provisioned"));
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        Assert.Equal(BackendStartOutcome.NotProvisioned, await supervisor.EnsureRunningAsync(Ct));

        Assert.False(IsOpen);
    }
}
