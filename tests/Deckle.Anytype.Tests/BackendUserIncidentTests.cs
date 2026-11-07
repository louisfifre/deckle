using System.Diagnostics.Tracing;
using Deckle.Anytype;
using Deckle.Diagnostics;
using Deckle.TestSupport;
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

    // ── The bound on the restart ladder ───────────────────────────────────

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    private static FakeBackendProcess Dead(int id, TimeSpan? age = null, int? exitCode = 1)
    {
        var process = new FakeBackendProcess(id, Path.GetFullPath("anytype.exe"))
        {
            StartedAt = DateTimeOffset.UtcNow - (age ?? TimeSpan.Zero),
        };
        process.Exit(exitCode ?? 0);
        process.ExitCode = exitCode;
        return process;
    }

    private static bool IsExhausted => UserIncidentRegistry.Instance.Snapshot()
        .Any(i => i.Id == UserIncidents.AnytypeBackend && i.Reason == "restarts_exhausted");

    private static string ReasonOfOpenIncident()
        => UserIncidentRegistry.Instance.Snapshot().Single(i => i.Id == UserIncidents.AnytypeBackend).Reason;

    private static BackendReconciliationResult[] OneStartThenRejections(int rejections, int? lastExitCode = null)
    {
        var results = new List<BackendReconciliationResult>
        {
            new(BackendStartOutcome.Started, Dead(17), "start", "spawned"),
        };
        for (int i = 0; i < rejections; i++)
            results.Add(Rejected($"rejected-{i}") with { ExitCode = i == rejections - 1 ? lastExitCode : null });
        return [.. results];
    }

    [Fact]
    public async Task ABackendThatNeverStartsStopsAfterTheBoundWithOneErrorNamingTheExitCode()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        using var listener = new TestEventListener("Deckle-Anytype");
        // The first serve dies, then seven restarts are rejected: eight failures in a row.
        var reconciler = new SequenceBackendReconciler(OneStartThenRejections(7, lastExitCode: 3));
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        await supervisor.EnsureRunningAsync(Ct);
        Assert.True(SpinWait.SpinUntil(() => IsExhausted, TimeSpan.FromSeconds(10)));
        await supervisor.DisposeAsync();

        EventWrittenEventArgs exhausted = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendRestartsExhausted);
        Assert.Equal(EventLevel.Error, exhausted.Level);
        Assert.Equal(BackendSupervisor.MaxConsecutiveFailures.ToString(), Field(exhausted, "attempts"));
        Assert.Equal("start-rejected", Field(exhausted, "last_outcome"));
        Assert.Equal("3", Field(exhausted, "exit_code"));
        Assert.Equal("True", Field(exhausted, "exit_known"));
        Assert.Equal(8, reconciler.Calls);
        Assert.Equal("restarts_exhausted", ReasonOfOpenIncident());
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
    }

    [Fact]
    public async Task AnUnreadableExitCodeIsReportedAsUnknownNotAsANumber()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        using var listener = new TestEventListener("Deckle-Anytype");
        var reconciler = new SequenceBackendReconciler(OneStartThenRejections(7, lastExitCode: null));
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        await supervisor.EnsureRunningAsync(Ct);
        Assert.True(SpinWait.SpinUntil(
            () => listener.Events.Any(e => e.EventId == DeckleAnytypeSource.EvtBackendRestartsExhausted),
            TimeSpan.FromSeconds(10)));

        EventWrittenEventArgs exhausted = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendRestartsExhausted);
        Assert.Equal("False", Field(exhausted, "exit_known"));
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ABackendThatStartsAndDiesAtOnceIsStoppedByTheSameBound()
    {
        // Every restart "succeeds", so no failed attempt ever counts: only the
        // deaths before the stable uptime can end the loop.
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        using var listener = new TestEventListener("Deckle-Anytype");
        var results = Enumerable.Range(0, 9)
            .Select(i => new BackendReconciliationResult(
                BackendStartOutcome.Started, Dead(20 + i), $"start-{i}", "spawned"))
            .ToArray();
        var reconciler = new SequenceBackendReconciler(results);
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        await supervisor.EnsureRunningAsync(Ct);
        Assert.True(SpinWait.SpinUntil(
            () => listener.Events.Any(e => e.EventId == DeckleAnytypeSource.EvtBackendRestartsExhausted),
            TimeSpan.FromSeconds(10)));
        await supervisor.DisposeAsync();

        Assert.Equal(BackendSupervisor.MaxConsecutiveFailures, reconciler.Calls);
        Assert.True(IsOpen);
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
    }

    [Fact]
    public async Task AStableRunResetsTheCountOfFailures()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        using var listener = new TestEventListener("Deckle-Anytype");
        var stable = TimeSpan.FromMinutes(10);
        var serving = new FakeBackendProcess(99, Path.GetFullPath("anytype.exe"));
        // Seven early deaths, one that lived ten minutes, then early deaths again:
        // without the reset the ninth start would never be reached.
        var results = Enumerable.Range(0, 7)
            .Select(i => Dead(30 + i))
            .Append(Dead(40, stable))
            .Concat(Enumerable.Range(0, 3).Select(i => Dead(50 + i)))
            .Select((p, i) => new BackendReconciliationResult(BackendStartOutcome.Started, p, $"s-{i}", "spawned"))
            .Append(new(BackendStartOutcome.Started, serving, "serving", "spawned"))
            .ToArray();
        var reconciler = new SequenceBackendReconciler(results);
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        await supervisor.EnsureRunningAsync(Ct);
        Assert.True(SpinWait.SpinUntil(() => reconciler.Calls == results.Length, TimeSpan.FromSeconds(10)));
        await supervisor.DisposeAsync();

        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendRestartsExhausted);
        Assert.False(IsOpen);
    }

    [Fact]
    public async Task APersonsGestureAfterTheBoundRunsTheLadderAgainAndClosesTheIncident()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        using var listener = new TestEventListener("Deckle-Anytype");
        var serving = new FakeBackendProcess(60, Path.GetFullPath("anytype.exe"));
        var reconciler = new SequenceBackendReconciler(
            [.. OneStartThenRejections(7), new(BackendStartOutcome.Started, serving, "back", "spawned")]);
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        Assert.False(supervisor.Resume());
        await supervisor.EnsureRunningAsync(Ct);
        // The reason is the last thing the bound writes, after the flag Resume reads.
        Assert.True(SpinWait.SpinUntil(() => IsExhausted, TimeSpan.FromSeconds(10)));

        Assert.True(supervisor.Resume());

        Assert.True(SpinWait.SpinUntil(() => !IsOpen, TimeSpan.FromSeconds(10)));
        Assert.Equal(9, reconciler.Calls);
    }

    [Fact]
    public async Task ASupervisionThatEndsOnAMissingBackendOrABlockedPortSaysSoInTheLog()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        foreach ((BackendStartOutcome outcome, string reason) in new[]
        {
            (BackendStartOutcome.NotProvisioned, "not_provisioned"),
            (BackendStartOutcome.EndpointConflict, "endpoint_conflict"),
        })
        {
            using var listener = new TestEventListener("Deckle-Anytype");
            var reconciler = new SequenceBackendReconciler(
                new BackendReconciliationResult(outcome, null, "start-id", "decision"));
            await using var supervisor = new BackendSupervisor(
                reconciler, new FakeBackendHealth(), new ControlledBackendTime());

            await supervisor.EnsureRunningAsync(Ct);

            EventWrittenEventArgs gaveUp = Assert.Single(
                listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendSupervisionGaveUp);
            Assert.Equal(reason, Field(gaveUp, "reason"));
            Assert.Equal("start-id", Field(gaveUp, "work_id"));
            UserIncidentRegistry.Instance.Close(UserIncidents.AnytypeBackend);
        }
    }

    [Fact]
    public async Task AStopWhoseStartTimeIsUnreadableDoesNotInventAnUptime()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        var process = new FakeBackendProcess(5, Path.GetFullPath("anytype.exe")) { StartedAt = null };
        process.Exit(1);

        BackendExit exit = BackendProcessReport.Stopped(process, "work");

        Assert.Null(exit.UptimeSeconds);
        EventWrittenEventArgs stopped = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendStopped);
        Assert.Equal("False", Field(stopped, "uptime_known"));
        Assert.Equal("0", Field(stopped, "uptime_s"));
    }
}
