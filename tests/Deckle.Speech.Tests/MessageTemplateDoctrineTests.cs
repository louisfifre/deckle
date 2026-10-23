using Deckle.TestSupport;
using Xunit;

namespace Deckle.Speech.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleSpeechSource.ReadAloudCompleteDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.ReadAloudEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.ReadAloudRequestedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.SettingsLoadComplete", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.SettingsLoaded", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.SpeechEngineStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.SpeechPlaybackFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.SpeechSynthesisFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSpeechSource.StubSynthesis", TemplateDoctrine.VerboseWithoutPayload },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleSpeechSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
