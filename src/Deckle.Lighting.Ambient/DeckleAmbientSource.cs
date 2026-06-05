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

    // ── AmbientEngine — lifecycle ───────────────────────────────────────

    [Event(EvtPipelineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient pipeline started")]
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
           Message = "Ambient pipeline failed to start — {0}: {1}")]
    public void PipelineStartFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStartFailed, ex_type, ex_message);
    }

    [Event(EvtPipelineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient pipeline stopped ({0})")]
    public void PipelineStopped(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopped, reason);
    }

    [Event(EvtPipelineStopDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "stop | reason={0} | shape={1} | duration_sec={2:F1} | pushed={3} | dropped={4}")]
    public void PipelineStopDetail(string reason, string shape, double duration_sec, long pushed, long dropped)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopDetail, reason, shape, duration_sec, pushed, dropped);
    }

    [Event(EvtCaptureLost,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient capture lost — fatal capture failure (likely DEVICE_REMOVED / DEVICE_HUNG). Engine stopping.")]
    public void CaptureLost()
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLost);
    }

    [Event(EvtExternalChangeStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient pipeline stopped by external light change")]
    public void ExternalChangeStopped()
    {
        if (IsEnabled()) WriteEvent(EvtExternalChangeStopped);
    }

    [Event(EvtEventStreamSetupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Hue EventStream setup failed — external light change detection disabled this session ({0}: {1})")]
    public void EventStreamSetupFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamSetupFailed, ex_type, ex_message);
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

    [Event(EvtEchoIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "echo ignored | v1_id={0} | resource_type={1} | age_ms={2} | match=state")]
    public void EchoIgnored(string v1_id, string resource_type, int age_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtEchoIgnored, v1_id, resource_type, age_ms);
    }

    [Event(EvtPipelinePerLightConfig,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "light cfg | id={0} | name={1} | zone={2} | brightness={3:F2} | controlled={4}")]
    public void PipelinePerLightConfig(string id, string name, string zone, double brightness, bool controlled)
    {
        if (IsEnabled()) WriteEvent(EvtPipelinePerLightConfig, id, name, zone, brightness, controlled);
    }

    // Consumer-side confirmation that the FrameSampler was rebuilt to match
    // a capture surface the service renegotiated mid-session (HDR↔SDR toggle
    // or resolution change). Info milestone — pairs with the Vision-side
    // CaptureFormatRenegotiated so the live window shows the full chain :
    // capture renegotiates → sampler rebuilt. mode = "HDR" | "SDR".
    [Event(EvtSamplerRebuilt,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Frame sampler rebuilt for the renegotiated capture surface — now {0}")]
    public void SamplerRebuilt(string mode)
    {
        if (IsEnabled()) WriteEvent(EvtSamplerRebuilt, mode);
    }

    [Event(EvtSamplerRebuildFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Frame sampler rebuild failed after a capture renegotiation — {0}: {1} (ambient output may stay frozen until restart)")]
    public void SamplerRebuildFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSamplerRebuildFailed, ex_type, ex_message);
    }

    [Event(EvtPushLoopCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Push loop crashed — {0}: {1}")]
    public void PushLoopCrashed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPushLoopCrashed, ex_type, ex_message);
    }

    [Event(EvtStateChangedSubscriberThrew,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "StateChanged subscriber threw — {0}: {1}")]
    public void StateChangedSubscriberThrew(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtStateChangedSubscriberThrew, ex_type, ex_message);
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
           Message = "Multi-light requested but driver doesn't expose IMultiLightOutput ({0}) — falling back to group push")]
    public void MultiLightDriverIncompat(string driver_type)
    {
        if (IsEnabled()) WriteEvent(EvtMultiLightDriverIncompat, driver_type);
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
           Message = "Push failed — {0}: {1}")]
    public void PushGroupFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPushGroupFailed, ex_type, ex_message);
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
           Message = "Multi-light push failed — {0}: {1}")]
    public void PushMultiFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPushMultiFailed, ex_type, ex_message);
    }

    [Event(EvtHeartbeat,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "heartbeat | mode={0} | period_sec={1:F1} | ticks={2} | pushed={3} | dropped={4} | unmapped_lights={5}{6}")]
    public void Heartbeat(string mode, double period_sec, int ticks, int pushed, int dropped, int unmapped_lights, string http_stats_suffix)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtHeartbeat, mode, period_sec, ticks, pushed, dropped, unmapped_lights, http_stats_suffix);
    }

    // ── HuePairingService ───────────────────────────────────────────────

    [Event(EvtBridgeAutoRestoreFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge auto-restore at boot failed — {0}: {1} (user will need to re-pair)")]
    public void BridgeAutoRestoreFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeAutoRestoreFailed, ex_type, ex_message);
    }

    [Event(EvtBridgePairingStored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge pairing stored | bridge_id={0} | username_head={1}")]
    public void BridgePairingStored(string bridge_id, string username_head)
    {
        if (IsEnabled()) WriteEvent(EvtBridgePairingStored, bridge_id, username_head);
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
           Message = "Bridge restored from settings | bridge_id={0} | bridge_ip={1}")]
    public void BridgeRestoredFromSettings(string bridge_id, string bridge_ip)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeRestoredFromSettings, bridge_id, bridge_ip);
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
           Message = "Pair from Settings failed — {0}: {1}")]
    public void AmbientPagePairFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPagePairFailed, ex_type, ex_message);
    }

    [Event(EvtAmbientPageListGroupsFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Listing groups from Settings failed — {0}: {1}")]
    public void AmbientPageListGroupsFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPageListGroupsFailed, ex_type, ex_message);
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
