using System.Diagnostics.Tracing;
using System.Reflection;
using Xunit;

namespace Deckle.Contributions.Tests;

// An EventSource with a duplicate id cannot build its event table, so the
// whole provider goes silent. This test reads the [Event] attributes the
// runtime reads and names the clash.
[Trait("Category", "regression")]
public sealed class DeckleContributionsSourceEventIdTests
{
    [Fact]
    public void EveryContributionsEventHasItsOwnId()
    {
        var clashes = typeof(DeckleContributionsSource)
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
