using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Orchestration.Tests;

// An application or process name is Verbose detail, never part of what the
// log keeps. The log outlives the session and says which program the
// person was using, so a name belongs on the detail event that can be switched
// off and nowhere else.
[Trait("Category", "observability")]
public sealed class OrchestrationAppNameTests
{
    private const string Provider = "Deckle-Orchestration";

    [Fact]
    public void NoEventThatTravelsInTheLogDeclaresAnAppOrProcessName()
    {
        Assert.Empty(LoggedPayloads.AppNamesInTheLog(typeof(DeckleOrchestrationSource)));
    }

    [Fact]
    public void TheNamesStayOnVerboseDetailEventsThatTheLogDoesNotKeep()
    {
        var carrying = LoggedPayloads
            .Carrying(typeof(DeckleOrchestrationSource), LoggedPayloads.AppNameFields)
            .Select(c => c.Event)
            .DistinctBy(e => e.Name)
            .ToArray();

        // The detail events the names moved to are still there, and quiet.
        Assert.Contains(nameof(DeckleOrchestrationSource.SessionStartedDetail), carrying.Select(e => e.Name));
        Assert.Contains(nameof(DeckleOrchestrationSource.EntryAddedDetail), carrying.Select(e => e.Name));
        Assert.All(carrying, e =>
        {
            Assert.Equal(EventLevel.Verbose, e.Level);
            Assert.False(LoggedPayloads.Travels(e), $"{e.Name} travels in the log.");
        });
    }

    [Fact]
    public void AStartedSessionNamesNoAppOnAnyEventTheLogKeeps()
    {
        using var listener = new TestEventListener(Provider);
        var entry = new AppEntry
        {
            DisplayName = "Probe Display Name 4f1c",
            ProcessName = "probe-process-4f1c",
        };

        OrchestrationRuntime.LogSessionStarted(entry, pid: 4343);
        OrchestrationRuntime.LogSessionEnded(entry, SessionEndReason.EntryDisabled);

        var logged = listener.Events
            .Where(e => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Milestone) != 0)
            .Where(e => Equals(e.Payload?[0], entry.Id))
            .ToArray();
        Assert.NotEmpty(logged);
        foreach (EventWrittenEventArgs e in logged)
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
