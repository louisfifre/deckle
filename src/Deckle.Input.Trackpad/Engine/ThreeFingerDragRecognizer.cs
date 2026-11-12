namespace Deckle.Input.Trackpad;

internal enum DragPhase
{
    Idle,     // fewer than three fingers, nothing pending
    Engaged,  // three fingers down, travel below the drag threshold (tap-vs-drag undecided)
    Hovering, // drag decided, cursor following, button press deferred to the press deadline
    Dragging, // primary button held, deltas flowing
    Grace,    // fingers lifted mid-drag, release deferred until the grace deadline
}

// The state machine that turns the contact-frame stream into drag
// intentions. Pure logic — no injection, no timers, no threads: time
// comes in on the frames and through Tick(), effects go out as events.
// That is what makes the quality-defining decisions testable frame by
// frame.
//
// The decisions it owns:
//   • finger lifts are read from the frames' tip switches, never
//     inferred from inter-frame silence (the reference implementation's
//     silence heuristic is the documented cause of its Magic Trackpad
//     issues);
//   • a three-finger tap is nothing — the drag only engages once travel
//     crosses the start threshold, and the accumulated travel is
//     replayed on start so the gesture feels anchored where the fingers
//     touched;
//   • engagement requires a rising edge (fewer than three tips on the
//     previous frame): settling from a four-finger gesture onto three
//     fingers must not start a drag;
//   • a fourth finger ends an active drag immediately: four-finger
//     gestures belong to Windows, and returning directly to three stays
//     disqualified by the same rising-edge rule;
//   • the button press waits PressDelayMs after the three fingers land,
//     while the cursor follows from the first frame. Fingers set down for
//     a four-finger gesture often reach three first: the fourth arrives
//     within ~45 ms in nine cases out of ten in recorded sessions, and a
//     press already made then turns into a stray click on release. A
//     fourth finger or a lift before the deadline abandons the hover with
//     nothing pressed; at the deadline the press lands at the point where
//     the cursor started (HoverStarted lets the host record it);
//   • travel cuts the wait short: once the fingers have moved
//     PressDistanceUnits since landing, the press lands at once. Fingers
//     settling for a four-finger gesture barely move before the fourth
//     arrives, while a hand that sets down and sweeps (a screenshot
//     selection) means a drag from the first millimetres — waiting out the
//     delay there made the drag start visibly late. The press comes at
//     whichever guard is met first;
//   • but the distance never presses inside FourthFingerWindowMs after
//     landing. Fingers set down for a four-finger swipe can already be
//     sweeping: in recorded sessions the distance pressed on the very
//     first frame, and the fourth finger, one frame later, released it as
//     a stray click that the swipe then carried. The delay alone still
//     presses inside the window if it is set shorter;
//   • lifting below three fingers during a drag opens a grace window;
//     three tips returning before the deadline resume the SAME drag, no
//     release-and-repress;
//   • per-frame deltas are the centroid of contacts matched by id
//     between consecutive frames — a finger swap or a glitch delta
//     beyond the hard anti-jump clamp moves nothing.
internal sealed class ThreeFingerDragRecognizer
{
    /// <summary>Release delay after fingers lift, in milliseconds. The engine sets the frozen value; settable for tests.</summary>
    public double GraceDelayMs { get; set; }

    /// <summary>Travel (logical units) before three fingers commit to a drag.</summary>
    public double StartThresholdUnits { get; set; } = 50;

    /// <summary>Delay after the three fingers land before the button is pressed, in milliseconds. 0 presses as soon as the drag starts.</summary>
    public double PressDelayMs { get; set; }

    /// <summary>Travel since the fingers landed (logical units) that presses before the delay runs out. Infinity leaves the press to the delay alone.</summary>
    public double PressDistanceUnits { get; set; } = double.PositiveInfinity;

    /// <summary>Time after the fingers land, in milliseconds, during which the distance cannot press: a fourth finger may still arrive. 0 lets the distance press at once.</summary>
    public double FourthFingerWindowMs { get; set; }

    /// <summary>Per-frame centroid delta above which the motion is discarded as a glitch.</summary>
    public double MaxFrameDeltaUnits { get; set; } = double.MaxValue;

    /// <summary>The drag is decided and the cursor starts moving; the press follows at the press deadline.</summary>
    public event Action? HoverStarted;
    /// <summary>Press the primary button now. After a hover, it belongs at the point where the cursor started.</summary>
    public event Action? DragStarted;
    /// <summary>Move the cursor, with or without the button held.</summary>
    public event Action<double, double>? DragMoved;
    public event Action<string>? DragEnded;
    /// <summary>The hover ended before the press: nothing was pressed. Carries the reason and the time since the fingers landed.</summary>
    public event Action<string, double>? HoverAbandoned;
    public event Action? TapIgnored;

    public DragPhase Phase { get; private set; } = DragPhase.Idle;

    /// <summary>Host-clock instant the pending grace release fires at; null outside Grace.</summary>
    public double? GraceDeadlineMs { get; private set; }

    /// <summary>Host-clock instant the deferred press fires at; null outside Hovering.</summary>
    /// <remarks>Travel already past the distance presses as soon as the fourth-finger window closes, frame or not.</remarks>
    public double? PressDeadlineMs => Phase == DragPhase.Hovering
        ? _engagedAtMs + (DistanceReached ? Math.Min(PressDelayMs, FourthFingerWindowMs) : PressDelayMs)
        : null;

    /// <summary>The next instant Tick must run at when no frame arrives, or null.</summary>
    public double? NextDeadlineMs => PressDeadlineMs ?? GraceDeadlineMs;

    private readonly Dictionary<int, (int X, int Y)> _lastTips = new();
    private int _lastTipCount;
    private double _engagedAtMs;
    private double _engagedTravelX;
    private double _engagedTravelY;

    public void ProcessFrame(ContactFrame frame)
    {
        int tips = frame.TipCount;

        switch (Phase)
        {
            case DragPhase.Idle:
                if (tips == 3 && _lastTipCount < 3)
                {
                    Phase = DragPhase.Engaged;
                    _engagedAtMs = frame.TimestampMs;
                    _engagedTravelX = 0;
                    _engagedTravelY = 0;
                    CaptureTips(frame);
                }
                break;

            case DragPhase.Engaged:
                if (tips != 3)
                {
                    // Lift = three-finger tap (deliberately nothing); a
                    // fourth finger = a different gesture, not ours.
                    if (tips < 3) TapIgnored?.Invoke();
                    ToIdle();
                    break;
                }

                if (TryMatchedDelta(frame, out double dx, out double dy))
                {
                    _engagedTravelX += dx;
                    _engagedTravelY += dy;
                    if (EngagedTravel >= StartThresholdUnits)
                    {
                        if (PressDue(frame.TimestampMs))
                        {
                            Phase = DragPhase.Dragging;
                            DragStarted?.Invoke();
                        }
                        else
                        {
                            Phase = DragPhase.Hovering;
                            HoverStarted?.Invoke();
                        }
                        DragMoved?.Invoke(_engagedTravelX, _engagedTravelY);
                    }
                }
                CaptureTips(frame);
                break;

            case DragPhase.Hovering:
                if (tips != 3)
                {
                    AbandonHover(tips > 3 ? "fourth-finger" : "lifted", frame.TimestampMs);
                    break;
                }

                if (TryMatchedDelta(frame, out dx, out dy))
                {
                    _engagedTravelX += dx;
                    _engagedTravelY += dy;
                    DragMoved?.Invoke(dx, dy);
                }
                CaptureTips(frame);

                // Moved first, pressed second: the press carries the cursor
                // back to where it started, so it must see this frame's
                // motion already applied.
                if (PressDue(frame.TimestampMs))
                    CommitPress();
                break;

            case DragPhase.Dragging:
                if (tips > 3)
                {
                    EndDrag("fourth-finger");
                    break;
                }

                if (tips < 3)
                {
                    Phase = DragPhase.Grace;
                    GraceDeadlineMs = frame.TimestampMs + GraceDelayMs;
                    _lastTips.Clear(); // stale on resume — repopulated from the resuming frame
                    break;
                }

                if (TryMatchedDelta(frame, out dx, out dy))
                    DragMoved?.Invoke(dx, dy);
                CaptureTips(frame);
                break;

            case DragPhase.Grace:
                if (frame.TimestampMs >= GraceDeadlineMs)
                {
                    EndDrag("grace-expired");
                    break;
                }

                if (tips > 3)
                {
                    // Same rule as while dragging: four fingers is Windows'.
                    EndDrag("fourth-finger");
                    break;
                }

                if (tips == 3)
                {
                    // Same drag resumes — the button never lifted.
                    Phase = DragPhase.Dragging;
                    GraceDeadlineMs = null;
                    CaptureTips(frame);
                }
                break;
        }

        _lastTipCount = tips;
    }

    /// <summary>Drives the press and grace deadlines between frames.</summary>
    public void Tick(double nowMs)
    {
        if (Phase == DragPhase.Hovering && PressDue(nowMs))
            CommitPress();
        else if (Phase == DragPhase.Grace && GraceDeadlineMs is not null && nowMs >= GraceDeadlineMs)
            EndDrag("grace-expired");
    }

    /// <summary>Forces a release (engine stop, device gone). Safe in any phase.</summary>
    public void Cancel(string reason)
    {
        if (Phase is DragPhase.Dragging or DragPhase.Grace)
            EndDrag(reason);
        else if (Phase == DragPhase.Hovering)
            AbandonHover(reason, _engagedAtMs);
        else
            ToIdle();
        _lastTipCount = 0;
    }

    // Net displacement since the fingers landed, not the path length: a
    // finger rolling back and forth while settling must not add up to a drag.
    private double EngagedTravel =>
        Math.Sqrt(_engagedTravelX * _engagedTravelX + _engagedTravelY * _engagedTravelY);

    private bool DistanceReached => EngagedTravel >= PressDistanceUnits;

    // The two guards on the press; whichever is met first lets it through,
    // the distance only once the fourth-finger window has closed. Travel
    // arrives on frames, so between frames Tick sees the travel of the last
    // one, which is all a window closing with no frame needs.
    private bool PressDue(double nowMs)
    {
        double elapsed = nowMs - _engagedAtMs;
        return elapsed >= PressDelayMs
            || (elapsed >= FourthFingerWindowMs && DistanceReached);
    }

    private void CommitPress()
    {
        Phase = DragPhase.Dragging;
        DragStarted?.Invoke();
    }

    private void AbandonHover(string reason, double nowMs)
    {
        double elapsed = Math.Max(0, nowMs - _engagedAtMs);
        ToIdle();
        HoverAbandoned?.Invoke(reason, elapsed);
    }

    private void EndDrag(string reason)
    {
        ToIdle();
        DragEnded?.Invoke(reason);
    }

    private void ToIdle()
    {
        Phase = DragPhase.Idle;
        GraceDeadlineMs = null;
        _lastTips.Clear();
        _engagedTravelX = 0;
        _engagedTravelY = 0;
    }

    // Centroid delta over the contacts present, tip down, in both this
    // frame and the previous one. False when nothing matched (fresh
    // touch, full id turnover) or the delta is a glitch beyond the
    // anti-jump clamp.
    private bool TryMatchedDelta(ContactFrame frame, out double dx, out double dy)
    {
        double sumX = 0, sumY = 0;
        int matched = 0;

        foreach (var contact in frame.Contacts)
        {
            if (!contact.Tip) continue;
            if (!_lastTips.TryGetValue(contact.Id, out var last)) continue;
            sumX += contact.X - last.X;
            sumY += contact.Y - last.Y;
            matched++;
        }

        if (matched == 0)
        {
            dx = 0; dy = 0;
            return false;
        }

        dx = sumX / matched;
        dy = sumY / matched;
        return Math.Sqrt(dx * dx + dy * dy) <= MaxFrameDeltaUnits;
    }

    private void CaptureTips(ContactFrame frame)
    {
        _lastTips.Clear();
        foreach (var contact in frame.Contacts)
            if (contact.Tip)
                _lastTips[contact.Id] = (contact.X, contact.Y);
    }
}
