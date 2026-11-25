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
// One pipeline shape: a colour per screen edge (Louis, 2026-11-25, grill
// answer r10q4 B; the single-colour group mode left). One sRGB sample per
// screen-border zone (top / bottom / left / right), broadcast to every light
// that follows that zone (LightZoneAssignment: the person's pick, else the
// edge its place in the Entertainment area suggests), pushed in a single
// batch via <see cref="IMultiLightOutput.SetLightColorsAsync"/> at the rate
// the driver advertises (Hue Entertainment: 50 Hz). The four zones are
// sampled once per tick from a border band (AmbientSettings.BorderMode /
// BorderDepth / BorderCells), HyperHDR's <c>horizontalDepth</c> /
// <c>verticalDepth</c> collapsed to one user value.
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
    // FrameProcessingFailed was written: that Error is the incident's end.
    private bool _frameProcessingIncidentEnded;
    private int _frameProcessingStopRequested;

    private CancellationTokenSource? _cts;
    private Task? _pushLoopTask;
    private long _startTimestamp;
    private bool _disposed;

    // Reason tag read by Stop() when emitting PipelineStopping and PipelineStopped. Internal
    // triggers set it before they post Stop() to the thread pool ; the
    // toggle path leaves "user". StartAsync resets it so an internal stop
    // doesn't leak into the next session's log.
    private string _stopReason = "user";
    private string? _startAbortReason;
    private int _stopRequested;

    // Pairs the log's start and end of one run (WorkId). Set when
    // StartAsync begins; Stop() copies it before its cleanup runs.
    private string _runId = "";

    // Serialises an upstream abort against the Starting → Running switch:
    // without it a push-loop crash could land after the last start check
    // and leave a Running engine whose loop is already dead. The first
    // abort of a run wins; later ones would only repeat its Error.
    private readonly object _lifecycleLock = new();
    private bool _abortRequested;

    // No watch on the bridge's event stream: while the Entertainment area
    // streams, the bridge gives Deckle the lights and ignores other writes to
    // them, so nothing outside can change them under the stream. The watch
    // served the REST path, which wrote alongside the Hue app and Home
    // Assistant; it left with that path on 2026-11-25.

    // Deferred-cleanup task spun by Stop() so the UI thread that
    // triggered the stop returns immediately while the DXGI duplication
    // + sampler GPU buffers + Hue Entertainment output get torn down on the
    // thread pool. StartAsync / DisposeAsync await this before they
    // touch the engine's owned deps, so a Start + Stop + Start sequence
    // serialises cleanly without holding the duplication past Stop.
    // Null when no cleanup is in flight (cold-start state).
    private Task? _stopCleanupTask;

    private Dictionary<string, (int R, int G, int B)>? _multiLastPushed;

    private IReadOnlyList<LightDescriptor>? _multiLights;

    // The edge each light's place in the Entertainment area suggests, read
    // at start; a light the person never assigned follows it.
    private IReadOnlyDictionary<string, LightZone>? _suggestedZones;
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
    private int  _hbPushFailures;

    // Wall-clock cost of each push await, for the heartbeat : the
    // Entertainment DTLS/UDP send plus any local back-pressure. Drops are not
    // counted.
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
    private BorderThicknessMode _borderMode   = BorderThicknessMode.Share;
    private double              _borderDepth  = 0.33;
    private int                 _borderCells  = 8;
    // EMA step at the 15 Hz reference cadence ; 1.0 = no smoothing.
    // AmbientPushCadence adapts it to the measured tick interval so a
    // faster stream adds intermediate steps without converging faster in
    // wall-clock time.
    private double _smoothingAlpha       = 1.0;

    // EMA state, keyed by fixture id. Cleared on every StartAsync so a
    // re-pair doesn't carry over the previous setup's smoothing trail.
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
    /// gate let them through. Keyed by fixture id. Empty before the first
    /// tick. Safe to call from any thread.</summary>
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

    /// <summary>Lights resolved from the driver at Start. Null when the
    /// engine isn't running.</summary>
    public IReadOnlyList<LightDescriptor>? MultiLights => _multiLights;

    /// <summary>The screen edge each light of the run follows right now:
    /// the person's pick, else the edge its place in the Entertainment area
    /// suggests. Empty when the engine isn't running.</summary>
    public IReadOnlyDictionary<string, LightZone> EffectiveZones()
    {
        var lights = _multiLights;
        return lights is null
            ? new Dictionary<string, LightZone>()
            : LightZoneAssignment.ResolveAll(
                lights.Select(l => l.Id), _host.Ambient.LightZones, _suggestedZones);
    }

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

    /// <summary>Frames the capture has delivered since it started, or zero
    /// while no capture runs. The preview reads it twice to measure the
    /// cadence the capture really holds, against the rate the user set.</summary>
    public long CapturedFrames
        => _capture is { IsRunning: true } capture ? capture.FrameCount : 0;

    /// <summary>True while the engine holds its light output, the Hue
    /// Entertainment stream. The preview names it as the transport.</summary>
    public bool HasOutput => _output is not null;

    /// <summary>Why the last start failed, set before the Error state is
    /// raised so a subscriber reads it on Error. Meaningless before the
    /// first failure.</summary>
    public AmbientStartFailure LastStartFailure { get; private set; }

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
            DeckleAmbientSource.Log.StateChangedSubscriberThrew(ex.GetType().Name, ex.Message);
        }
    }

    private void AbortStartOrStop(string reason, Action log)
    {
        bool stopNow;
        string? ignoredWhy = null;
        lock (_lifecycleLock)
        {
            var state = _state;
            if (state == AmbientEngineState.Off || state == AmbientEngineState.Stopping)
                ignoredWhy = "already_stopping";
            else if (_abortRequested)
                ignoredWhy = "abort_pending";

            if (ignoredWhy is null)
            {
                _abortRequested = true;
                _stopReason = reason;
                stopNow = IsRunning;
                if (!stopNow) _startAbortReason = reason;
            }
            else
            {
                stopNow = false;
            }
        }

        if (ignoredWhy is not null)
        {
            // The first abort of a run owns the consequence line; this one
            // leaves a trace of the cause it carried instead of vanishing.
            DeckleAmbientSource.Log.PipelineAbortIgnoredDetail(reason, ignoredWhy);
            return;
        }

        log();

        if (stopNow) Task.Run(Stop);
    }
    private void ThrowIfStartAbortRequested()
    {
        if (_startAbortReason is { } reason)
            throw new InvalidOperationException($"Ambient start aborted by upstream stop: {reason}");
    }

    // The final start check and the Running switch happen under the abort
    // lock, so an abort is either seen here or sees IsRunning and stops.
    private void CommitRunningOrThrow()
    {
        lock (_lifecycleLock)
        {
            ThrowIfStartAbortRequested();
            IsRunning = true;
        }
    }

}
