using Deckle.TestSupport;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleLlmSource.BatchRewriteFellBack", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.BatchRewriteReturnedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.BatchRewriteSentDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.EndpointNonLoopbackHostDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.EndpointRefreshFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.EndpointSchemeNotAllowedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ListModelsInvalidJson", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ListModelsInvalidJsonDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ManualRefreshFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.OllamaRefreshSkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.PageNavigatedToFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ParagraphOfferSettled", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ParagraphRewriteFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ProviderKeyReadFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ProviderListModelsFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.PsProbeBusyDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.PsProbeEmpty", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleLlmSource.PsProbeErrorDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.PsProbeFaulted", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.PsProbeRejectedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ResetAllFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteAttemptFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteCancelled", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteResponseDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteSkippedNoModelDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteSkippedNoProvider", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteSkippedNoProviderDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteTimeout", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.RewriteUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.SettingsLoadComplete", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.SettingsLoaded", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ShowModelInvalidJson", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.ShowModelInvalidJsonDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.TranscriptRewriteCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.TranscriptRewriteStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleLlmSource.UserFeedbackEmitted", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleLlmSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
