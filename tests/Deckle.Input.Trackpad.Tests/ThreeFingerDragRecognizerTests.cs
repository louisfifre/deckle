using Deckle.Input;
using Deckle.Input.Trackpad;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

// Each test sets the thresholds its contract depends on, so it never relies
// on the recognizer's defaults.
[Trait("Category", "unit")]
public class ThreeFingerDragRecognizerTests
{
    // Only the timestamp and the tips matter to the recognizer.
    private static ContactFrame Frame(double t, params (int id, int x, int y, bool tip)[] contacts)
    {
        var array = contacts
            .Select(c => new TouchpadContact(c.id, c.x, c.y, c.tip, Confidence: true))
            .ToArray();
        return new ContactFrame(array, array.Length, ButtonDown: false, ScanTime: 0, TimestampMs: t, ReportCount: 1);
    }

    private sealed class Recorder
    {
        public int Started;
        public int Hovered;
        public int Tapped;
        public readonly List<(double dx, double dy)> Moves = new();
        public readonly List<string> Ended = new();
        public readonly List<string> Abandoned = new();
        // Every effect in emission order, for the contracts that hinge on it.
        public readonly List<string> Sequence = new();

        public Recorder(ThreeFingerDragRecognizer r)
        {
            r.HoverStarted += () => { Hovered++; Sequence.Add("hover"); };
            r.DragStarted += () => { Started++; Sequence.Add("press"); };
            r.TapIgnored += () => Tapped++;
            r.DragMoved += (dx, dy) => { Moves.Add((dx, dy)); Sequence.Add("move"); };
            r.DragEnded += reason => { Ended.Add(reason); Sequence.Add("release"); };
            r.HoverAbandoned += (reason, _) => { Abandoned.Add(reason); Sequence.Add("abandon"); };
        }
    }

    private static (int, int, int, bool)[] Three(int x) =>
        [(1, x, 0, true), (2, x, 0, true), (3, x, 0, true)];

    private static (int, int, int, bool)[] Four(int x) =>
        [(1, x, 0, true), (2, x, 0, true), (3, x, 0, true), (4, 999, 999, true)];

    // Four-finger gestures often land as three fingers first. Pressing at
    // once turned the fourth finger's arrival into a stray click; the
    // press now waits, and a fourth finger inside the wait presses nothing.
    [Fact]
    [Trait("Category", "regression")]
    public void FourthFingerBeforeThePressDeadlinePressesNothing()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, PressDelayMs = 50 };
        var rec = new Recorder(r);
        double beforeDeadline = r.PressDelayMs / 2;

        r.ProcessFrame(Frame(0, Three(0)));
        r.ProcessFrame(Frame(beforeDeadline / 2, Three(60)));
        Assert.Equal(DragPhase.Hovering, r.Phase);

        r.ProcessFrame(Frame(beforeDeadline, Four(70)));

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(0, rec.Started);
        Assert.Empty(rec.Ended);
        Assert.Equal(new[] { "fourth-finger" }, rec.Abandoned.ToArray());
    }

    [Fact]
    public void CursorFollowsAtOnceWhileThePressWaits()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, PressDelayMs = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(0, Three(0)));
        r.ProcessFrame(Frame(10, Three(60)));
        r.ProcessFrame(Frame(20, Three(80)));

        Assert.Equal(DragPhase.Hovering, r.Phase);
        Assert.Equal(1, rec.Hovered);
        Assert.Equal(0, rec.Started);
        Assert.Equal(new[] { 60.0, 20.0 }, rec.Moves.Select(m => m.dx).ToArray());
        Assert.Equal(r.PressDelayMs, r.PressDeadlineMs!.Value, precision: 6);
    }

    [Fact]
    public void FrameAtThePressDeadlineMovesThenPresses()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, PressDelayMs = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(0, Three(0)));
        r.ProcessFrame(Frame(10, Three(60)));
        r.ProcessFrame(Frame(r.PressDelayMs, Three(90)));

        Assert.Equal(DragPhase.Dragging, r.Phase);
        Assert.Equal(new[] { "hover", "move", "move", "press" }, rec.Sequence.ToArray());
    }

    [Fact]
    public void TickAtThePressDeadlinePressesWithoutAFrame()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, PressDelayMs = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(0, Three(0)));
        r.ProcessFrame(Frame(10, Three(60)));
        Assert.Equal(r.PressDeadlineMs, r.NextDeadlineMs);

        r.Tick(r.PressDeadlineMs!.Value - 1);
        Assert.Equal(0, rec.Started);

        r.Tick(r.PressDeadlineMs!.Value);
        Assert.Equal(1, rec.Started);
        Assert.Equal(DragPhase.Dragging, r.Phase);
        Assert.Null(r.NextDeadlineMs);
    }

    [Fact]
    public void LiftingBeforeThePressDeadlinePressesNothing()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, PressDelayMs = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(0, Three(0)));
        r.ProcessFrame(Frame(10, Three(60)));
        r.ProcessFrame(Frame(20));

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(0, rec.Started);
        Assert.Equal(new[] { "lifted" }, rec.Abandoned.ToArray());
    }

    [Fact]
    public void ThresholdCrossedAfterThePressDeadlinePressesWithoutHovering()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, PressDelayMs = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(0, Three(0)));
        r.ProcessFrame(Frame(r.PressDelayMs + 10, Three(60)));

        Assert.Equal(DragPhase.Dragging, r.Phase);
        Assert.Equal(new[] { "press", "move" }, rec.Sequence.ToArray());
    }

    [Fact]
    public void CancelWhileHoveringPressesNothing()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, PressDelayMs = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(0, Three(0)));
        r.ProcessFrame(Frame(10, Three(60)));
        r.Cancel("engine-stopped");

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Empty(rec.Ended);
        Assert.Equal(new[] { "engine-stopped" }, rec.Abandoned.ToArray());
    }

    [Fact]
    public void ThreeFingerTapBelowThresholdIsIgnoredWithNoDrag()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 5, 0, true), (2, 5, 0, true), (3, 5, 0, true)));
        r.ProcessFrame(Frame(3));

        Assert.Equal(1, rec.Tapped);
        Assert.Equal(0, rec.Started);
        Assert.Empty(rec.Moves);
        Assert.Empty(rec.Ended);
    }

    [Fact]
    public void TravelBeyondThresholdStartsDragAndFirstMoveReplaysAccumulatedTravel()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));

        Assert.Equal(1, rec.Started);
        Assert.Equal(DragPhase.Dragging, r.Phase);
        var first = Assert.Single(rec.Moves);
        Assert.Equal(60, first.dx, precision: 6);
        Assert.Equal(0, first.dy, precision: 6);
    }

    [Fact]
    public void WhileDraggingSubsequentFramesEmitCentroidDelta()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        rec.Moves.Clear(); // isolate the post-start delta
        r.ProcessFrame(Frame(3, (1, 70, 20, true), (2, 70, 20, true), (3, 70, 20, true)));

        var move = Assert.Single(rec.Moves);
        Assert.Equal(10, move.dx, precision: 6);
        Assert.Equal(20, move.dy, precision: 6);
    }

    [Fact]
    public void FourthFingerMidDragEndsAndReturningToThreeDoesNotRestart()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        rec.Moves.Clear();
        r.ProcessFrame(Frame(3,
            (1, 70, 0, true), (2, 70, 0, true), (3, 70, 0, true), (4, 999, 999, true)));

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(new[] { "fourth-finger" }, rec.Ended.ToArray());
        Assert.Empty(rec.Moves);

        r.ProcessFrame(Frame(4,
            (1, 80, 0, true), (2, 80, 0, true), (3, 80, 0, true)));
        r.ProcessFrame(Frame(5,
            (1, 140, 0, true), (2, 140, 0, true), (3, 140, 0, true)));

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(1, rec.Started);
        Assert.Equal(new[] { "fourth-finger" }, rec.Ended.ToArray());
    }

    [Fact]
    public void EngagementRequiresARisingEdge()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true), (4, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(3, (1, 80, 0, true), (2, 80, 0, true), (3, 80, 0, true)));

        Assert.Equal(0, rec.Started);
        Assert.Equal(DragPhase.Idle, r.Phase);

        r.ProcessFrame(Frame(4));
        r.ProcessFrame(Frame(5, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));

        Assert.Equal(DragPhase.Engaged, r.Phase);
    }

    [Fact]
    public void LiftingToTwoFingersEntersGraceAndReturningResumesSameDrag()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, GraceDelayMs = 100 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(10, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        Assert.Equal(DragPhase.Dragging, r.Phase);

        r.ProcessFrame(Frame(20, (1, 60, 0, true), (2, 60, 0, true)));
        Assert.Equal(DragPhase.Grace, r.Phase);
        Assert.Equal(120, r.GraceDeadlineMs!.Value, precision: 6);

        r.ProcessFrame(Frame(50, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        Assert.Equal(DragPhase.Dragging, r.Phase);
        Assert.Equal(1, rec.Started);
        Assert.Empty(rec.Ended);

        rec.Moves.Clear();
        r.ProcessFrame(Frame(60, (1, 70, 0, true), (2, 70, 0, true), (3, 70, 0, true)));
        var move = Assert.Single(rec.Moves);
        Assert.Equal(10, move.dx, precision: 6);
    }

    // Four-finger gestures belong to Windows: a fourth finger landing while the
    // drag waits out its grace window ends it at once, exactly as it does in
    // the Dragging phase, instead of resuming the drag for one more frame.
    [Fact]
    [Trait("Category", "regression")]
    public void FourFingersDuringGraceEndTheDragInsteadOfResumingIt()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, GraceDelayMs = 100 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(10, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        r.ProcessFrame(Frame(20, (1, 60, 0, true), (2, 60, 0, true))); // grace, deadline 120
        rec.Moves.Clear();

        r.ProcessFrame(Frame(50,
            (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true), (4, 999, 999, true)));

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(new[] { "fourth-finger" }, rec.Ended.ToArray());
        Assert.Empty(rec.Moves);
    }

    [Fact]
    public void GraceFrameAfterDeadlineEndsDragAsExpired()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, GraceDelayMs = 100 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(10, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        r.ProcessFrame(Frame(20, (1, 60, 0, true), (2, 60, 0, true))); // grâce, deadline 120

        r.ProcessFrame(Frame(200, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(new[] { "grace-expired" }, rec.Ended.ToArray());
    }

    [Fact]
    public void TickPastDeadlineEndsDragWithNoFrames()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, GraceDelayMs = 100 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(10, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        r.ProcessFrame(Frame(20, (1, 60, 0, true), (2, 60, 0, true))); // grâce, deadline 120

        r.Tick(120);

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(new[] { "grace-expired" }, rec.Ended.ToArray());
    }

    [Fact]
    public void FrameDeltaBeyondClampMovesNothingButDragSurvives()
    {
        var r = new ThreeFingerDragRecognizer
        {
            StartThresholdUnits = 50,
            MaxFrameDeltaUnits = 500,
        };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        rec.Moves.Clear();
        r.ProcessFrame(Frame(3, (1, 1060, 0, true), (2, 1060, 0, true), (3, 1060, 0, true)));

        Assert.Empty(rec.Moves);
        Assert.Equal(DragPhase.Dragging, r.Phase);
        Assert.Empty(rec.Ended);
    }

    [Fact]
    public void CancelDuringDragEndsOnceCancelWhenIdleEndsNothing()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.Cancel("noop");
        Assert.Empty(rec.Ended);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));

        r.Cancel("x");

        Assert.Equal(new[] { "x" }, rec.Ended.ToArray());
        Assert.Equal(DragPhase.Idle, r.Phase);
    }

    [Fact]
    public void TapThenImmediateRetouchEngagesAgainFromCleanState()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2));
        Assert.Equal(1, rec.Tapped);
        Assert.Equal(DragPhase.Idle, r.Phase);

        r.ProcessFrame(Frame(3, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(4, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));

        Assert.Equal(1, rec.Started);
        Assert.Equal(DragPhase.Dragging, r.Phase);
    }
}
