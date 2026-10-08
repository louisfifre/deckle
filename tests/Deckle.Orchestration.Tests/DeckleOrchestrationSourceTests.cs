using System.Diagnostics.Tracing;
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
            Only(listener, DeckleOrchestrationSource.EvtSessionStarted, DeckleOrchestrationSource.EvtSessionStartedDetail),
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtSessionStarted, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
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

        OrchestrationRuntime.LogSessionEnded(entry);

        Assert.Collection(
            Only(listener, DeckleOrchestrationSource.EvtSessionEnded, DeckleOrchestrationSource.EvtSessionEndedDetail),
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtSessionEnded, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
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
                Assert.Equal(DeckleOrchestrationSource.EvtProcessTraceUnavailable, e.EventId);
                Assert.Equal(EventLevel.Warning, e.Level);
            },
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtProcessTraceUnavailableDetail, e.EventId);
                Assert.Equal(EventLevel.Verbose, e.Level);
                Assert.Equal(nameof(UnauthorizedAccessException), e.Payload![0]);
            },
            e => Assert.Equal(DeckleOrchestrationSource.EvtProcessTraceUnavailableDetail, e.EventId));
    }

    [Fact]
    public void ExitWatchRefusalIsAWarningThenItsVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Orchestration");

        ProcessExitWatch.LogUnavailable(pid: 4242, win32Error: 5, firstTime: true);

        Assert.Collection(
            Only(listener, DeckleOrchestrationSource.EvtExitWatchUnavailable, DeckleOrchestrationSource.EvtExitWatchUnavailableDetail),
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtExitWatchUnavailable, e.EventId);
                Assert.Equal(EventLevel.Warning, e.Level);
            },
            e =>
            {
                Assert.Equal(DeckleOrchestrationSource.EvtExitWatchUnavailableDetail, e.EventId);
                Assert.Equal(EventLevel.Verbose, e.Level);
                Assert.Equal(4242, e.Payload![0]);
                Assert.Equal(5, e.Payload[1]);
            });
    }
    // Other test classes of this project run in parallel and emit on the same
    // provider; each test asserts only on the events it is about.
    private static IReadOnlyList<EventWrittenEventArgs> Only(TestEventListener listener, params int[] ids) =>
        listener.Events.Where(e => ids.Contains(e.EventId)).ToList();
}
