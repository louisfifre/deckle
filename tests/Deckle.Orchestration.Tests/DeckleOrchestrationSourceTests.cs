using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Orchestration;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "observability")]
public sealed class DeckleOrchestrationSourceTests
{
    [Fact]
    public void AddingAnEntryEmitsTheMilestoneThenItsVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Orchestration");
        var entry = new AppEntry
        {
            DisplayName = "Hollow",
            ProcessName = "Hollow",
            Source = AppSource.Manual,
        };

        OrchestrationSettingsService.LogAdded(entry);

        Assert.Collection(
            Only(listener, DeckleOrchestrationSource.EvtEntryAdded, DeckleOrchestrationSource.EvtEntryAddedDetail),
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtEntryAdded, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
            },
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtEntryAddedDetail, e.EventId);
                Assert.Equal(EventLevel.Verbose, e.Level);
                Assert.Equal(entry.Id, e.Payload![0]);
                Assert.Equal("Manual", e.Payload[2]);
            });
    }

    [Fact]
    public void SessionStartEmitsTheMilestoneThenItsVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Orchestration");
        var entry = new AppEntry { DisplayName = "Hollow", ProcessName = "Hollow" };

        OrchestrationRuntime.LogSessionStarted(entry, pid: 4242);

        Assert.Collection(
            ForEntry(listener, entry, DeckleOrchestrationSource.EvtSessionStarted, DeckleOrchestrationSource.EvtSessionStartedDetail),
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtSessionStarted, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
                Assert.True(TravelsInTheJournal(e));
                Assert.Equal(entry.Id, e.Payload![0]);
                Assert.Equal(4242, e.Payload[1]);
            },
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtSessionStartedDetail, e.EventId);
                Assert.Equal(EventLevel.Verbose, e.Level);
                Assert.Equal(entry.Id, e.Payload![0]);
                Assert.Equal("Hollow", e.Payload[2]);
                Assert.Equal(4242, e.Payload[3]);
            });
    }

    [Fact]
    public void SessionEndEmitsTheMilestoneThenItsVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Orchestration");
        var entry = new AppEntry { DisplayName = "Hollow", ProcessName = "Hollow" };

        OrchestrationRuntime.LogSessionEnded(entry, SessionEndReason.EntryDisabled);

        Assert.Collection(
            ForEntry(listener, entry, DeckleOrchestrationSource.EvtSessionEnded, DeckleOrchestrationSource.EvtSessionEndedDetail),
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtSessionEnded, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
                Assert.True(TravelsInTheJournal(e));
                Assert.Equal(entry.Id, e.Payload![0]);
                Assert.Equal("entry_disabled", e.Payload[1]);
            },
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtSessionEndedDetail, e.EventId);
                Assert.Equal(EventLevel.Verbose, e.Level);
                Assert.Equal(entry.Id, e.Payload![0]);
            });
    }

    // The refusal is a Warning the first time, then only its Verbose detail:
    // one incident per process lifetime, not one Warning per reconcile.
    [Fact]
    public void ProcessTraceRefusalIsOneWarningThenDetailsOnly()
    {
        using var listener = new TestEventListener("Deckle-Orchestration");
        var refusal = new UnauthorizedAccessException("Access denied");

        WmiProcessStartSource.LogUnavailable(refusal, firstTime: true);
        WmiProcessStartSource.LogUnavailable(refusal, firstTime: false);

        Assert.Collection(
            Only(listener, DeckleOrchestrationSource.EvtProcessTraceUnavailable, DeckleOrchestrationSource.EvtProcessTraceUnavailableDetail),
            e =>
            {
                // The Warning carries its own cause: a journal without Verbose
                // must still say why start events are missing.
                Assert.Equal(DeckleOrchestrationSource.EvtProcessTraceUnavailable, e.EventId);
                Assert.Equal(EventLevel.Warning, e.Level);
                Assert.Equal(nameof(UnauthorizedAccessException), e.Payload![1]);
                Assert.Equal(refusal.HResult, e.Payload[3]);
            },
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtProcessTraceUnavailableDetail, e.EventId);
                Assert.Equal(EventLevel.Verbose, e.Level);
                Assert.Equal(nameof(UnauthorizedAccessException), e.Payload![0]);
            });
    }

    [Fact]
    public void ExitWatchRefusalIsOneWarningThenDetailsOnly()
    {
        using var listener = new TestEventListener("Deckle-Orchestration");

        ProcessExitWatch.LogUnavailable(pid: 4242, win32Error: 5, firstTime: true);
        ProcessExitWatch.LogUnavailable(pid: 4243, win32Error: 5, firstTime: false);

        Assert.Collection(
            Only(listener, DeckleOrchestrationSource.EvtExitWatchUnavailable, DeckleOrchestrationSource.EvtExitWatchUnavailableDetail),
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtExitWatchUnavailable, e.EventId);
                Assert.Equal(EventLevel.Warning, e.Level);
                Assert.Equal(4242, e.Payload![0]);
                Assert.Equal(5, e.Payload[1]);
            },
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtExitWatchUnavailableDetail, e.EventId);
                Assert.Equal(EventLevel.Verbose, e.Level);
                Assert.Equal(4243, e.Payload![0]);
            });
    }

    [Fact]
    public void AppliedRulesEmitTheMilestoneThenItsVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Orchestration");
        var entry = new AppEntry { DisplayName = "Hollow", ProcessName = "Hollow" };
        var effect = new ProfileTransactionTests.FakeEffect("hdr", []);

        ProfileTransaction.Apply(entry, [effect]);

        Assert.Collection(
            ForEntry(listener, entry, DeckleOrchestrationSource.EvtProfileApplied, DeckleOrchestrationSource.EvtProfileAppliedDetail),
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtProfileApplied, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
                Assert.True(TravelsInTheJournal(e));
                Assert.Equal(entry.Id, e.Payload![0]);
                Assert.Equal(1, e.Payload[1]);
            },
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtProfileAppliedDetail, e.EventId);
                Assert.Equal(EventLevel.Verbose, e.Level);
                Assert.Equal("hdr", e.Payload![1]);
                Assert.Equal(1, e.Payload[2]);
            });
    }

    [Fact]
    public void AFailedRuleIsOneWarningCarryingItsCause()
    {
        using var listener = new TestEventListener("Deckle-Orchestration");
        var entry = new AppEntry { DisplayName = "Hollow", ProcessName = "Hollow" };
        var effect = new ProfileTransactionTests.FakeEffect("hdr", []) { ApplyThrows = true };

        ProfileTransaction.Apply(entry, [effect]);

        EventWrittenEventArgs failed = Assert.Single(
            Only(listener, DeckleOrchestrationSource.EvtRuleApplyFailed),
            e => Equals(e.Payload?[0], entry.Id));
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.Equal("hdr", failed.Payload![1]);
        Assert.Equal(nameof(InvalidOperationException), failed.Payload[2]);
    }

    [Fact]
    public void APartialRestoreReportsHowManyOfTheTotalCameBack()
    {
        using var listener = new TestEventListener("Deckle-Orchestration");
        var entry = new AppEntry { DisplayName = "Hollow", ProcessName = "Hollow" };
        var effects = new[]
        {
            new ProfileTransactionTests.FakeEffect("a", []),
            new ProfileTransactionTests.FakeEffect("b", []) { RestoreThrows = true },
        };

        ProfileTransaction.Apply(entry, effects).Restore();

        EventWrittenEventArgs detail = Assert.Single(
            Only(listener, DeckleOrchestrationSource.EvtProfileRestoredDetail),
            e => Equals(e.Payload?[0], entry.Id));
        Assert.Equal(1, detail.Payload![1]);
        Assert.Equal(2, detail.Payload[2]);
        Assert.Single(
            Only(listener, DeckleOrchestrationSource.EvtRuleRestoreFailed),
            e => Equals(e.Payload?[0], entry.Id));
    }

    // Events from other tests share the provider and the event ids; the milestone
    // and its detail both name the entry they are about, so each is kept by it.
    private static IReadOnlyList<EventWrittenEventArgs> ForEntry(
        TestEventListener listener, AppEntry entry, int milestoneId, int detailId) =>
        listener.Events
            .Where(e => (e.EventId == milestoneId || e.EventId == detailId)
                && Equals(e.Payload?[0], entry.Id))
            .ToList();

    private static bool TravelsInTheJournal(EventWrittenEventArgs e) =>
        e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Journal) != 0;

    // Other test classes of this project run in parallel and emit on the same
    // provider; each test asserts only on the events it is about.
    private static IReadOnlyList<EventWrittenEventArgs> Only(TestEventListener listener, params int[] ids) =>
        listener.Events.Where(e => ids.Contains(e.EventId)).ToList();
}
