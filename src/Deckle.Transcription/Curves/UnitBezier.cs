using System;

namespace Deckle.Transcription;

// A cubic Bézier easing on the unit square, with fixed endpoints P0 = (0,0) and
// P3 = (1,1) and two free control points P1 = (X1,Y1), P2 = (X2,Y2). This is the
// exact function CSS `cubic-bezier(x1,y1,x2,y2)` defines : given an input x in
// [0,1] it returns the curve's y at that x.
//
// The curve is parametric — both x and y are cubics in t — so evaluating y at a
// given x means first inverting x(t) = x for t, then reading y(t). x(t) is kept
// monotone (hence single-valued) by clamping X1, X2 to [0,1] ; the inversion is
// Newton-Raphson with a bisection fallback, the same scheme WebKit's UnitBezier
// uses for the CSS timing functions. A handful of iterations land within 1e-6,
// so the whole Solve is a few microseconds — cheap enough to call per frame.
//
// Used in two places, one model :
//   • EnergySegmenter.RequiredHangoverFrames — the runtime decision delay decay.
//   • HangoverCurveCanvas — the Playground plot of that same decay.
// Sharing this one type is why neither has to hand-mirror the curve math.
internal readonly struct UnitBezier
{
    // Polynomial coefficients of x(t) and y(t) expanded from the control points :
    // x(t) = ((ax·t + bx)·t + cx)·t, likewise for y. Precomputed once so Solve is
    // pure arithmetic.
    private readonly double _ax, _bx, _cx;
    private readonly double _ay, _by, _cy;

    public UnitBezier(double x1, double y1, double x2, double y2)
    {
        // Clamp the control x's so x(t) stays monotone non-decreasing : a control
        // point outside [0,1] on x would make the function multi-valued.
        x1 = Math.Clamp(x1, 0.0, 1.0);
        x2 = Math.Clamp(x2, 0.0, 1.0);

        _cx = 3.0 * x1;
        _bx = 3.0 * (x2 - x1) - _cx;
        _ax = 1.0 - _cx - _bx;

        _cy = 3.0 * y1;
        _by = 3.0 * (y2 - y1) - _cy;
        _ay = 1.0 - _cy - _by;
    }

    private double SampleX(double t) => ((_ax * t + _bx) * t + _cx) * t;
    private double SampleY(double t) => ((_ay * t + _by) * t + _cy) * t;
    private double SampleDerivativeX(double t) => (3.0 * _ax * t + 2.0 * _bx) * t + _cx;

    // Invert x(t) = x for t ∈ [0,1]. Newton-Raphson from t = x (a good seed since
    // x(t) ≈ t near the diagonal), then a bisection fallback for the rare cases
    // where the derivative stalls.
    private double SolveForT(double x, double epsilon)
    {
        double t = x;
        for (int i = 0; i < 8; i++)
        {
            double dx = SampleX(t) - x;
            if (Math.Abs(dx) < epsilon) return t;
            double d = SampleDerivativeX(t);
            if (Math.Abs(d) < 1e-6) break;
            t -= dx / d;
        }

        double t0 = 0.0, t1 = 1.0;
        t = x;
        if (t < t0) return t0;
        if (t > t1) return t1;
        while (t0 < t1)
        {
            double xt = SampleX(t);
            if (Math.Abs(xt - x) < epsilon) return t;
            if (x > xt) t0 = t; else t1 = t;
            t = (t1 - t0) * 0.5 + t0;
        }
        return t;
    }

    // y at the given x, both in [0,1]. The input is clamped so an out-of-range
    // progress can't escape the unit interval.
    public double Solve(double x, double epsilon = 1e-6)
        => SampleY(SolveForT(Math.Clamp(x, 0.0, 1.0), epsilon));
}
