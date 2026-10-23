using Deckle.TestSupport;
using Xunit;

namespace Deckle.Vad.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleVadSource.SpeechTrimBounds", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimmed", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimNotReady", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleVadSource.SpeechTrimRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimSettingsSnapshot", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimUnavailableDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimVadDownloadAttemptFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimVadDownloadCompleteDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimVadDownloadStartDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimVadFileDeleteFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimVadLoadComplete", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.SpeechTrimVadLoadedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVadSource.UtteranceDroppedNoSpeech", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleVadSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
