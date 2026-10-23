using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleAnytypeSource.AnytypeRuntimeFaulted", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.ApiRefusalsEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.ApiRequestCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.ApiRequestFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.ApiRequestFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.ApiRequestRetriedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.ApiRequestStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.ApiRequestSuperseded", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.ApiTransportRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendEndpointConflict", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendEndpointConflictDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendHealthProbed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendHealthProbeFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendInstallationChecked", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendInstallationPrepareFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendListenerObserved", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendProcessAttached", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendProcessQueryFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendProviderFileOperationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendReady", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendReconciliationCancelled", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendReconciliationCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendReconciliationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendReconciliationLeaseAcquired", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendReconciliationLeaseWaitEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendReconciliationLeaseWaiting", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendReconciliationStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendResponsive", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendRestartAttemptFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendRestartAttemptSpawnFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendRestartAttemptStarting", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendRestartAttemptStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendRestartAttemptTimedOut", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendRestartScheduled", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendRestartsFailing", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendSpawnFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendSpawnFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendStarting", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendStartTimedOut", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendSupervisionFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendSupervisionGaveUp", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.BackendUnresponsive", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.CredentialsResolved", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.DeleteRefused", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.GestureCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.ObjectBinned", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.PagedReadEndedOnEmptyPage", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.SessionReportCreated", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeSource.SpaceWriteContended", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAnytypeSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
