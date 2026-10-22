using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

// Ambient lighting provider, "Deckle-Ambient". One partial file per area:
// pipeline lifecycle, push, capture and sampling consequences, bridge pairing
// and endpoint recovery, light placement, UI and settings. Verbose detail
// passes the Ambient activity admission. Screen capture reports its own
// causes on Deckle-Vision and the low-level Hue driver on its provider in
// Deckle.Lighting; this provider says what they mean for ambient lighting.
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
    // event. A new event takes the next free id.

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtPipelineStarted               = 1;
    public const int EvtPipelineStartDetail           = 2;
    // 3 retired.
    // 4 retired.
    // 5 retired.
    // 6 retired.
    // 7 retired.
    // 8 retired.
    // 9 retired.
    public const int EvtPushGroup                     = 10;
    // 11 retired.
    public const int EvtPushMulti                     = 12;
    // 13 retired.
    // 14 retired.
    // 15 retired.
    public const int EvtBridgePairingStored           = 16;
    public const int EvtBridgeRestoreSkipped          = 17;
    // 18 retired.
    public const int EvtBridgeForgotten               = 19;
    // 20 retired.
    // 21 retired.
    // 22 retired.
    // 23 retired.
    public const int EvtSettingsLoadWarning           = 24;
    public const int EvtSettingsLoadError             = 25;
    // 26 retired.
    // 27 retired.
    public const int EvtExternalChangeStopped         = 28;
    // 29 retired.
    public const int EvtPipelinePerLightConfig        = 30;
    public const int EvtExternalChangeStoppedDetail   = 31;
    public const int EvtEchoIgnored                   = 32;
    public const int EvtSamplerRebuilt                = 33;
    // 34 retired.

    // ── Appended ────────────────────────────────────────────────────────
    // 35 retired.
    // 36 retired.
    // 37 retired.
    // 38 retired.
    // 39 retired.
    // 40 retired.
    // 41 retired.
    public const int EvtBridgePairingStoredDetail       = 42;
    public const int EvtBridgeRestoredFromSettingsDetail = 43;
    // 44 retired.
    // 45 retired.
    // 46 retired.
    public const int EvtSamplerRebuiltDetail            = 47;
    // 48 retired.
    public const int EvtExternalChangeDecisionDetail    = 49;
    public const int EvtPipelineStarting                = 50;
    public const int EvtPipelineStopping                = 51;
    // 52 retired.
    public const int EvtPushRecovered                   = 53;
    public const int EvtPushEpisodeDetail               = 54;
    // 55 retired.
    // 56 retired.
    public const int EvtFrameProcessingRecovered        = 57;
    // 58 retired.
    public const int EvtFrameProcessingEpisodeDetail    = 59;
    public const int EvtBridgeEndpointRecovered         = 60;
    public const int EvtBridgeEndpointRecoveredDetail   = 61;
    // 62 retired.
    public const int EvtBridgeEndpointRecoveryFailedDetail = 63;
    public const int EvtPipelineRestartDetail           = 64;

    // ── AmbientViewModel setters ─────────────────────────────────────────
    public const int EvtTuningChanged                   = 65;
    public const int EvtPipelineModeChanged             = 66;
    public const int EvtPipelineModeChangedDetail       = 67;

    // ── Per-light placement ──────────────────────────────────────────────
    // 68 retired.
    // 69 retired.
    public const int EvtLightsResolved                  = 70;
    public const int EvtEntertainmentAreaUsed           = 71;
    public const int EvtEntertainmentAreaUsedDetail     = 72;
    public const int EvtEntertainmentAreaMatched        = 73;
    public const int EvtZoneSuggested                   = 74;
    public const int EvtLightZoneUpdated                = 75;
    public const int EvtLightZoneUpdatedDetail          = 76;
    public const int EvtLightBrightnessChanged          = 77;

    // ── Causes carried in the milestones ─────────────────────────────────
    public const int EvtPipelineStartFailed             = 78;
    public const int EvtPipelineStartCancelled          = 79;
    public const int EvtPipelineStopped                 = 80;
    // 81: retired ID, never reused. Its pushed / dropped totals moved onto PipelineStopped.
    public const int EvtPushLoopCrashed                 = 82;
    public const int EvtStateChangedSubscriberThrew     = 83;
    public const int EvtMultiLightFallback              = 84;
    public const int EvtHeartbeat                       = 85;
    public const int EvtBridgeAutoRestoreFailed         = 86;
    public const int EvtAmbientPagePairFailed           = 87;
    public const int EvtAmbientPageListGroupsFailed     = 88;
    public const int EvtEventStreamSetupFailed          = 89;
    public const int EvtSamplerRebuildFailed            = 90;
    public const int EvtPushIncidentOpened              = 91;
    public const int EvtPushRejected                    = 92;
    public const int EvtFrameProcessingIncidentOpened   = 93;
    public const int EvtFrameProcessingFailed           = 94;
    public const int EvtBridgeEndpointRecoveryFailed    = 95;
    public const int EvtLightCallFailed                 = 96;
    public const int EvtPushAttemptFailedDetail         = 97;
    public const int EvtResourceReleaseFailed           = 98;
    public const int EvtPipelineStepFailedDetail        = 99;
    public const int EvtBridgeEndpointRecoveryStarting  = 100;
    public const int EvtBridgeCandidateRejectedDetail   = 101;
    public const int EvtBridgeRestoredFromSettings      = 102;
    public const int EvtCaptureLost                     = 103;
    public const int EvtPipelineIncidentAbandoned       = 104;
    public const int EvtPipelineAbortIgnoredDetail      = 105;
}
