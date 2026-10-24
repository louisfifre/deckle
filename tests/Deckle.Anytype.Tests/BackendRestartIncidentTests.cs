using System.Diagnostics.Tracing;
using Deckle.Anytype;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// A backend that will not come back is one incident in the log: the death that
// opens it, one mark when the streak passes the threshold, the recovery that closes
// it. Each rung of the restart ladder stays Verbose, whatever number of them it takes.
[Trait("Category", "observability")]
public sealed class BackendRestartIncidentTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    // What the always-on log keeps: every Warning and Error, and the events
    // that carry the Milestone tag.
    private static bool TravelsInTheLog(EventWrittenEventArgs e)
        => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Milestone) != 0;

    private static BackendReconciliationResult Rejected(string id)
        => new(BackendStartOutcome.StartRejected, null, id, "start-rejected");

    [Fact]
    [Trait("Category", "regression")]
    public async Task ABackendThatRejectsTenRestartsLeavesOneIncidentInTheLog()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        string executable = Path.GetFullPath("anytype.exe");
        var first = new FakeBackendProcess(17, executable);
        var second = new FakeBackendProcess(18, executable);
        var results = new List<BackendReconciliationResult>
        {
            new(BackendStartOutcome.Started, first, "start", "spawned"),
        };
        for (int i = 0; i < 10; i++) results.Add(Rejected($"rejected-{i}"));
        results.Add(new(BackendStartOutcome.Started, second, "back", "spawned"));
        var reconciler = new SequenceBackendReconciler(results.ToArray());
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        Assert.Equal(BackendStartOutcome.Started, await supervisor.EnsureRunningAsync(Ct));
        first.Exit(1);
        Assert.True(
            SpinWait.SpinUntil(
                () => listener.Events.Any(e => e.EventId == DeckleAnytypeSource.EvtBackendRecovered),
                TimeSpan.FromSeconds(10)));

        var logged = listener.Events.Where(TravelsInTheLog).ToArray();
        var stopped = Assert.Single(logged, e => e.EventId == DeckleAnytypeSource.EvtBackendStopped);
        var failing = Assert.Single(logged, e => e.EventId == DeckleAnytypeSource.EvtBackendRestartsFailing);
        var recovered = Assert.Single(logged, e => e.EventId == DeckleAnytypeSource.EvtBackendRecovered);

        // One id runs from the death to the recovery.
        string incident = Field(stopped, "work_id");
        Assert.NotEmpty(incident);
        Assert.Equal(incident, Field(failing, "work_id"));
        Assert.Equal(incident, Field(recovered, "work_id"));
        Assert.Equal("11", Field(recovered, "attempts"));

        // Nothing else of the ladder travels: ten rungs are not ten log lines.
        Assert.Equal(3, logged.Length);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ADeathThatEndsInAGiveUpClosesTheIncidentWithItsId()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        string executable = Path.GetFullPath("anytype.exe");
        var first = new FakeBackendProcess(17, executable);
        var reconciler = new SequenceBackendReconciler(
            new(BackendStartOutcome.Started, first, "start", "spawned"),
            Rejected("rejected"),
            new(BackendStartOutcome.NotProvisioned, null, "gone", "not-provisioned"));
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        Assert.Equal(BackendStartOutcome.Started, await supervisor.EnsureRunningAsync(Ct));
        first.Exit(1);
        Assert.True(
            SpinWait.SpinUntil(
                () => listener.Events.Any(e => e.EventId == DeckleAnytypeSource.EvtBackendSupervisionGaveUp),
                TimeSpan.FromSeconds(10)));

        var stopped = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendStopped);
        var gaveUp = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendSupervisionGaveUp);
        Assert.Equal(Field(stopped, "work_id"), Field(gaveUp, "work_id"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ARestartAttemptWhoseServeDiesAgainWritesNoLogLine()
    {
        using var listener = new TestEventListener("Deckle-Anytype");

        await ReconcileADyingServe(BackendTriggers.ProcessExit);

        Assert.DoesNotContain(listener.Events, TravelsInTheLog);
        Assert.Single(listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendRestartAttemptStarting);
        Assert.Single(listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendRestartAttemptStopped);
    }

    [Fact]
    public async Task AFirstStartWhoseServeDiesPairsItsStartAndItsStopInTheLog()
    {
        using var listener = new TestEventListener("Deckle-Anytype");

        await ReconcileADyingServe(BackendTriggers.Startup);

        var starting = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendStarting);
        var stopped = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendStopped);
        Assert.NotEmpty(Field(starting, "work_id"));
        Assert.Equal(Field(starting, "work_id"), Field(stopped, "work_id"));
        Assert.All(
            listener.Events.Where(e => e.EventId is DeckleAnytypeSource.EvtBackendStarting
                or DeckleAnytypeSource.EvtBackendStopped),
            e => Assert.True(TravelsInTheLog(e)));
    }

    private static BackendReconciliationResult Unready(int pid, BackendReplacement? replacement = null)
        => new(BackendStartOutcome.TimedOut, null, "wait", "timed-out", pid, replacement);

    // Eight waits on pid 52 (the startup and seven attempts) put the controlled
    // clock at 322 s, past the five minutes of grace; the ninth call is asked to
    // replace it.
    private static IEnumerable<BackendReconciliationResult> EightWaits()
        => Enumerable.Range(0, 8).Select(_ => Unready(52));

    [Fact]
    [Trait("Category", "regression")]
    public async Task AWedgedServeThatIsEndedLeavesOneWarningPairedWithTheIncident()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        var serving = new FakeBackendProcess(60, Path.GetFullPath("anytype.exe"));
        var reconciler = new SequenceBackendReconciler(
            [.. EightWaits(), new(BackendStartOutcome.Started, serving, "back", "spawned",
                Replacement: new BackendReplacement(BackendReplaceOutcome.Ended))]);
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        await supervisor.EnsureRunningAsync(Ct);
        Assert.True(
            SpinWait.SpinUntil(
                () => listener.Events.Any(e => e.EventId == DeckleAnytypeSource.EvtBackendRecovered),
                TimeSpan.FromSeconds(10)));

        var ended = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendWedgedServeEnded);
        Assert.True(TravelsInTheLog(ended));
        Assert.Equal("52", Field(ended, "pid"));
        Assert.Equal("322", Field(ended, "unready_s"));
        // The id of the start that opened the incident rides to its recovery.
        var recovered = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendRecovered);
        Assert.Equal("wait", Field(ended, "work_id"));
        Assert.Equal(Field(ended, "work_id"), Field(recovered, "work_id"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AnEndingThatIsRefusedIsLoggedOnceAndNeverAskedAgainForThatPid()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        var serving = new FakeBackendProcess(60, Path.GetFullPath("anytype.exe"));
        var reconciler = new SequenceBackendReconciler(
            [
                .. EightWaits(),
                Unready(52, new BackendReplacement(
                    BackendReplaceOutcome.Refused, "Win32Exception", "Access is denied.")),
                Unready(52), Unready(52), Unready(52),
                new(BackendStartOutcome.Started, serving, "back", "spawned"),
            ]);
        await using var supervisor = new BackendSupervisor(
            reconciler, new FakeBackendHealth(), new ControlledBackendTime());

        await supervisor.EnsureRunningAsync(Ct);
        Assert.True(
            SpinWait.SpinUntil(
                () => listener.Events.Any(e => e.EventId == DeckleAnytypeSource.EvtBackendRecovered),
                TimeSpan.FromSeconds(10)));

        Assert.Equal(1, reconciler.ReplaceRequests.Count(pid => pid != 0));
        var refused = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendWedgedServeEndRefused);
        Assert.True(TravelsInTheLog(refused));
        Assert.Equal("52", Field(refused, "pid"));
        Assert.Equal("Win32Exception", Field(refused, "ex_type"));
        Assert.Equal("Access is denied.", Field(refused, "ex_message"));
        Assert.Equal("wait", Field(refused, "work_id"));
        Assert.DoesNotContain(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendWedgedServeEnded);
    }

    // The identity re-check is an everyday no, not an event worth keeping: it
    // stays Verbose, outside the always-on log, and says why without a path.
    [Fact]
    public async Task AReplaceRequestTheReconcilerDeclinesStaysVerboseAndCarriesNoPath()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        string executable = Path.GetFullPath("anytype.exe");
        var stranger = new FakeBackendProcess(52, Path.GetFullPath("other.exe"));
        var processes = new FakeBackendProcessHost();
        processes.Opened.Add(stranger.Id, stranger);
        var reconciler = new BackendReconciler(
            new FakeBackendProvider(executable), processes, new ScriptedBackendListener(),
            new FakeBackendHealth(), new ImmediateBackendCoordinator(), new ControlledBackendTime());

        await reconciler.ReconcileAsync(BackendTriggers.StartFailed, Ct, replaceProcessId: stranger.Id);

        var declined = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendWedgedServeEndDeclined);
        Assert.False(TravelsInTheLog(declined));
        Assert.Equal("not_trusted", Field(declined, "reason"));
        Assert.Equal("52", Field(declined, "pid"));
        Assert.DoesNotContain("other.exe", declined.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ReconcileADyingServe(string trigger)
    {
        string executable = Path.GetFullPath("anytype.exe");
        var processes = new FakeBackendProcessHost
        {
            SpawnResult = new FakeBackendProcess(88, executable) { HasExited = true, ExitCode = 1 },
        };
        var reconciler = new BackendReconciler(
            new FakeBackendProvider(executable),
            processes,
            new ScriptedBackendListener(),
            new FakeBackendHealth(),
            new ImmediateBackendCoordinator(),
            new ControlledBackendTime());

        BackendReconciliationResult result = await reconciler.ReconcileAsync(trigger, Ct);

        Assert.Equal(BackendStartOutcome.StartRejected, result.Outcome);
    }
}
