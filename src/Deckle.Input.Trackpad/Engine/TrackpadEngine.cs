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
// pool thread, settings changes on the store's flush path — every entry
// point funnels through one gate lock; the work inside is microseconds
// (state machine + SendInput).
public sealed class TrackpadEngine : IDisposable
{
    // Anti-jump clamp as a fraction of the device's logical X range —
    // in-engine constant by decision (smoothing is never exposed).
    private const double MaxFrameDeltaRatio = 0.08;

    private readonly RawInputHost _host;
    private readonly MouseInjector _injector = new();
    private readonly ThreeFingerDragRecognizer _recognizer = new();
    private readonly System.Threading.Timer _graceTimer;
    private readonly object _gate = new();

    private bool _running;
    private double _dragStartedMs;
    private int _dragMoves;
    private bool _injectionFailureLogged;

    public TrackpadEngine(RawInputHost host)
    {
        _host = host;
        _graceTimer = new System.Threading.Timer(
            OnGraceTimer, null, Timeout.Infinite, Timeout.Infinite);

        _recognizer.DragStarted += OnDragStarted;
        _recognizer.DragMoved   += OnDragMoved;
        _recognizer.DragEnded   += OnDragEnded;
        _recognizer.TapIgnored  += () => DeckleTrackpadSource.Log.TapIgnored();
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;

            ApplyTuning();
            _host.FrameAssembled      += OnFrame;
            _host.TouchpadConnected   += OnTouchpadConnected;
            _host.TouchpadDisconnected += OnTouchpadDisconnected;
            TrackpadSettingsService.Instance.Changed += OnSettingsChanged;
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
            TrackpadSettingsService.Instance.Changed -= OnSettingsChanged;

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
        _dragStartedMs = RawInputHost.NowMs;
        _dragMoves = 0;
        _injectionFailureLogged = false;

        if (!_injector.PressPrimary())
            LogInjectionFailure("press");
        DeckleTrackpadSource.Log.DragStarted();
    }

    private void OnDragMoved(double dx, double dy)
    {
        var settings = TrackpadSettingsService.Instance.Current;
        double scale = settings.Tuning.BaseScale * settings.DragSpeed;

        if (!_injector.MoveRelative(dx * scale, dy * scale))
            LogInjectionFailure("move");
        _dragMoves++;
    }

    private void OnDragEnded(string reason)
    {
        if (!_injector.ReleasePrimary())
            LogInjectionFailure("release");
        DeckleTrackpadSource.Log.DragEnded(
            reason, Math.Round(RawInputHost.NowMs - _dragStartedMs, 0), _dragMoves);
    }

    // First failure of a drag is a Warning; the rest of the drag stays
    // quiet (a refused SendInput repeats at frame cadence — typically
    // UIPI against an elevated foreground window).
    private void LogInjectionFailure(string action)
    {
        if (_injectionFailureLogged) return;
        _injectionFailureLogged = true;
        DeckleTrackpadSource.Log.InjectionFailed(action, _injector.LastError);
    }

    // ── Device + settings observers ──────────────────────────────────────

    private void OnTouchpadConnected(TouchpadCapabilities caps)
    {
        lock (_gate) { if (_running) ApplyTuning(); }
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

    private void OnSettingsChanged()
    {
        lock (_gate) { if (_running) ApplyTuning(); }
    }

    // Thresholds are expressed relative to the device's logical range —
    // no absolute magic units; the tuning expander calibrates the ratios
    // until the value freeze.
    private void ApplyTuning()
    {
        var settings = TrackpadSettingsService.Instance.Current;
        var caps = _host.Touchpad;
        int xRange = caps?.XRange > 0 ? caps.XRange : 4096;

        _recognizer.GraceDelayMs        = settings.Tuning.GraceDelayMs;
        _recognizer.StartThresholdUnits = settings.Tuning.StartThresholdRatio * xRange;
        _recognizer.MaxFrameDeltaUnits  = MaxFrameDeltaRatio * xRange;

        DeckleTrackpadSource.Log.TuningApplied(
            settings.Tuning.GraceDelayMs,
            Math.Round(_recognizer.StartThresholdUnits, 1),
            settings.Tuning.BaseScale,
            settings.DragSpeed);
    }
}
