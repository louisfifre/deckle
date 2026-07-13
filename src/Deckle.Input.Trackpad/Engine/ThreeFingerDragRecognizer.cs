namespace Deckle.Input.Trackpad;

internal enum DragPhase
{
    Idle,     // fewer than three fingers, nothing pending
    Engaged,  // three fingers down, travel below the drag threshold (tap-vs-drag undecided)
    Dragging, // primary button held, deltas flowing
    Grace,    // fingers lifted mid-drag, release deferred until the grace deadline
}

// The state machine that turns the contact-frame stream into drag
// intentions (CONTEXT.md § Input). Pure logic — no injection, no
// timers, no threads: time comes in on the frames and through Tick(),
// effects go out as events. That is what makes the quality-defining
// decisions testable frame by frame.
//
// The decisions it owns, per the framing session:
//   • finger lifts are read from the frames' tip switches, never
//     inferred from inter-frame silence (the reference's 40 ms
//     heuristic is the documented cause of its Magic Trackpad issues);
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

    /// <summary>Per-frame centroid delta above which the motion is discarded as a glitch.</summary>
    public double MaxFrameDeltaUnits { get; set; } = double.MaxValue;

    public event Action? DragStarted;
    public event Action<double, double>? DragMoved;
    public event Action<string>? DragEnded;
    public event Action? TapIgnored;

    public DragPhase Phase { get; private set; } = DragPhase.Idle;

    /// <summary>Host-clock instant the pending grace release fires at; null outside Grace.</summary>
    public double? GraceDeadlineMs { get; private set; }

    private readonly Dictionary<int, (int X, int Y)> _lastTips = new();
    private int _lastTipCount;
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
                    double travel = Math.Sqrt(
                        _engagedTravelX * _engagedTravelX + _engagedTravelY * _engagedTravelY);
                    if (travel >= StartThresholdUnits)
                    {
                        Phase = DragPhase.Dragging;
                        DragStarted?.Invoke();
                        DragMoved?.Invoke(_engagedTravelX, _engagedTravelY);
                    }
                }
                CaptureTips(frame);
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

                if (tips >= 3)
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

    /// <summary>Drives the grace deadline when no frames arrive (fingers fully lifted).</summary>
    public void Tick(double nowMs)
    {
        if (Phase == DragPhase.Grace && GraceDeadlineMs is not null && nowMs >= GraceDeadlineMs)
            EndDrag("grace-expired");
    }

    /// <summary>Forces a release (engine stop, device gone). Safe in any phase.</summary>
    public void Cancel(string reason)
    {
        if (Phase is DragPhase.Dragging or DragPhase.Grace)
            EndDrag(reason);
        else
            ToIdle();
        _lastTipCount = 0;
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
