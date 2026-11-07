using System.Diagnostics.Tracing;
using System.Reflection;
using Xunit;

namespace Deckle.Transcription.Tests;

// Two branches each took event ids 255 and 256 for their new events; git merged
// the constants without a conflict because they sit on different lines. An
// EventSource with a duplicate id cannot build its event table, so the whole
// Deckle-Whisp provider went silent: every event came out as one
// EventSourceMessage and 33 unrelated observability tests failed at once.
// This test reads the [Event] attributes the runtime reads and names the clash.
//
// EventSource.GenerateManifest is not used: even without options it rejects
// keywords declared outside the source type, which Deckle does on purpose
// (Deckle.Diagnostics.Keywords).
[Trait("Category", "regression")]
public sealed class DeckleWhispSourceEventIdTests
{
    [Fact]
    public void EveryWhispEventHasItsOwnId()
    {
        var clashes = typeof(DeckleWhispSource)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => (Method: m.Name, Attribute: m.GetCustomAttribute<EventAttribute>()))
            .Where(e => e.Attribute is not null)
            .GroupBy(e => e.Attribute!.EventId)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(e => e.Method))}")
            .ToArray();

        Assert.Empty(clashes);
    }
}
