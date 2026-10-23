using Deckle.TestSupport;
using Xunit;

namespace Deckle.Modules.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleModulesSource.ModuleRegistered", TemplateDoctrine.PreDoctrine },
        { "DeckleModulesSource.PresenceLoadedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleModulesSource.PresenceLoadFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleModulesSource.PresenceSavedDetail", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleModulesSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
