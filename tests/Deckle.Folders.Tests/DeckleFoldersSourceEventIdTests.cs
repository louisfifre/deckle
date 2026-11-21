using System.Diagnostics.Tracing;
using System.Reflection;
using Xunit;

namespace Deckle.Folders.Tests;

// Two branches each took event ids 50 and 51 for their new events, one in
// DeckleFoldersSource.cs and one in Launching/DeckleFoldersSource.Launching.cs;
// git merged them without a conflict because they sit in different files. An
// EventSource with a duplicate id cannot build its event table, so the whole
// Deckle-Folders provider went silent: every event came out as one
// EventSourceMessage and nine unrelated observability tests failed at once.
// This test reads the [Event] attributes the runtime reads and names the clash.
//
// EventSource.GenerateManifest is not used, for the reason given in
// Deckle.Transcription.Tests/DeckleWhispSourceEventIdTests.cs.
[Trait("Category", "regression")]
public sealed class DeckleFoldersSourceEventIdTests
{
    [Fact]
    public void EveryFoldersEventHasItsOwnId()
    {
        var clashes = typeof(DeckleFoldersSource)
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
