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
// SampledFrame and pushes colour(s) to the output, through a change
// gate (AmbientPushGate) so a static screen doesn't spam the bridge.
//
// Two pipeline shapes, selected at Start time.
//
//   - Group mode (default). One sRGB average over the whole frame, one
//     push to the connected group via <see cref="ILightOutput.SetColorAsync"/>.
//     Every driver supports this path. REST runs at 15 Hz; a streaming
//     driver can advertise its preferred rate.
//
//   - Multi-light mode. One sRGB sample per screen-border zone (top /
//     bottom / left / right), broadcast to every light assigned to
//     that zone via <see cref="LightZone"/>, pushed in a single batch
//     via <see cref="IMultiLightOutput.SetLightColorsAsync"/>. Only
//     enabled when UseMultiLight is set, the driver implements
//     <see cref="IMultiLightOutput"/> AND it reports at least one light.
//     REST cadence is throttled to 10 Hz to keep the per-second PUT
//     count within the Hue REST CLIP v1 comfort zone (3 lights × 10 Hz
//     = 30 PUT/s). A batch streaming driver can override that transport
//     constraint. The four zones are sampled once per tick from a border
//     band (AmbientSettings.BorderMode / BorderDepth / BorderCells),
//     HyperHDR's <c>horizontalDepth</c> / <c>verticalDepth</c> collapsed
//     to one user value.
//
// If multi-light is requested but the driver doesn't expose the
// capability, the engine logs a warning and falls back to group mode
// transparently — the user still gets ambient lighting, just not the
// zoned variant.
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
//   - The ILightOutput is owned and disposed in the Stop cleanup.
//   - The HueBridgeClient is borrowed from HuePairingService, the
//     process-wide singleton owner shared with the
//     Settings AmbientPage. The engine releases the reference on Stop
//     but never disposes it.
public sealed partial class AmbientEngine : IAsyncDisposable
{

    // Black gate — a light is sent black only when its source is exactly
    // black : the frame's linear average (group) or the zone's linear
    // mean (multi) is exactly (0, 0, 0). With the brightness floor off,
    // HueColorMath maps a pushed (0, 0, 0) to on:false ; with the floor
    // on, AmbientColorPipeline lifts black to the neutral floor instead.

    // Steady ticks roll up into one heartbeat per interval instead of one
    // log line per tick.
    private const int HeartbeatIntervalMs = 5000;

    private readonly IAmbientEngineHost _host;

    private ScreenCaptureService? _capture;
    private HueBridgeClient? _bridgeClient;
    private ILightOutput? _output;
    private FrameSampler? _sampler;

    // Per-frame Vision causes are folded into one workflow incident. The
    // accumulated clock caps gaps at AcquireNextFrame's normal wait window so
    // a static desktop does not count as sustained failed processing.
    private ScreenCaptureService? _frameProcessingEpisodeCapture;
    private long _frameProcessingLastFailureTicks;
    private long _frameProcessingActiveFailureTicks;
    private int _frameProcessingFailures;
    private bool _frameProcessingIncidentOpen;
    private int _frameProcessingStopRequested;

    // Snapshotted at StartAsync : the loop shape and the per-light state
    // are built there, so a live change waits for the next start. The app
    // restarts the engine when the value changes (AmbientStartSettings).
    private bool _useMultiLightRequested;

    private CancellationTokenSource? _cts;
    private Task? _pushLoopTask;
    private Task? _eventStreamTask;
    private long _startTimestamp;
    private bool _disposed;

    // Reason tag read by Stop() when emitting PipelineStopDetail. Internal
    // triggers set it before they post Stop() to the thread pool ; the
    // toggle path leaves "user". StartAsync resets it so an internal stop
    // doesn't leak into the next session's log.
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

    // Last colour pushed in group mode ; (-1, -1, -1) means none yet, so
    // the first tick always pushes.
    private int _lastR = -1, _lastG = -1, _lastB = -1;

    private Dictionary<string, (int R, int G, int B)>? _multiLastPushed;

    private IReadOnlyList<LightDescriptor>? _multiLights;

    private bool _multiLightActive;
    private int _pushRateHz = 15;
    private int _pushIntervalMs = 1000 / 15;
    private bool _requiresContinuousColorUpdates;

    private long _pushedCount;
    private long _droppedCount;
    private int _pushConsecutiveFailures;
    private long _pushFailureStartTimestamp;
    private bool _pushIncidentOpen;
    private string _pushFailureType = "none";
    private string _pushFailureMessage = "none";

    // Heartbeat accumulators, reset every HeartbeatIntervalMs. Distinct
    // from the cumulative session counters above (which feed the stop
    // summary) so a heartbeat shows recent activity.
    private readonly AmbientHeartbeatWindow _heartbeatWindow = new();
    private int  _hbTicks;
    private int  _hbPushed;
    private int  _hbDropped;
    private int  _hbUnmappedLights;
    private long _hbSkippedSlots;

    // Wall-clock cost of each push await, for the heartbeat : REST includes
    // the bridge HTTP round-trip, Entertainment the DTLS/UDP send plus any
    // local back-pressure. Drops are not counted.
    private List<double>? _hbPushDurationsMs;

    // Tuning snapshot, refreshed at the top of each tick from _host.Ambient
    // so a settings change applies on the next tick without a restart.
    // Exposure goes to the FrameSampler (linear light, before the tone-map) ;
    // saturation, curve and floor run in AmbientColorPipeline, the floor last
    // so it matches the value the user set, not a curve-attenuated one.
    private double _saturationBoost       = 1.0;
    private double _brightnessCurveX1     = 0.42;
    private double _brightnessCurveY1     = 0.00;
    private double _brightnessCurveX2     = 1.00;
    private double _brightnessCurveY2     = 1.00;
    private bool   _minBrightnessEnabled  = true;
    private int    _minBrightness         = 0;
    private int    _changeThreshold       = 6;
    private BorderThicknessMode _borderMode   = BorderThicknessMode.Share;
    private double              _borderDepth  = 0.33;
    private int                 _borderCells  = 8;
    // EMA step at the 15 Hz reference cadence ; 1.0 = no smoothing.
    // AmbientPushCadence adapts it to the measured tick interval so a
    // faster stream adds intermediate steps without converging faster in
    // wall-clock time.
    private double _smoothingAlpha       = 1.0;

    // EMA state, keyed "group" in group mode and by fixture id in multi
    // mode. Cleared on every StartAsync so a re-pair doesn't carry over
    // the previous setup's smoothing trail.
    private const string GroupKey = "group";
    private readonly AmbientColorSmoother _smoother = new();

    // The single quantisation per key, with its near-black latch. Same
    // keys and lifetime as the smoother. See AmbientBlackLatch.
    private readonly AmbientBlackLatch _blackLatch = new();

    // Measured time between two push ticks, fed to the smoother. Zero
    // timestamp = no tick yet since Start ; the first tick uses the
    // nominal interval.
    private long _lastTickTimestamp;
    private double _tickDtSeconds;

    // The lock covers reads from the UI preview timer (SnapshotEmittedColors).
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

    // Last-constructed engine, for the Ambient UI that cannot reference
    // App (circular). One engine per process ; with several (tests), the
    // most recent wins.
    public static AmbientEngine? Current { get; private set; }

    public AmbientEngine(IAmbientEngineHost host)
    {
        _host = host;
        Current = this;
    }

    /// <summary>True between a successful <see cref="StartAsync"/>
    /// and the matching <see cref="Stop"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>True when the engine resolved to multi-light mode at
    /// Start.</summary>
    public bool IsMultiLightActive => _multiLightActive;

    /// <summary>Lights resolved from the driver when multi-light mode is
    /// active. Null when the engine isn't running or when group mode is
    /// active.</summary>
    public IReadOnlyList<LightDescriptor>? MultiLights => _multiLights;

    // Preview accessors, forwarded from the owned FrameSampler. Null /
    // zero / 1.0 when the engine is idle ; consumers treat a missing
    // sample as "engine not running".
    public SampledFrame? LatestSample => _sampler?.LatestSample;
    public int   GridCols     => _sampler?.GridCols ?? 0;
    public int   GridRows     => _sampler?.GridRows ?? 0;
    public bool  IsHdr        => _sampler?.IsHdr ?? false;
    public float ContentPeak  => _sampler?.ContentPeak ?? 1f;

    /// <summary>Pixel size of the captured display, or null while no
    /// capture runs. The Settings preview sizes its cells from it
    /// (width / GridCols × height / GridRows) so the grid keeps the
    /// screen's exact ratio.</summary>
    public Windows.Graphics.SizeInt32? SourceSize
        => _capture is { IsRunning: true } capture ? capture.ContentSize : null;

    // Error is a transient blip : the engine raises Off right after it,
    // so a subscriber gets two consecutive callbacks (Error then Off).
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
