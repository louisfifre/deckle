using Deckle.Input;

namespace Deckle.Input.Trackpad;

// Binds the chain together for the lifetime of the master switch:
// RawInputHost frames → recognizer → MouseInjector. Owns the deadline
// timer (the recognizer is pure; somebody must fire the deferred press
// or the grace release when no frame arrives at that instant) and the safety
// releases (engine stop, touchpad disconnect — an injected button must
// never stay held).
//
// Threading: frames arrive on the input thread, the deadline timer on a
// pool thread — every entry point funnels through one gate lock; the
// work inside is microseconds (state machine + SendInput).
public sealed class TrackpadEngine : IDisposable
{
    // Anti-jump clamp as a fraction of the device's logical X range — an
    // engine constant: smoothing is never exposed.
    private const double MaxFrameDeltaRatio = 0.08;

    // Baseline logical-units → mickeys factor the drag-speed multiplier
    // rides on. An engine constant: sensitivity is the one linear
    // drag-speed slider, nothing else.
    private const double BaseScale = 0.25;

    // Frozen calibration. No grace: the drag releases the instant the
    // fingers lift.
    private const double GraceDelayMs = 0;

    // How long after three fingers land the pick-up distance waits for a
    // possible fourth finger before it may press. Measured on 13 recorded
    // Magic Trackpad sessions (30 ms delay, 8 px distance): frames come every
    // ~11 ms and the fourth finger lands one or two frames after the three;
    // 20 ms cuts the stray presses of four-finger swipes from 35 to 13 and
    // still lets a sweeping hand press on its second frame (~22 ms), where
    // a window of 25 ms or more would leave the distance nothing to do. An
    // engine constant: it is a property of the hand, not a preference.
    private const double FourthFingerWindowMs = 20;

    // How long after landing a fourth finger still withdraws a drag already
    // pressed, releasing it where it was pressed. On 34 recorded sessions the
    // fourth finger of a four-finger gesture that the pad reports late lands
    // 44 to 191 ms after the three; a fourth finger later than that is one
    // added on purpose in the middle of a drag, which ends where it stands.
    private const double LateFourthFingerWindowMs = 200;

    // Start threshold as a fraction of the X range — the calibrated
    // minimum, perceptually instant. Not zero on purpose: at exactly 0 the
    // drag would commit on the first frame even without movement, turning
    // a three-finger tap into a left click — and tap is deliberately
    // nothing.
    private const double StartThresholdRatio = 0.001;

    private readonly RawInputHost _host;
    private readonly MouseInjector _injector = new();
    private readonly ThreeFingerDragRecognizer _recognizer = new();
    private readonly System.Threading.Timer _deadlineTimer;
    private readonly object _gate = new();

    private bool _running;
    private double _dragStartedMs;
    private int _dragMoves;
    private bool _traceDrag;
    private bool _injectionIncidentOpen;
    // Counted for every drag, traced or not, so the stop milestone says how
    // much the engine was used without Verbose admission.
    private long _drags;

    public TrackpadEngine(RawInputHost host)
    {
        _host = host;
        _deadlineTimer = new System.Threading.Timer(
            OnDeadlineTimer, null, Timeout.Infinite, Timeout.Infinite);

        _recognizer.HoverStarted   += OnHoverStarted;
        _recognizer.DragStarted    += OnDragStarted;
        _recognizer.DragMoved      += OnDragMoved;
        _recognizer.DragEnded      += OnDragEnded;
        _recognizer.DragWithdrawn  += OnDragWithdrawn;
        _recognizer.HoverAbandoned += OnHoverAbandoned;
        _recognizer.TapIgnored     += OnTapIgnored;
    }

    public void Start()
    {
        bool touchpad;
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            // No incident to reset here: Stop always ends an open one.
            _drags = 0;

            ApplyThresholds();
            _host.FrameAssembled      += OnFrame;
            _host.TouchpadConnected   += OnTouchpadConnected;
            _host.TouchpadDisconnected += OnTouchpadDisconnected;
            touchpad = _host.Touchpad is not null;
        }
        DeckleTrackpadSource.Log.EngineStarted(
            TrackpadSettingsService.Instance.Current.DragSpeed, touchpad);
    }

    public void Stop()
    {
        long drags;
        lock (_gate)
        {
            if (!_running) return;
            _running = false;

            _host.FrameAssembled      -= OnFrame;
            _host.TouchpadConnected   -= OnTouchpadConnected;
            _host.TouchpadDisconnected -= OnTouchpadDisconnected;

            _deadlineTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _recognizer.Cancel("engine-stopped");
            // The incident cannot close by a successful injection anymore, so
            // it is ended here; otherwise the log would hold a Warning
            // that never ends.
            if (_injectionIncidentOpen)
                EndInjectionIncident("engine_stopped");
            drags = _drags;
        }
        DeckleTrackpadSource.Log.EngineStopped("completed", drags);
    }

    public void Dispose()
    {
        Stop();
        _deadlineTimer.Dispose();
    }

    // ── Frame path (input thread) ────────────────────────────────────────

    private void OnFrame(ContactFrame frame)
    {
        lock (_gate)
        {
            if (!_running) return;
            ApplyPressGuards(_recognizer, TrackpadSettingsService.Instance.Current);
            _recognizer.ProcessFrame(frame);
            ArmDeadlineTimer();
        }
    }

    private void OnDeadlineTimer(object? state)
    {
        lock (_gate)
        {
            if (!_running) return;
            ApplyPressGuards(_recognizer, TrackpadSettingsService.Instance.Current);
            _recognizer.Tick(RawInputHost.NowMs);
            ArmDeadlineTimer();
        }
    }

    // One-shot timer at the recognizer's next deadline (deferred press or
    // grace release); re-armed after every frame so a deadline that moved
    // stays accurate. The press deadline needs it too: a hand held still
    // on the pad may send no frame at the instant the press is due.
    private void ArmDeadlineTimer()
    {
        if (_recognizer.NextDeadlineMs is double deadline)
        {
            double due = Math.Max(1, deadline - RawInputHost.NowMs);
            _deadlineTimer.Change((int)Math.Ceiling(due), Timeout.Infinite);
        }
        else
        {
            _deadlineTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    // ── Recognizer effects (called under _gate) ──────────────────────────

    // The cursor is about to move ahead of the button: the injector notes
    // where it stands so the deferred press lands there.
    private void OnHoverStarted() => _injector.MarkPressOrigin();

    private void OnHoverAbandoned(string reason, double elapsedMs)
    {
        _injector.ClearPressOrigin();
        if (DeckleTrackpadSource.IsInputActivityDetailEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Deckle.Diagnostics.Keywords.Capture))
        {
            DeckleTrackpadSource.Log.PressWithheld(reason, Math.Round(elapsedMs, 0));
        }
    }

    private void OnDragStarted()
    {
        _drags++;
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

    private void OnDragEnded(string reason) => EndInjectedDrag(reason, atPress: false);

    // A late fourth finger: the release goes back to the press point, so
    // what the drag stretched or moved returns before Windows takes the
    // four-finger gesture.
    private void OnDragWithdrawn() => EndInjectedDrag("fourth-finger-withdrawn", atPress: true);

    private void EndInjectedDrag(string reason, bool atPress)
    {
        bool released = atPress ? _injector.ReleasePrimaryAtPress() : _injector.ReleasePrimary();
        if (released)
            CloseInjectionIncident();
        else
        {
            LogInjectionFailure("release");
            DeckleTrackpadSource.Log.PrimaryReleaseFailed(_injector.LastError);
        }
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
        DeckleTrackpadSource.Log.InjectionFailed(action, _injector.LastError);
    }

    private void CloseInjectionIncident()
    {
        if (!_injectionIncidentOpen) return;
        EndInjectionIncident("injection_succeeded");
    }

    // Caller holds _gate.
    private void EndInjectionIncident(string reason)
    {
        _injectionIncidentOpen = false;
        DeckleTrackpadSource.Log.InjectionIncidentEnded(reason);
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
            _deadlineTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _recognizer.Cancel("touchpad-disconnected");
        }
    }

    // Thresholds are expressed relative to the device's logical range —
    // no absolute magic units. Re-applied on device connect because the
    // range comes from the device's capabilities. The person's settings
    // need no re-apply hook: OnDragMoved reads the drag speed live, and the
    // press guards are copied before every frame and every deadline.
    private void ApplyThresholds()
    {
        var caps = _host.Touchpad;
        int xRange = caps?.XRange > 0 ? caps.XRange : 4096;

        _recognizer.GraceDelayMs         = GraceDelayMs;
        _recognizer.FourthFingerWindowMs = FourthFingerWindowMs;
        _recognizer.LateFourthFingerWindowMs = LateFourthFingerWindowMs;
        _recognizer.StartThresholdUnits  = StartThresholdRatio * xRange;
        _recognizer.MaxFrameDeltaUnits   = MaxFrameDeltaRatio * xRange;
        ApplyPressGuards(_recognizer, TrackpadSettingsService.Instance.Current);
    }

    // Read live, like the drag speed: a slider move takes effect on the
    // next frame, with no settings-changed subscription to keep in step.
    // A drag already hovering re-reads its deadline, which the recognizer
    // computes from the delay on demand. Clamped because the settings file
    // can be edited by hand; the page never leaves the range.
    //
    // The distance is set in cursor pixels and the recognizer counts finger
    // units, so it goes back through the same scale OnDragMoved applies.
    // Approximate on purpose: Windows pointer ballistics sit between the
    // injected mickeys and the pixels, and a few pixels either way do not
    // change what the guard is for.
    internal static void ApplyPressGuards(ThreeFingerDragRecognizer recognizer, TrackpadSettings settings)
    {
        recognizer.PressDelayMs = Math.Clamp(
            settings.PressDelayMs, TrackpadSettings.MinPressDelayMs, TrackpadSettings.MaxPressDelayMs);

        double distancePx = Math.Clamp(
            settings.PressDistancePx, TrackpadSettings.MinPressDistancePx, TrackpadSettings.MaxPressDistancePx);
        double unitsToPx = BaseScale * Math.Max(settings.DragSpeed, 0.01);
        recognizer.PressDistanceUnits = distancePx / unitsToPx;
    }
}
