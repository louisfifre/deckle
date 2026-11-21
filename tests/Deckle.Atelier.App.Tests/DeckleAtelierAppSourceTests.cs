using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Atelier.App.Tests;

// An EventSource with a duplicate id cannot build its event table, so the
// whole provider goes silent. This test reads the [Event] attributes the
// runtime reads and names the clash.
[Trait("Category", "regression")]
public sealed class DeckleAtelierAppSourceEventIdTests
{
    [Fact]
    public void EveryAtelierAppEventHasItsOwnId()
    {
        var clashes = typeof(DeckleAtelierAppSource)
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

// Every template of the app's provider follows the shape of its level, as
// TemplateDoctrine states it.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAtelierAppSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
