using Deckle.TestSupport;
using Xunit;

namespace Deckle.App.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that cannot, each
// with its reason; an entry that outlives its breach fails the test too.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleAppSource.ModuleSkipped", "the module is the subject of the startup step, so the sentence names it with its reason" },
        { "DeckleAppSource.ModuleStarted", "the module is the subject of the startup step, so the sentence names it" },
        { "DeckleAppSource.ModuleStarting", "the module is the subject of the startup step, so the sentence names it" },
        { "DeckleAppSource.RunIdentity", "the run identity line exists to show the version, build and session it names" },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAppSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
