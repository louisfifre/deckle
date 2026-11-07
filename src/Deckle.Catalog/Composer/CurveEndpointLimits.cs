using System;

namespace Deckle.Catalog;

// One axis of a draggable endpoint: the bounds its value keeps and the step it
// lands on, the grain of the field that edits the same value. A drag or a key
// never writes a value the field could not show.
public sealed record AxisLimit(double Minimum, double Maximum, double Step)
{
    // Onto the step's grid, then into the bounds. The grid counts from zero, as
    // the field's arrows do; the bounds win when they sit off the grid. The
    // decimals the step needs are kept, so 3 × 0.05 lands on 0.15, not
    // 0.15000000000000002.
    public double Apply(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return Minimum;
        double snapped = Step > 0 ? Math.Round(value / Step, MidpointRounding.AwayFromZero) * Step : value;
        snapped = MagnitudeGrain.Snap(snapped, MagnitudeGrain.DecimalsFor(Step));
        return Math.Clamp(snapped, Minimum, Maximum);
    }
}

// Which coordinates of a curve's frame its two endpoint handles may move, and
// within what. The origin is the frame's (u,v) = (0,0) point, the end its (1,1)
// point (CurveFrame); a null axis is held where the frame puts it, and a handle
// with both axes null is not drawn. For the cut curve the origin is (ramp start,
// max wait) and the end (ramp end, min wait): one handle carries two anchors.
//
// A move keeps the frame drawable: the end never reaches the origin's x (a box
// of no width has no curve), and the vertical order the two endpoints had stays
// (a decay stays a decay; a flat line is allowed). Pure, so the rule is tested
// without a control. This is the per-point shape a timeline of several points
// would take (docs/research/research--courbe-manipulation-directe--2026-11-07.md).
public sealed record CurveEndpointLimits(
    AxisLimit? OriginX = null,
    AxisLimit? OriginY = null,
    AxisLimit? EndX = null,
    AxisLimit? EndY = null)
{
    public bool OriginMovable => OriginX is not null || OriginY is not null;
    public bool EndMovable => EndX is not null || EndY is not null;

    // The frame with its origin moved toward (x, y) in data units.
    public CurveFrame MoveOrigin(CurveFrame frame, double x, double y)
    {
        double nx = OriginX?.Apply(x) ?? frame.OriginX;
        double ny = OriginY?.Apply(y) ?? frame.OriginY;
        if (OriginX is not null)
        {
            double gap = OriginX.Step > 0 ? OriginX.Step : 0;
            nx = frame.EndX >= frame.OriginX
                ? Math.Min(nx, frame.EndX - gap)
                : Math.Max(nx, frame.EndX + gap);
        }
        if (OriginY is not null)
            ny = frame.OriginY >= frame.EndY ? Math.Max(ny, frame.EndY) : Math.Min(ny, frame.EndY);
        return frame with { OriginX = nx, OriginY = ny };
    }

    // The frame with its end moved toward (x, y) in data units.
    public CurveFrame MoveEnd(CurveFrame frame, double x, double y)
    {
        double nx = EndX?.Apply(x) ?? frame.EndX;
        double ny = EndY?.Apply(y) ?? frame.EndY;
        if (EndX is not null)
        {
            double gap = EndX.Step > 0 ? EndX.Step : 0;
            nx = frame.EndX >= frame.OriginX
                ? Math.Max(nx, frame.OriginX + gap)
                : Math.Min(nx, frame.OriginX - gap);
        }
        if (EndY is not null)
            ny = frame.OriginY >= frame.EndY ? Math.Min(ny, frame.OriginY) : Math.Max(ny, frame.OriginY);
        return frame with { EndX = nx, EndY = ny };
    }

    // An arrow key on a handle: whole steps along each axis (Shift's ten steps
    // are the caller's), routed through the same rule as a drag.
    public CurveFrame NudgeOrigin(CurveFrame frame, int dx, int dy)
        => MoveOrigin(frame, frame.OriginX + dx * (OriginX?.Step ?? 0), frame.OriginY + dy * (OriginY?.Step ?? 0));

    public CurveFrame NudgeEnd(CurveFrame frame, int dx, int dy)
        => MoveEnd(frame, frame.EndX + dx * (EndX?.Step ?? 0), frame.EndY + dy * (EndY?.Step ?? 0));
}
