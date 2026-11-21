using System.Diagnostics.Tracing;
using System.Reflection;
using Xunit;

namespace Deckle.FolderMap.Tests;

// Two branches of Deckle Folders each took event ids 50 and 51 once, and git
// merged them without a conflict because they sat in different files. An
// EventSource with a duplicate id cannot build its event table, so the whole
// provider goes silent. This test reads the [Event] attributes the runtime
// reads and names the clash, as DeckleFoldersSourceEventIdTests does for the
// Folders's provider.
[Trait("Category", "regression")]
public sealed class DeckleFolderMapSourceEventIdTests
{
    [Fact]
    public void EveryFolderMapEventHasItsOwnId()
    {
        var clashes = typeof(DeckleFolderMapSource)
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
