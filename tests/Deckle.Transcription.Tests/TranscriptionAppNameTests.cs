using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Tests;

// An application or process name names the program the person is using, so it
// stays on Verbose detail events and never on a line the log keeps.
[Trait("Category", "observability")]
public sealed class TranscriptionAppNameTests
{
    [Fact]
    public void NoEventThatTravelsInTheLogDeclaresAnAppOrProcessName()
    {
        Assert.Empty(LoggedPayloads.AppNamesInTheLog(typeof(DeckleWhispSource)));
    }

    [Fact]
    public void AnAppNameStaysOnVerboseDetailEventsThatTheLogDoesNotKeep()
    {
        var carrying = LoggedPayloads
            .Carrying(typeof(DeckleWhispSource), LoggedPayloads.AppNameFields)
            .Select(c => c.Event)
            .DistinctBy(e => e.Name)
            .ToArray();

        Assert.All(carrying, e =>
        {
            Assert.Equal(EventLevel.Verbose, e.Level);
            Assert.False(LoggedPayloads.Travels(e), $"{e.Name} travels in the log.");
        });
    }
}
