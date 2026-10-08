using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

// Ambient module provider. Covers the AmbientEngine orchestrator (pipeline
// lifecycle + group/multi push tick + heartbeat + push failures), the
// HuePairingService (pairing + restore + forget), the Settings AmbientPage
// surface (Hue pair button + group list), and module settings persistence
// (AmbientSettingsService).
//
// Provider Name = "Deckle-Ambient": short choice to keep the LogWindow
// [AMBIENT] tag. The low-level Hue driver keeps its separate provider in
// Deckle.Lighting.
[EventSource(Name = "Deckle-Ambient")]
public sealed partial class DeckleAmbientSource : DeckleEventSource
{
    public static readonly DeckleAmbientSource Log = new();

    private DeckleAmbientSource() { }

    [NonEvent]
    private bool IsAmbientDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Ambient, this, level, keywords);

    // IDs are public in the ETW manifest; never reuse an id after deleting an
    // event. Milestones keep their original id; the Verbose mirrors added for the
    // Verbose/Info separation take fresh ids 35-52 appended after the sequence.

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtPipelineStarted               = 1;
    public const int EvtPipelineStartDetail           = 2;
    public const int EvtPipelineStartFailed           = 3;
    public const int EvtPipelineStopped               = 4;
    public const int EvtPipelineStopDetail            = 5;
    public const int EvtPushLoopCrashed               = 6;
    public const int EvtStateChangedSubscriberThrew   = 7;
    public const int EvtMultiLightFallbackNoLights    = 8;
    public const int EvtMultiLightDriverIncompat      = 9;
    public const int EvtPushGroup                     = 10;
    public const int EvtPushGroupFailed               = 11;
    public const int EvtPushMulti                     = 12;
    public const int EvtPushMultiFailed               = 13;
    public const int EvtHeartbeat                     = 14;
    public const int EvtBridgeAutoRestoreFailed       = 15;
    public const int EvtBridgePairingStored           = 16;
    public const int EvtBridgeRestoreSkipped          = 17;
    public const int EvtBridgeRestoredFromSettings    = 18;
    public const int EvtBridgeForgotten               = 19;
    public const int EvtAmbientPagePairFailed         = 20;
    public const int EvtAmbientPageListGroupsFailed   = 21;
    public const int EvtSettingsLoaded                = 22;
    public const int EvtSettingsLoadComplete          = 23;
    public const int EvtSettingsLoadWarning           = 24;
    public const int EvtSettingsLoadError             = 25;
    public const int EvtAmbientSettingsPrefixed       = 26;
    public const int EvtCaptureLost                   = 27;
    public const int EvtExternalChangeStopped         = 28;
    public const int EvtEventStreamSetupFailed        = 29;
    public const int EvtPipelinePerLightConfig        = 30;
    public const int EvtExternalChangeStoppedDetail   = 31;
    public const int EvtEchoIgnored                   = 32;
    public const int EvtSamplerRebuilt                = 33;
    public const int EvtSamplerRebuildFailed          = 34;

    // ── Verbose mirrors (Verbose/Info separation) ───────────────────────
    // Fresh ids appended after the milestone sequence; each mirrors a milestone
    // whose IDs / k=v detail moved out of the Capital Info/Warning/Error message.
    public const int EvtPipelineStartFailedDetail       = 35;
    public const int EvtPushLoopCrashedDetail           = 36;
    public const int EvtStateChangedSubscriberThrewDetail = 37;
    public const int EvtMultiLightDriverIncompatDetail  = 38;
    public const int EvtPushGroupFailedDetail           = 39;
    public const int EvtPushMultiFailedDetail           = 40;
    public const int EvtBridgeAutoRestoreFailedDetail   = 41;
    public const int EvtBridgePairingStoredDetail       = 42;
    public const int EvtBridgeRestoredFromSettingsDetail = 43;
    public const int EvtAmbientPagePairFailedDetail     = 44;
    public const int EvtAmbientPageListGroupsFailedDetail = 45;
    public const int EvtEventStreamSetupFailedDetail    = 46;
    public const int EvtSamplerRebuiltDetail            = 47;
    public const int EvtSamplerRebuildFailedDetail      = 48;
    public const int EvtExternalChangeDecisionDetail    = 49;
    public const int EvtPipelineStarting                = 50;
    public const int EvtPipelineStopping                = 51;
    public const int EvtPushIncidentOpened              = 52;
    public const int EvtPushRecovered                   = 53;
    public const int EvtPushEpisodeDetail               = 54;
    public const int EvtPushRejected                    = 55;
    public const int EvtFrameProcessingIncidentOpened   = 56;
    public const int EvtFrameProcessingRecovered        = 57;
    public const int EvtFrameProcessingFailed           = 58;
    public const int EvtFrameProcessingEpisodeDetail    = 59;
    public const int EvtBridgeEndpointRecovered         = 60;
    public const int EvtBridgeEndpointRecoveredDetail   = 61;
    public const int EvtBridgeEndpointRecoveryFailed    = 62;
    public const int EvtBridgeEndpointRecoveryFailedDetail = 63;
    public const int EvtPipelineRestartDetail           = 64;

    // ── AmbientViewModel setters (moved from Deckle-Playground ids 3, 16, 17,
    // which stay reserved there) ─────────────────────────────────────────
    public const int EvtTuningChanged                   = 65;
    public const int EvtPipelineModeChanged             = 66;
    public const int EvtPipelineModeChangedDetail       = 67;

    // ── Per-light placement (moved from Deckle-Playground ids 7, 8, 9, 15,
    // 18-22, which stay reserved there) ──────────────────────────────────
    public const int EvtLightCallFailed                 = 68;
    public const int EvtLightCallFailedDetail           = 69;
    public const int EvtLightsResolved                  = 70;
    public const int EvtEntertainmentAreaUsed           = 71;
    public const int EvtEntertainmentAreaUsedDetail     = 72;
    public const int EvtEntertainmentAreaMatched        = 73;
    public const int EvtZoneSuggested                   = 74;
    public const int EvtLightZoneUpdated                = 75;
    public const int EvtLightZoneUpdatedDetail          = 76;
    public const int EvtLightBrightnessChanged          = 77;

}
