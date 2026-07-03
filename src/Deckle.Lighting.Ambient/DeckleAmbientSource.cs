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
public sealed class DeckleAmbientSource : DeckleEventSource
{
    public static readonly DeckleAmbientSource Log = new();

    private DeckleAmbientSource() { }

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

    // ── AmbientEngine — lifecycle ───────────────────────────────────────

    [Event(EvtPipelineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pipeline started")]
    public void PipelineStarted()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStarted);
    }

    [Event(EvtPipelineStartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "start | source={0} | output={1} | shape={2} | lights={3} | push_hz={4} | sampler_grid={5}x{6} | hdr={7}")]
    public void PipelineStartDetail(string source, string output, string shape, int lights, int push_hz, int grid_cols, int grid_rows, string hdr)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStartDetail, source, output, shape, lights, push_hz, grid_cols, grid_rows, hdr);
    }

    [Event(EvtPipelineStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pipeline failed to start")]
    public void PipelineStartFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStartFailed);
    }

    [Event(EvtPipelineStartFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "start failed | ex_type={0} | ex_message={1}")]
    public void PipelineStartFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelineStartFailedDetail, ex_type, ex_message);
    }

    // The reason / shape / counters move to the existing PipelineStopDetail
    // Verbose mirror; the milestone drops the (reason) suffix.
    [Event(EvtPipelineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pipeline stopped")]
    public void PipelineStopped()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopped);
    }

    [Event(EvtPipelineStopDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "stop | reason={0} | shape={1} | duration_sec={2:F1} | pushed={3} | dropped={4}")]
    public void PipelineStopDetail(string reason, string shape, double duration_sec, long pushed, long dropped)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopDetail, reason, shape, duration_sec, pushed, dropped);
    }

    // Entirely human-readable statement, no params and no placeholders — cleaned
    // in place (dropped the module name and the DEVICE_REMOVED / DEVICE_HUNG
    // implementation aside), no Verbose mirror.
    [Event(EvtCaptureLost,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Capture was lost — the engine is stopping")]
    public void CaptureLost()
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLost);
    }

    [Event(EvtExternalChangeStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pipeline stopped by an external light change")]
    public void ExternalChangeStopped()
    {
        if (IsEnabled()) WriteEvent(EvtExternalChangeStopped);
    }

    [Event(EvtEventStreamSetupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Hue event stream setup failed — external light change detection is disabled this session")]
    public void EventStreamSetupFailed()
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamSetupFailed);
    }

    [Event(EvtEventStreamSetupFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "event stream setup failed | ex_type={0} | ex_message={1}")]
    public void EventStreamSetupFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtEventStreamSetupFailedDetail, ex_type, ex_message);
    }

    [Event(EvtExternalChangeStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "external change | v1_id={0} | resource_type={1} | age_ms={2} | on={3} | bri={4} | xy={5}")]
    public void ExternalChangeStoppedDetail(string v1_id, string resource_type, int age_ms, string on, string bri, string xy)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtExternalChangeStoppedDetail, v1_id, resource_type, age_ms, on, bri, xy);
    }

    [Event(EvtExternalChangeDecisionDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "external decision | v1_id={0} | resource_type={1} | age_ms={2} | event_on={3} | pushed_on={4} | event_bri={5} | pushed_bri={6} | event_xy={7} | pushed_xy={8} | delta_xy={9} | basis={10}")]
    public void ExternalChangeDecisionDetail(
        string v1_id,
        string resource_type,
        int age_ms,
        string event_on,
        string pushed_on,
        string event_bri,
        string pushed_bri,
        string event_xy,
        string pushed_xy,
        string delta_xy,
        string mismatch)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(
            EvtExternalChangeDecisionDetail,
            v1_id,
            resource_type,
            age_ms,
            event_on,
            pushed_on,
            event_bri,
            pushed_bri,
            event_xy,
            pushed_xy,
            delta_xy,
            mismatch);
    }

    [Event(EvtEchoIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "echo ignored | v1_id={0} | resource_type={1} | age_ms={2} | match=state")]
    public void EchoIgnored(string v1_id, string resource_type, int age_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtEchoIgnored, v1_id, resource_type, age_ms);
    }

    // Per-light configuration dump: emitted in a loop, one line per light, with
    // ids and k=v — Verbose by nature, never a single human milestone. Demoted
    // from Info to Verbose for the Verbose/Info separation. The call site emits
    // it right after PipelineStarted, BEFORE AmbientCaptureGate opens, so the
    // line still passes the LogWindow drop filter (the gate is closed at that
    // point) — demotion costs no visibility there.
    [Event(EvtPipelinePerLightConfig,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "light cfg | id={0} | name={1} | zone={2} | brightness={3:F2} | controlled={4}")]
    public void PipelinePerLightConfig(string id, string name, string zone, double brightness, bool controlled)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelinePerLightConfig, id, name, zone, brightness, controlled);
    }

    // Consumer-side confirmation that the FrameSampler was rebuilt to match
    // a capture surface the service renegotiated mid-session (HDR↔SDR toggle
    // or resolution change). Info milestone — pairs with the Vision-side
    // CaptureFormatRenegotiated so the live window shows the full chain :
    // capture renegotiates → sampler rebuilt. mode = "HDR" | "SDR".
    [Event(EvtSamplerRebuilt,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Frame sampler rebuilt for the renegotiated capture surface")]
    public void SamplerRebuilt()
    {
        if (IsEnabled()) WriteEvent(EvtSamplerRebuilt);
    }

    [Event(EvtSamplerRebuiltDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "sampler rebuilt | mode={0}")]
    public void SamplerRebuiltDetail(string mode)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtSamplerRebuiltDetail, mode);
    }

    [Event(EvtSamplerRebuildFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Frame sampler rebuild failed after a capture renegotiation — output may stay frozen until restart")]
    public void SamplerRebuildFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSamplerRebuildFailed);
    }

    [Event(EvtSamplerRebuildFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "sampler rebuild failed | ex_type={0} | ex_message={1}")]
    public void SamplerRebuildFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtSamplerRebuildFailedDetail, ex_type, ex_message);
    }

    [Event(EvtPushLoopCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Push loop crashed")]
    public void PushLoopCrashed()
    {
        if (IsEnabled()) WriteEvent(EvtPushLoopCrashed);
    }

    [Event(EvtPushLoopCrashedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "push loop crashed | ex_type={0} | ex_message={1}")]
    public void PushLoopCrashedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtPushLoopCrashedDetail, ex_type, ex_message);
    }

    [Event(EvtStateChangedSubscriberThrew,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A state-change subscriber threw")]
    public void StateChangedSubscriberThrew()
    {
        if (IsEnabled()) WriteEvent(EvtStateChangedSubscriberThrew);
    }

    [Event(EvtStateChangedSubscriberThrewDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "state change subscriber threw | ex_type={0} | ex_message={1}")]
    public void StateChangedSubscriberThrewDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtStateChangedSubscriberThrewDetail, ex_type, ex_message);
    }

    [Event(EvtMultiLightFallbackNoLights,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Multi-light requested but driver returned no lights — falling back to group push")]
    public void MultiLightFallbackNoLights()
    {
        if (IsEnabled()) WriteEvent(EvtMultiLightFallbackNoLights);
    }

    [Event(EvtMultiLightDriverIncompat,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Multi-light requested but the driver cannot address individual lights — falling back to group push")]
    public void MultiLightDriverIncompat()
    {
        if (IsEnabled()) WriteEvent(EvtMultiLightDriverIncompat);
    }

    [Event(EvtMultiLightDriverIncompatDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "multi-light driver incompatible | driver_type={0}")]
    public void MultiLightDriverIncompatDetail(string driver_type)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtMultiLightDriverIncompatDetail, driver_type);
    }

    // ── Push ticks ──────────────────────────────────────────────────────

    [Event(EvtPushGroup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "push | mode=group | rgb={0},{1},{2} | off={3} | http_ms={4:F1}")]
    public void PushGroup(int r, int g, int b, bool off, double http_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushGroup, r, g, b, off, http_ms);
    }

    [Event(EvtPushGroupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Group push failed")]
    public void PushGroupFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPushGroupFailed);
    }

    [Event(EvtPushGroupFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "group push failed | ex_type={0} | ex_message={1}")]
    public void PushGroupFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushGroupFailedDetail, ex_type, ex_message);
    }

    [Event(EvtPushMulti,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "push | mode=multi | lights={0}/{1} | colors={2} | http_ms={3:F1}")]
    public void PushMulti(int pushed_lights, int total_lights, string colors, double http_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushMulti, pushed_lights, total_lights, colors, http_ms);
    }

    [Event(EvtPushMultiFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Multi-light push failed")]
    public void PushMultiFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPushMultiFailed);
    }

    [Event(EvtPushMultiFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "multi-light push failed | ex_type={0} | ex_message={1}")]
    public void PushMultiFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushMultiFailedDetail, ex_type, ex_message);
    }

    [Event(EvtHeartbeat,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "heartbeat | mode={0} | period_sec={1:F1} | ticks={2} | pushed={3} | dropped={4} | unmapped_lights={5}{6}")]
    public void Heartbeat(string mode, double period_sec, int ticks, int pushed, int dropped, int unmapped_lights, string push_stats_suffix)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtHeartbeat, mode, period_sec, ticks, pushed, dropped, unmapped_lights, push_stats_suffix);
    }

    // ── HuePairingService ───────────────────────────────────────────────

    [Event(EvtBridgeAutoRestoreFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge auto-restore at boot failed — the user will need to re-pair")]
    public void BridgeAutoRestoreFailed()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeAutoRestoreFailed);
    }

    [Event(EvtBridgeAutoRestoreFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge auto-restore failed | ex_type={0} | ex_message={1}")]
    public void BridgeAutoRestoreFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgeAutoRestoreFailedDetail, ex_type, ex_message);
    }

    [Event(EvtBridgePairingStored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge pairing stored")]
    public void BridgePairingStored()
    {
        if (IsEnabled()) WriteEvent(EvtBridgePairingStored);
    }

    [Event(EvtBridgePairingStoredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge pairing stored | bridge_id={0} | username_head={1}")]
    public void BridgePairingStoredDetail(string bridge_id, string username_head)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgePairingStoredDetail, bridge_id, username_head);
    }

    [Event(EvtBridgeRestoreSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restore | skipped — no persisted bridge identity")]
    public void BridgeRestoreSkipped()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeRestoreSkipped);
    }

    [Event(EvtBridgeRestoredFromSettings,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge restored from settings")]
    public void BridgeRestoredFromSettings()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeRestoredFromSettings);
    }

    [Event(EvtBridgeRestoredFromSettingsDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge restored from settings | bridge_id={0} | bridge_ip={1}")]
    public void BridgeRestoredFromSettingsDetail(string bridge_id, string bridge_ip)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgeRestoredFromSettingsDetail, bridge_id, bridge_ip);
    }

    [Event(EvtBridgeForgotten,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge forgotten — persisted credentials cleared")]
    public void BridgeForgotten()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeForgotten);
    }

    // ── AmbientPage UI surface ──────────────────────────────────────────

    [Event(EvtAmbientPagePairFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing from Settings failed")]
    public void AmbientPagePairFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPagePairFailed);
    }

    [Event(EvtAmbientPagePairFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair from settings failed | ex_type={0} | ex_message={1}")]
    public void AmbientPagePairFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtAmbientPagePairFailedDetail, ex_type, ex_message);
    }

    [Event(EvtAmbientPageListGroupsFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Listing groups from Settings failed")]
    public void AmbientPageListGroupsFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPageListGroupsFailed);
    }

    [Event(EvtAmbientPageListGroupsFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "list groups from settings failed | ex_type={0} | ex_message={1}")]
    public void AmbientPageListGroupsFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtAmbientPageListGroupsFailedDetail, ex_type, ex_message);
    }

    // ── Settings persistence ────────────────────────────────────────────

    [Event(EvtSettingsLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoaded(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoaded, message);
    }

    [Event(EvtSettingsLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadComplete(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadComplete, message);
    }

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    [Event(EvtAmbientSettingsPrefixed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void AmbientSettingsPrefixed(string message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientSettingsPrefixed, message);
    }
}
