using Deckle.TestSupport;
using Xunit;

namespace Deckle.Atelier.Tests;

// Every template of this suite's provider follows the shape of its level, as
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
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAtelierSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
