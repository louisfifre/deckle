using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Tests;

// The paste events describe the window the text goes to. That description names
// the program the person is using, so it stays on Verbose detail events and
// never on a line the journal keeps.
[Trait("Category", "observability")]
public sealed class TranscriptionAppNameTests
{
    [Fact]
    public void NoEventThatTravelsInTheJournalDeclaresAnAppOrProcessName()
    {
        Assert.Empty(JournalledPayloads.AppNamesInTheJournal(typeof(DeckleWhispSource)));
    }

    [Fact]
    public void TheForegroundDescriptorStaysOnVerboseDetailEventsThatTheJournalDoesNotKeep()
    {
        var carrying = JournalledPayloads
            .Carrying(typeof(DeckleWhispSource), JournalledPayloads.AppNameFields)
            .Select(c => c.Event)
            .DistinctBy(e => e.Name)
            .ToArray();

        Assert.Contains(nameof(DeckleWhispSource.PasteForeground), carrying.Select(e => e.Name));
        Assert.Contains(nameof(DeckleWhispSource.PasteSent), carrying.Select(e => e.Name));
        Assert.All(carrying, e =>
        {
            Assert.Equal(EventLevel.Verbose, e.Level);
            Assert.False(JournalledPayloads.Travels(e), $"{e.Name} travels in the journal.");
        });
    }
}
