using Deckle.TestSupport;
using Xunit;

namespace Deckle.Mail.Hub.Tests;

// Every template of the Mail window's provider follows the shape of its
// level, as TemplateDoctrine states it.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleMailHubSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
