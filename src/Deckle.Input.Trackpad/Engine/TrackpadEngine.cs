using Deckle.Input;

namespace Deckle.Input.Trackpad;

// Binds the chain together for the lifetime of the master switch:
// RawInputHost frames → recognizer → MouseInjector. Owns the grace
// timer (the recognizer is pure; somebody must fire the release when
// the fingers are gone and no frames arrive anymore) and the safety
// releases (engine stop, touchpad disconnect — an injected button must
// never stay held).
//
// Threading: frames arrive on the input thread, the grace timer on a
// pool thread — every entry point funnels through one gate lock; the
// work inside is microseconds (state machine + SendInput).
public sealed class TrackpadEngine : IDisposable
{
    // Anti-jump clamp as a fraction of the device's logical X range —
    // in-engine constant by decision (smoothing is never exposed).
    private const double MaxFrameDeltaRatio = 0.08;

    // Baseline logical-units → mickeys factor the drag-speed multiplier
    // rides on. In-engine constant by decision: sensitivity is the one
    // linear drag-speed slider, nothing else.
    private const double BaseScale = 0.25;

    // Values frozen 2026-06-12 after hands-on calibration (maintainer's
    // choices). No grace: the drag releases the instant the fingers lift.
    private const double GraceDelayMs = 0;

    // Start threshold as a fraction of the X range — the calibrated
    // minimum, perceptually instant. Not zero on purpose: at exactly 0 the
    // drag would commit on the first frame even without movement, turning
    // a three-finger tap into a left click — and tap is deliberately
    // nothing (framing decision).
    private const double StartThresholdRatio = 0.001;

    private readonly RawInputHost _host;
    private readonly MouseInjector _injector = new();
    private readonly ThreeFingerDragRecognizer _recognizer = new();
    private readonly System.Threading.Timer _graceTimer;
    private readonly object _gate = new();

    private bool _running;
    private double _dragStartedMs;
    private int _dragMoves;
    private bool _traceDrag;
    private bool _injectionIncidentOpen;

    public TrackpadEngine(RawInputHost host)
    {
        _host = host;
        _graceTimer = new System.Threading.Timer(
            OnGraceTimer, null, Timeout.Infinite, Timeout.Infinite);

        _recognizer.DragStarted += OnDragStarted;
        _recognizer.DragMoved   += OnDragMoved;
        _recognizer.DragEnded   += OnDragEnded;
        _recognizer.TapIgnored  += OnTapIgnored;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            _injectionIncidentOpen = false;

            ApplyThresholds();
            _host.FrameAssembled      += OnFrame;
            _host.TouchpadConnected   += OnTouchpadConnected;
            _host.TouchpadDisconnected += OnTouchpadDisconnected;
        }
        DeckleTrackpadSource.Log.EngineStarted();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;

            _host.FrameAssembled      -= OnFrame;
            _host.TouchpadConnected   -= OnTouchpadConnected;
            _host.TouchpadDisconnected -= OnTouchpadDisconnected;

            _graceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _recognizer.Cancel("engine-stopped");
        }
        DeckleTrackpadSource.Log.EngineStopped();
    }

    public void Dispose()
    {
        Stop();
        _graceTimer.Dispose();
    }

    // ── Frame path (input thread) ────────────────────────────────────────

    private void OnFrame(ContactFrame frame)
    {
        lock (_gate)
        {
            if (!_running) return;
            _recognizer.ProcessFrame(frame);
            ArmGraceTimer();
        }
    }

    private void OnGraceTimer(object? state)
    {
        lock (_gate)
        {
            if (!_running) return;
            _recognizer.Tick(RawInputHost.NowMs);
            ArmGraceTimer();
        }
    }

    // One-shot timer at the grace deadline; re-armed after every frame so
    // a deadline pushed back by a resumed-then-lifted drag stays accurate.
    private void ArmGraceTimer()
    {
        if (_recognizer.GraceDeadlineMs is double deadline)
        {
            double due = Math.Max(1, deadline - RawInputHost.NowMs);
            _graceTimer.Change((int)Math.Ceiling(due), Timeout.Infinite);
        }
        else
        {
            _graceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    // ── Recognizer effects (called under _gate) ──────────────────────────

    private void OnDragStarted()
    {
        _traceDrag = DeckleTrackpadSource.IsInputActivityDetailEnabled(
            System.Diagnostics.Tracing.EventLevel.Verbose,
            (System.Diagnostics.Tracing.EventKeywords)Deckle.Diagnostics.Keywords.Capture);
        if (_traceDrag)
        {
            _dragStartedMs = RawInputHost.NowMs;
            _dragMoves = 0;
        }

        if (_injector.PressPrimary())
            CloseInjectionIncident();
        else
            LogInjectionFailure("press");
        if (_traceDrag)
            DeckleTrackpadSource.Log.DragStarted();
    }

    private void OnDragMoved(double dx, double dy)
    {
        var settings = TrackpadSettingsService.Instance.Current;
        double scale = BaseScale * settings.DragSpeed;

        if (_injector.MoveRelative(dx * scale, dy * scale))
            CloseInjectionIncident();
        else
            LogInjectionFailure("move");
        if (_traceDrag && DeckleTrackpadSource.IsInputActivityDetailEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Deckle.Diagnostics.Keywords.Capture))
            _dragMoves++;
    }

    private void OnDragEnded(string reason)
    {
        if (_injector.ReleasePrimary())
            CloseInjectionIncident();
        else
            LogInjectionFailure("release");
        if (_traceDrag && DeckleTrackpadSource.IsInputActivityDetailEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Deckle.Diagnostics.Keywords.Capture))
        {
            DeckleTrackpadSource.Log.DragEnded(
                reason, Math.Round(RawInputHost.NowMs - _dragStartedMs, 0), _dragMoves);
        }
        _traceDrag = false;
    }

    // One Warning opens an engine-wide incident. A refused SendInput can
    // repeat at frame cadence (typically UIPI against an elevated foreground
    // window), so subsequent failures stay quiet until a successful injection
    // proves that the path recovered.
    private void LogInjectionFailure(string action)
    {
        if (_injectionIncidentOpen) return;
        _injectionIncidentOpen = true;
        DeckleTrackpadSource.Log.InjectionFailed();
        DeckleTrackpadSource.Log.InjectionFailedDetail(action, _injector.LastError);
    }

    private void CloseInjectionIncident()
    {
        if (!_injectionIncidentOpen) return;
        _injectionIncidentOpen = false;
        DeckleTrackpadSource.Log.InjectionRecovered();
    }

    private static void OnTapIgnored()
    {
        if (DeckleTrackpadSource.IsInputActivityDetailEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Deckle.Diagnostics.Keywords.Capture))
        {
            DeckleTrackpadSource.Log.TapIgnored();
        }
    }

    // ── Device + settings observers ──────────────────────────────────────

    private void OnTouchpadConnected(TouchpadCapabilities caps)
    {
        lock (_gate) { if (_running) ApplyThresholds(); }
    }

    private void OnTouchpadDisconnected()
    {
        lock (_gate)
        {
            if (!_running) return;
            _graceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _recognizer.Cancel("touchpad-disconnected");
        }
    }

    // Thresholds are expressed relative to the device's logical range —
    // no absolute magic units. Re-applied on device connect because the
    // range comes from the device's capabilities. Drag speed needs no
    // re-apply hook: OnDragMoved reads it from the settings live.
    private void ApplyThresholds()
    {
        var caps = _host.Touchpad;
        int xRange = caps?.XRange > 0 ? caps.XRange : 4096;

        _recognizer.GraceDelayMs        = GraceDelayMs;
        _recognizer.StartThresholdUnits = StartThresholdRatio * xRange;
        _recognizer.MaxFrameDeltaUnits  = MaxFrameDeltaRatio * xRange;
    }
}
