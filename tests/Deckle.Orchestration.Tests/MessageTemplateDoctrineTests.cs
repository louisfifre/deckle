using Deckle.TestSupport;
using Xunit;

namespace Deckle.Orchestration.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleOrchestrationSource.DetectionStatusChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.DisplayQueryFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.DisplayQueryFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.EntryAddedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.EntryRemovedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ExitWatchCallbackFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ExitWatchCallbackFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ExitWatchUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ExitWatchUnavailableDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ForegroundDeliveryFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ForegroundDeliveryFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ForegroundPathUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.HdrRuleSkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.HdrSetFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.HdrStateChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.PageActionFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProcessQuerySkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProcessTraceEventDropped", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProcessTraceEventDroppedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProcessTraceLost", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProcessTraceStopFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProcessTraceUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProcessTraceUnavailableDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProfileApplied", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProfileAppliedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProfileRestored", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ProfileRestoredDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ReconcileDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ReconcileFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ReconcileFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.RuleApplyFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.RuleRestoreFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.RuntimeStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ScanCompletedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ScanFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ScanFolderMissing", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ScanPathSkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.ScanStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.SessionEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.SessionEndedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.SessionHandlerFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.SessionStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.SessionStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.SteamLibraryScanned", TemplateDoctrine.PreDoctrine },
        { "DeckleOrchestrationSource.SteamNotFound", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleOrchestrationSource.SteamPathUnreadable", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleOrchestrationSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
