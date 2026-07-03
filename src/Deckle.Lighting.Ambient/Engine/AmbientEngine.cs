using System.Diagnostics;
using Deckle.Composition;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

// Orchestrator hub for the ambient-lighting pipeline. The engine
// stitches an upstream ScreenCaptureService to a downstream
// ILightOutput via a FrameSampler that runs the analysis on the GPU.
// At each tick the engine reads the sampler's most recent
// SampledFrame and pushes colour(s) to the output — with per-channel
// early-exit thresholds so a static screen doesn't spam the bridge.
//
// Two pipeline shapes, selected at Start time.
//
//   - Group mode (default). One sRGB average over the whole frame, one
//     push to the connected group via <see cref="ILightOutput.SetColorAsync"/>.
//     Every driver supports this path. Cadence : 15 Hz, matched with
//     the capture pump.
//
//   - Multi-light mode. One sRGB sample per screen-border zone (top /
//     bottom / left / right), broadcast to every light assigned to
//     that zone via <see cref="LightZone"/>, pushed in a single batch
//     via <see cref="IMultiLightOutput.SetLightColorsAsync"/>. Only
//     enabled when the driver implements <see cref="IMultiLightOutput"/>
//     AND the caller passes a non-empty zone assignment dictionary AND
//     `useMultiLight=true`. Cadence is throttled to 10 Hz to keep total
//     per-second PUT count within the Hue REST CLIP v1 comfort zone
//     for a typical 3-5 light setup (3 lights × 10 Hz = 30 PUT/s).
//     The four zones are sampled once per tick from a band of
//     <see cref="AmbientSettings.BorderDepth"/> on every edge — this
//     mirrors HyperHDR's <c>horizontalDepth</c> / <c>verticalDepth</c>
//     concept, collapsed to a single user-tunable value (V0 shipped
//     asymmetric 0.33 / 0.40 hardcoded ; V1 lets the user pick one
//     fraction that fits their room).
//
// If multi-light is requested but the driver doesn't expose the
// capability, the engine logs a warning and falls back to group mode
// transparently — the user still gets ambient lighting, just not the
// zoned variant.
//
// Why an engine at all (vs. wiring the events directly in the
// Playground or in AmbientPage code-behind) : we want a single
// well-tested object that owns the lifecycle (start, stop, dispose),
// the cadence (throttle pushes to a sane Hz against the bridge),
// and the error surface (one Warning when a push fails, never spam
// the LogWindow). Both the Playground tuning surface and the Settings
// master toggle route through the same engine — only the trigger differs.
//
// Ownership.
//   - The ScreenCaptureService is owned. The engine constructs it in
//     StartAsync, Start()s it, and Disposes it in the deferred Stop
//     cleanup. A fresh service per run picks up the current monitor
//     selection + HDR negotiation rather than a stale snapshot.
//   - The FrameSampler is owned. Built from the capture service's
//     Device / ContentSize / ActiveFormat / PeakLuminance once capture
//     is running, rebuilt in place when a mid-session recreate
//     renegotiates the capture surface (OnCaptureFormatChanged), and
//     disposed in the Stop cleanup.
//   - The ILightOutput is owned. ConnectAsync on Start ; DisposeAsync
//     (or Dispose) in the Stop cleanup.
//   - The HueBridgeClient is borrowed from HuePairingService, the
//     process-wide singleton owner shared with the Playground and the
//     Settings AmbientPage. The engine releases the reference on Stop
//     but never disposes it.
//   - The engine owns its CancellationTokenSource and the push
//     loop ; both are released on Stop / DisposeAsync.
//
// Lifecycle.
//   - Construct cheap, no I/O.
//   - StartAsync : ConnectAsync the output (idempotent), pick the
//     pipeline shape based on capability + placements, ensure capture
//     is running, kick the push loop. Returns once both are up —
//     exceptions surface from there.
//   - Stop : cancels the loop. Doesn't await the loop's finally —
//     the cancellation token is enough.
//   - DisposeAsync : Stop + dispose CTS. Idempotent.
public sealed partial class AmbientEngine : IAsyncDisposable
{

    // Push cadence — group mode. 15 Hz matches the screen capture
    // cadence throttled inside ScreenCaptureService (ThrottleIntervalMs
    // = 66 ms). One frame in, one push out (modulo the early-exit).
    // 15 Hz is well within the REST CLIP v1 sweet spot (10-20 Hz) for
    // the Hue bridge.
    private const int GroupPushHz = 15;

    // Push cadence — multi-light mode. Each tick fans out N parallel
    // PUTs (one per light), so the effective per-second pressure on
    // the bridge is N × Hz. 10 Hz × 3 lights = 30 PUT/s, still within
    // the bridge's comfort zone (Philips guidance is "no faster than
    // 10 Hz for lights, 1 Hz for groups" — we're at the lights ceiling
    // and the consumer rate-limit knob lives here).
    private const int MultiPushHz = 10;

    // Early-exit threshold — if |ΔR| + |ΔG| + |ΔB| < this, the push
    // is skipped. Now sourced from AmbientSettings.ChangeThreshold
    // (default 6), refreshed at the top of each tick. Effective
    // value seen by GroupTickAsync / MultiLightTickAsync via the
    // _changeThreshold field below.

    // Lights-out threshold — if every channel of the analysed average
    // is at or below this, we clamp the colour to (0,0,0) before the
    // push. HueColorMath maps pure black to bri=0 which the bridge
    // client translates into on:false (lamp off) ; without the clamp,
    // a near-black sample like (5,5,5) maps to bri≈2 and the lamp
    // stays faintly on instead of going dark when the screen is dark.
    // J5 will surface this in the Playground tuning panel ; for V0 we
    // keep it conservative (8 / 255 ≈ 3 %) so it only triggers on
    // unambiguously dark content (lock screen, off display).
    private const int OffThreshold = 8;

    // Zone-sampling band thickness. V0 hardcoded an asymmetric pair
    // (lateral 0.33, vertical 0.40) tuned for a 3-lamp Top + Left + Right
    // setup ; V1 exposes the thickness to the user via two scales :
    //   - Share — fraction of the matching screen dimension, same number
    //     on every edge. The top / bottom bands end up thinner in pixels
    //     than the lateral bands on a 16:9 screen.
    //   - Pixels — fixed source-pixel thickness, same number on every
    //     edge. The top band reads at the same physical thickness as
    //     the lateral bands regardless of aspect ratio.
    // Both values + the mode selector are persisted on AmbientSettings
    // and snapshotted at the top of every tick (same live-reload
    // pattern as the HDR sliders below). The engine converts the user
    // value into a per-axis cell count before calling SampleZone.

    // Heartbeat cadence for the push-loop telemetry. The per-tick
    // "push" log used to fire 10-15 times a second on a steady
    // screen, flooding the LogWindow with identical lines. We now
    // log a per-tick line only on an actual colour change, and roll
    // up the rest into a single heartbeat every N seconds so the
    // pipeline still reports it's alive without producing 300 lines
    // for a 30-second session.
    private const int HeartbeatIntervalMs = 5000;

    private readonly IAmbientEngineHost _host;

    // Three deps are owned by the engine — instantiated in StartAsync,
    // disposed in Stop. Null when the engine is idle. The
    // ScreenCaptureService is created fresh on every start so the
    // monitor selection + HDR negotiation are picked up from the
    // current Windows state rather than a stale snapshot. The bridge
    // client is borrowed from HuePairingService (singleton owner,
    // shared with the Playground and Settings AmbientPage) — never
    // disposed here, the reference is simply released on Stop.
    private ScreenCaptureService? _capture;
    private HueBridgeClient? _bridgeClient;
    private ILightOutput? _output;
    private FrameSampler? _sampler;

    // Resolved at StartAsync from _host.Ambient.UseMultiLight. The
    // multi-light pipeline shape is locked for the session : changing
    // UseMultiLight live mid-run would force a Stop + Start to reshape
    // the loop and the per-light state, so we snapshot at start and
    // ignore later host mutations until the next start.
    private bool _useMultiLightRequested;

    private CancellationTokenSource? _cts;
    private Task? _pushLoopTask;
    private Task? _eventStreamTask;
    private long _startTimestamp;
    private bool _disposed;

    // Reason tag read by Stop() when emitting PipelineStopDetail. Set
    // by the internal-trigger paths (OnCaptureStopped → "capture_lost",
    // OnResourceUpdate → "external") before they post Stop() to the
    // thread pool ; otherwise stays at the "user" default for the toggle
    // path. Reset to "user" at the top of StartAsync so a previous
    // internal stop doesn't leak into the next session's user-stop log.
    private string _stopReason = "user";
    private string? _startAbortReason;
    private int _stopRequested;

    // External-change detection state — populated at StartAsync from
    // the bridge's CLIP v2 resource list. Used by OnResourceUpdate to
    // translate event-side v2 UUIDs (what the EventStream carries)
    // back to v1 ids (what the engine's push path takes), then attribute
    // the event to either our own pending push or a bridge-side change
    // Ambient should not fight.
    private IReadOnlyDictionary<string, string>? _v2LightMap;          // v2_uuid → v1_light_id
    private IReadOnlyDictionary<string, string>? _v2GroupedLightMap;   // v2_uuid → v1_group_id
    private string? _managedGroupId;                                   // v1_group_id we are syncing

    // Last successful self-push, per v1 id ("group:<id>" or "light:<id>"
    // namespaced). Stores the Hue state we intended, not just the clock.
    // EventStream carries state changes without caller provenance, so this
    // is an attribution baseline: pending self-push echoes are ignored ;
    // stable bridge changes that diverge from it stop the pipeline.
    private readonly Dictionary<string, AmbientHueAttributionState> _hueAttributionStates = new();
    private readonly object _hueAttributionLock = new();

    // Deferred-cleanup task spun by Stop() so the UI thread that
    // triggered the stop returns immediately while the DXGI duplication
    // + sampler GPU buffers + Hue REST output get torn down on the
    // thread pool. StartAsync / DisposeAsync await this before they
    // touch the engine's owned deps, so a Start + Stop + Start sequence
    // serialises cleanly without holding the duplication past Stop.
    // Null when no cleanup is in flight (cold-start state).
    private Task? _stopCleanupTask;

    // Group-mode state — last colour we actually pushed to the bridge.
    // Compared with the sampler's most recent average to decide whether
    // to push or suppress. Default to (-1, -1, -1) so the first tick
    // always pushes.
    private int _lastR = -1, _lastG = -1, _lastB = -1;

    // Multi-light-mode state — last colour pushed per light id. The
    // dictionary lives for the engine session ; cleared on Stop.
    private Dictionary<string, (int R, int G, int B)>? _multiLastPushed;

    // Resolved multi-light fixture list (driver-reported) at Start time.
    // Null when group mode is active.
    private IReadOnlyList<LightDescriptor>? _multiLights;

    // Active pipeline shape. Set in StartAsync, read by the loop.
    private bool _multiLightActive;
    private int _pushIntervalMs = 1000 / GroupPushHz;
    private bool _requiresContinuousColorUpdates;

    private long _pushedCount;
    private long _droppedCount;

    // Heartbeat accumulators — counted by the push loop between log
    // emissions and reset every HeartbeatIntervalMs. Distinct from
    // the cumulative session counters above (which feed the stop
    // summary) so a heartbeat shows recent activity, not the whole
    // session-to-date.
    private long _hbTimestamp;
    private int  _hbTicks;
    private int  _hbPushed;
    private int  _hbDropped;
    private int  _hbUnmappedLights;

    // Per-push duration buffer for the heartbeat. Reset every
    // HeartbeatIntervalMs. Captures the wall-clock cost of the
    // await on _output.SetColorAsync / IMultiLightOutput.SetLight-
    // ColorsAsync — REST includes the bridge HTTP round-trip,
    // Entertainment covers the DTLS/UDP send path plus any local
    // back-pressure. One pushed value per tick — drops are not counted.
    private readonly List<double> _hbPushDurationsMs = new(128);

    // HDR tuning snapshot, refreshed at the top of each tick from
    // _host.Ambient. Live-reload — settings changes apply on the next
    // tick without restarting the pipeline. See
    // AmbientSettings.ExposureEv / SaturationBoost / MinBrightness /
    // BrightnessCurveParam for the user-facing semantics. The snapshot
    // avoids re-reading the host on every pixel inside the helpers.
    //   - Exposure is forwarded to FrameSampler (applied in linear
    //     light before the tone-map, mathematically correct).
    //   - Saturation boost is applied here on the sRGB output (OKLCh
    //     chroma amplification to keep hue stable and perceived
    //     luminance constant across the hue wheel).
    //   - Brightness curve gamma is applied here on the sRGB output
    //     as a uniform RGB scale (xy chromaticity invariant — only
    //     the bri derived from max(R,G,B) drops). Squashes the bottom
    //     of the bri range so dim scenes don't read as visibly lit in
    //     a dark room. γ = 1.0 is a no-op.
    //   - Min brightness is applied last on the sRGB output (raises
    //     the max channel to the floor while preserving chromaticity).
    //     Comes after the curve so the effective floor matches the
    //     value the user set, not the curve-attenuated version of it.
    private double _saturationBoost               = 1.0;
    private double _brightnessCurveParam          = 1.0;
    private double _brightnessCurveSCurveSteepness = 2.0;
    private BrightnessCurveType _brightnessCurveType = BrightnessCurveType.Linear;
    private int    _minBrightness         = 0;
    private int    _changeThreshold       = 6;
    // Zone-sampling band thickness snapshot. See the field-doc block
    // above and AmbientSettings.BorderMode / .BorderDepth / .BorderCells
    // for the user-facing semantics.
    private BorderThicknessMode _borderMode   = BorderThicknessMode.Share;
    private double              _borderDepth  = 0.33;
    private int                 _borderCells  = 8;
    // EMA factor snapshot. 1.0 = pass-through (no temporal smoothing).
    // Refreshed at the top of each tick from _host.Ambient.SmoothingAlpha.
    // The lamp jitter observed in dark scenes with small moving
    // reflections was attributed to the absence of temporal damping ;
    // this knob plus the per-tick state below close that gap.
    private double _smoothingAlpha       = 1.0;

    // EMA state — group mode. Float to preserve sub-byte precision so
    // a slow ramp progresses each tick instead of getting clipped to
    // the previous integer step. Sentinel -1f means "not yet seeded"
    // — the first tick after Start adopts the raw value (effectively
    // alpha = 1 for that single frame, no fade-in from black).
    private float _smoothedR = -1f, _smoothedG = -1f, _smoothedB = -1f;

    // EMA state — multi mode. One entry per fixture id, lazy-initialised
    // the first time the light shows up post-Start. Cleared on every
    // StartAsync so a re-pair doesn't carry over the previous setup's
    // smoothing trail. Float for the same reason as the group state.
    private readonly Dictionary<string, (float R, float G, float B)> _multiSmoothed = new();

    // Most-recent "intent" colour after tuning + smoothing, even when
    // the ChangeThreshold delta check drops the actual push. The
    // Playground swatches read these via SnapshotEmittedColors() to
    // visualise the colour the engine wants to send right now — what
    // the user would see if the lamp were faster than the delta gate.
    // Key "group" in group mode, fixture id in multi mode. The lock
    // covers cross-thread reads from the UI poll timer.
    private readonly Dictionary<string, LightColor> _emittedColors = new();
    private readonly object _emittedLock = new();

    /// <summary>Returns a snapshot of the colours the engine intended
    /// to push on the latest tick, regardless of whether the delta
    /// gate let them through. Keyed by "group" in group mode and by
    /// fixture id in multi mode. Empty before the first tick. Safe to
    /// call from any thread.</summary>
    public IReadOnlyDictionary<string, LightColor> SnapshotEmittedColors()
    {
        lock (_emittedLock)
        {
            return new Dictionary<string, LightColor>(_emittedColors);
        }
    }

    /// <summary>Raised after every push tick once the intent colours
    /// have been refreshed. Subscribers run on the engine thread —
    /// dispatch onto the UI queue before touching XAML.</summary>
    public event Action? EmittedColorsChanged;

    // Last-constructed engine, exposed for the AmbientPage that lives
    // in Deckle.Lighting.Ambient and cannot reference App (circular).
    // V0 assumes a single engine for the whole process ; multi-instance
    // scenarios (tests) get the most recent. Set by the ctor below.
    public static AmbientEngine? Current { get; private set; }

    // Bridged action invoked by the AmbientPage's "Open Playground"
    // button (rendered inside the NotPaired InfoBar). The App wires
    // this at boot to its lazy ShowPlaygroundLazy() — same approach as
    // TrayIconManager.OnShowPlayground. Lighting.Ambient cannot
    // reference App directly, so the App side fills the slot.
    public static Action? OpenPlaygroundRequested { get; set; }

    // ctor : the engine is glued to a host that exposes the live
    // AmbientSettings snapshot. The capture, the Hue bridge client,
    // the light output and the frame sampler are all owned — created
    // in StartAsync from the host's settings and disposed in Stop.
    // Construct is cheap (no I/O, no allocations beyond the empty
    // accumulator buffers above).
    public AmbientEngine(IAmbientEngineHost host)
    {
        _host = host;
        Current = this;
    }

    /// <summary>True between a successful <see cref="StartAsync"/>
    /// and the matching <see cref="Stop"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>True when the engine resolved to multi-light mode at
    /// Start. Useful for the UI to display the active pipeline shape.</summary>
    public bool IsMultiLightActive => _multiLightActive;

    /// <summary>Lights resolved from the driver when multi-light mode is
    /// active. Null when the engine isn't running or when group mode is
    /// active.</summary>
    public IReadOnlyList<LightDescriptor>? MultiLights => _multiLights;

    // Preview accessors — forwarded from the owned FrameSampler. Null /
    // zero / 1.0 when the engine is idle ; consumers (Playground
    // preview grid, AmbientPage tuning panel) should treat the absence
    // of a sample as "engine not running" and render an empty state.
    public SampledFrame? LatestSample => _sampler?.LatestSample;
    public int   GridCols     => _sampler?.GridCols ?? 0;
    public int   GridRows     => _sampler?.GridRows ?? 0;
    public bool  IsHdr        => _sampler?.IsHdr ?? false;
    public float ContentPeak  => _sampler?.ContentPeak ?? 1f;

    // State machine fired on every transition. Consumers (App tray
    // tooltip + log, AmbientPage ProgressRing / InfoBar / ModeCombo
    // gating, Playground Pipeline UI) subscribe to surface the live
    // status and distinguish a transient "Starting" / "Stopping" from
    // a settled "Off" / "Running". Error is a transient blip — the
    // engine immediately collapses to Off after raising it, so the
    // subscriber gets two consecutive callbacks (Error then Off) and
    // is expected to flash a brief error indicator before returning
    // to the Off rendering.
    private AmbientEngineState _state = AmbientEngineState.Off;
    public  AmbientEngineState State => _state;
    public event Action<AmbientEngineState>? StateChanged;

    private void SetState(AmbientEngineState newState)
    {
        _state = newState;
        try { StateChanged?.Invoke(newState); }
        catch (Exception ex)
        {
            // A subscriber threw — don't let it kill the engine flow.
            DeckleAmbientSource.Log.StateChangedSubscriberThrew();
            DeckleAmbientSource.Log.StateChangedSubscriberThrewDetail(ex.GetType().Name, ex.Message);
        }
    }

    private void AbortStartOrStop(string reason, Action log)
    {
        var state = _state;
        if (state == AmbientEngineState.Off || state == AmbientEngineState.Stopping)
            return;

        _stopReason = reason;
        log();

        if (IsRunning)
        {
            Task.Run(Stop);
        }
        else
        {
            _startAbortReason = reason;
        }
    }

    private void ThrowIfStartAbortRequested()
    {
        if (_startAbortReason is { } reason)
            throw new InvalidOperationException($"Ambient start aborted by upstream stop: {reason}");
    }

}
