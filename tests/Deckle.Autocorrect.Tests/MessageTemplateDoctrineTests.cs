using Deckle.TestSupport;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleAutocorrectSource.ActivityRollup", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.AutocorrectDecisionRecorded", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.AutocorrectRerankRecorded", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.AutocorrectStreamRecorded", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.AutocorrectTextRecorded", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.CaretRecoveryFailing", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.CaretRecoveryRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.CaretSentenceRecovery", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.CorrectionApplied", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleAutocorrectSource.CorrectionDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.DomainPackManifestUnreadable", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.DomainPackMerged", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.EngineStartFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.EnrollmentSuggested", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.ExternalMutationBurst", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.InjectionEpisodeDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.InjectionFailing", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.LearningSignal", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.LearningStoreLoaded", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.LearningStoreUnreadable", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.LexiconLoadComplete", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.PersonalDictionaryReset", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.PersonalDictionarySanitized", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankerLoadFailure", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankerLoadRetried", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankerStatus", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankFailing", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankFailureDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankLaneStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankLaneStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankSlotPending", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankStalled", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankStallRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankSubmitted", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankVerdict", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.RerankWorkerFaulted", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.SentenceJudgeFeatureUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.SentenceJudgeLoaded", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.SentenceStageAbandoned", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.SurfaceChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleAutocorrectSource.SystemLanguagesFallback", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAutocorrectSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
