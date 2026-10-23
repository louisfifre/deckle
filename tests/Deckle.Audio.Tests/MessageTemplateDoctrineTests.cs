using Deckle.TestSupport;
using Xunit;

namespace Deckle.Audio.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that cannot, each
// with its reason; an entry that outlives its breach fails the test too.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAudioSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
