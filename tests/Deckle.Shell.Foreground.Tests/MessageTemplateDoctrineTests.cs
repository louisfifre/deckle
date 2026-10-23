using Deckle.TestSupport;
using Xunit;

namespace Deckle.Shell.Foreground.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleShellForegroundSource.ForegroundChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleShellForegroundSource.HookFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellForegroundSource.HookFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellForegroundSource.HookRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleShellForegroundSource.WatcherStartFailed", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleShellForegroundSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
