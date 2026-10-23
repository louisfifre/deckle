using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleAmbientSource.AmbientPageListGroupsFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.AmbientPagePairFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgeAutoRestoreFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgeCandidateRejectedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgeEndpointRecoveredDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgeEndpointRecoveryFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgeEndpointRecoveryFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgeEndpointRecoveryStarting", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgePairingStoredDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgeRestoredFromSettings", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgeRestoredFromSettingsDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.BridgeRestoreSkipped", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleAmbientSource.CaptureLost", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.EchoIgnored", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.EntertainmentAreaMatched", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.EntertainmentAreaUsedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.EventStreamSetupFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.ExternalChangeDecisionDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.ExternalChangeStoppedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.FrameProcessingEpisodeDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.FrameProcessingFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.FrameProcessingIncidentOpened", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.Heartbeat", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.LightBrightnessChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.LightCallFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.LightsResolved", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.LightZoneUpdatedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.MultiLightFallback", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineAbortIgnoredDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineIncidentAbandoned", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineModeChangedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelinePerLightConfig", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineRestartDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineStartCancelled", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineStartDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineStartFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineStarting", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineStepFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PipelineStopping", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PresetSelectionReplaced", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PushAttemptFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PushEpisodeDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PushGroup", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PushIncidentOpened", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PushLoopCrashed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PushMulti", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.PushRejected", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.ResourceReleaseFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.SamplerRebuildFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.SamplerRebuiltDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.StateChangedSubscriberThrew", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.TuningChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleAmbientSource.ZoneSuggested", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAmbientSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
