using Deckle.TestSupport;
using Xunit;

namespace Deckle.App.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleAppSource.AmbientMasterForcedOff", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.AmbientStartFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.AnytypeHostDrainIncomplete", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.AnytypeRuntimeEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.AnytypeRuntimeFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.AutocorrectAssetSkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.AutocorrectEnrollmentPromptFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.AutocorrectFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.AutocorrectUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.CleanupFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.CmdLinePostBuildFlag", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleAppSource.CmdLineSettingsFlag", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.CrashAppDomain", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.CrashAppDomainDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.CrashDumpCaptureArmedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.CrashDumpCaptureOffDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.CrashDumpFoundDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.CrashRelaunched", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.CrashStackTrace", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.DataRootCleanupFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.DataRootCleanupFinished", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.DataRootCleanupStarting", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.DictationRecoveredDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.DictationRecoveryClipboardFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.DictationRecoveryKept", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.DictationRecoveryUnreadable", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.FileTranscriptionEntryFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.HotkeyPressIgnored", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.HotkeyRegistrationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.HotkeyRegistrationFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.HotkeyStartDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.HotkeyToggleDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.InputConsumerUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.InstallModeEnteredDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.InstallTempCleanupFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.LaunchContextDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.LegacySettingsMigrationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.LoggingPolicy", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ModuleFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ModuleRefused", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ModuleSkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ModuleStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ModuleStarting", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.PathsDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.PostBuildShellExecute", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.PreviousRunEndedUncleanly", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.RelaunchFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ResidentMutexAbandoned", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ResidentMutexReleaseFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.RestartSettingsFlushFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.RestartSpawned", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.RestartSpawnNewProcess", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.RestartSpawnSkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.RunIdentity", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.SecondInstanceRejected", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.SecondInstancesRejected", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ShutdownCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ShutdownRequested", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ShutdownStepFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.StartupMilestones", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.StatusChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.SurvivedException", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.SurvivedExceptionRepeated", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ThemeApplied", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.ThemeUnknown", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.TransactionalModeExited", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.TransactionalModeFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.TrayToggleChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.UpdateCheckOutcome", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.UpdateChecksStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.UpdateChecksStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.UpdateFlowFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.UpdateFlowFinished", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.UpdateFlowRefused", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.UpdateFlowStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.UserFeedbackEmitted", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.WindowCreated", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.WindowOpenFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAppSource.WindowOperationFailed", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAppSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
