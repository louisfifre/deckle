using Deckle.TestSupport;
using Xunit;

namespace Deckle.Setup.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleSetupSource.BestEffortFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.ChildProcessLaunchedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.ChoicesConfirmedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.DeployBlockedByRunningApp", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.DeployCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.DeployCompletedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.DeployFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.DeployStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.FoldersChosenDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.ItemCancelledDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.ItemDownloadFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.ItemImported", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.ItemImportFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.ItemInstalledDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.ItemSkippedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.ItemStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.NativeImportFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.NativeSourcePickedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.PredecessorWaitFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.PredecessorWaitTimedOut", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.RelocateCancelled", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.RelocateCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.RelocateCompletedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.RelocateFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.RelocateFilesSkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.RelocateInsufficientSpaceDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.RelocateStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.RelocateStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.SummaryShownDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.UpdateCheckDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.UpdateCheckFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.UpdateCheckSkippedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.UpdateDownloadCancelled", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.UpdateDownloadCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.UpdateDownloadFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.UpdateDownloadStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.UpdateDownloadStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.UpdateHandoffDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleSetupSource.WindowClosingDetail", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleSetupSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
