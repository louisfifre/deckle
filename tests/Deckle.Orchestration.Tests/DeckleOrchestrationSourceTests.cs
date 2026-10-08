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
            listener.Events,
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
}
