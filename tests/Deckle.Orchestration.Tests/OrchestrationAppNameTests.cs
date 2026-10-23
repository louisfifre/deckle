using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Orchestration.Tests;

// An application or process name is Verbose detail, never part of what the
// journal keeps. The journal outlives the session and says which program the
// person was using, so a name belongs on the detail event that can be switched
// off and nowhere else.
[Trait("Category", "observability")]
public sealed class OrchestrationAppNameTests
{
    private const string Provider = "Deckle-Orchestration";

    [Fact]
    public void NoEventThatTravelsInTheJournalDeclaresAnAppOrProcessName()
    {
        Assert.Empty(JournalledPayloads.AppNamesInTheJournal(typeof(DeckleOrchestrationSource)));
    }

    [Fact]
    public void TheNamesStayOnVerboseDetailEventsThatTheJournalDoesNotKeep()
    {
        var carrying = JournalledPayloads
            .Carrying(typeof(DeckleOrchestrationSource), JournalledPayloads.AppNameFields)
            .Select(c => c.Event)
            .DistinctBy(e => e.Name)
            .ToArray();

        // The detail events the names moved to are still there, and quiet.
        Assert.Contains(nameof(DeckleOrchestrationSource.SessionStartedDetail), carrying.Select(e => e.Name));
        Assert.Contains(nameof(DeckleOrchestrationSource.EntryAddedDetail), carrying.Select(e => e.Name));
        Assert.All(carrying, e =>
        {
            Assert.Equal(EventLevel.Verbose, e.Level);
            Assert.False(JournalledPayloads.Travels(e), $"{e.Name} travels in the journal.");
        });
    }

    [Fact]
    public void AStartedSessionNamesNoAppOnAnyEventTheJournalKeeps()
    {
        using var listener = new TestEventListener(Provider);
        var entry = new AppEntry
        {
            DisplayName = "Probe Display Name 4f1c",
            ProcessName = "probe-process-4f1c",
        };

        OrchestrationRuntime.LogSessionStarted(entry, pid: 4343);
        OrchestrationRuntime.LogSessionEnded(entry, SessionEndReason.EntryDisabled);

        var journalled = listener.Events
            .Where(e => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Journal) != 0)
            .Where(e => Equals(e.Payload?[0], entry.Id))
            .ToArray();
        Assert.NotEmpty(journalled);
        foreach (EventWrittenEventArgs e in journalled)
        {
            foreach (object? value in e.Payload!)
            {
                if (value is string text)
                {
                    Assert.DoesNotContain("4f1c", text);
                }
            }
        }
    }
}
