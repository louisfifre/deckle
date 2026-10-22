using System.Diagnostics.Tracing;
using Deckle.Anytype;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// A backend that will not come back is one incident in the journal: the death that
// opens it, one mark when the streak passes the threshold, the recovery that closes
// it. Each rung of the restart ladder stays Verbose, whatever number of them it takes.
[Trait("Category", "observability")]
public sealed class BackendRestartIncidentTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    // What the always-on journal keeps: every Warning and Error, and the events
    // that carry the Journal tag.
    private static bool TravelsInTheJournal(EventWrittenEventArgs e)
        => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Journal) != 0;

    private static BackendReconciliationResult Rejected(string id)
        => new(BackendStartOutcome.StartRejected, null, id, "start-rejected");

    [Fact]
    [Trait("Category", "regression")]
    public async Task ABackendThatRejectsTenRestartsLeavesOneIncidentInTheJournal()
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

        var journal = listener.Events.Where(TravelsInTheJournal).ToArray();
        var stopped = Assert.Single(journal, e => e.EventId == DeckleAnytypeSource.EvtBackendStopped);
        var failing = Assert.Single(journal, e => e.EventId == DeckleAnytypeSource.EvtBackendRestartsFailing);
        var recovered = Assert.Single(journal, e => e.EventId == DeckleAnytypeSource.EvtBackendRecovered);

        // One id runs from the death to the recovery.
        string incident = Field(stopped, "work_id");
        Assert.NotEmpty(incident);
        Assert.Equal(incident, Field(failing, "work_id"));
        Assert.Equal(incident, Field(recovered, "work_id"));
        Assert.Equal("11", Field(recovered, "attempts"));

        // Nothing else of the ladder travels: ten rungs are not ten journal lines.
        Assert.Equal(3, journal.Length);
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
    public async Task ARestartAttemptWhoseServeDiesAgainWritesNoJournalLine()
    {
        using var listener = new TestEventListener("Deckle-Anytype");

        await ReconcileADyingServe(BackendTriggers.ProcessExit);

        Assert.DoesNotContain(listener.Events, TravelsInTheJournal);
        Assert.Single(listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendRestartAttemptStarting);
        Assert.Single(listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendRestartAttemptStopped);
    }

    [Fact]
    public async Task AFirstStartWhoseServeDiesPairsItsStartAndItsStopInTheJournal()
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
            e => Assert.True(TravelsInTheJournal(e)));
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
