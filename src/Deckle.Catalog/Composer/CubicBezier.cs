using System;

namespace Deckle.Catalog;

// The two free control points of a cubic Bézier on the unit square, the value a
// Curve setting reads and writes. P0 = (0,0) and P3 = (1,1) are fixed; X stays in
// [0,1] so the curve is a function of x; Y may leave [0,1] when the editor allows
// it (an overshoot, as CSS cubic-bezier permits). A record so the composer's
// dirty check compares values, not references.
public sealed record CurvePoints(double X1, double Y1, double X2, double Y2);

// The frame a Curve setting draws in: the data space of the plot and where the
// unit square of the Bézier sits in it. Origin is the data point at (u,v) = (0,0),
// End the data point at (1,1), so a decay (the dictation's hangover: from the
// maximum delay at the ramp start down to the minimum at the ramp end) and a
// response (the Ambient brightness: from (0,0) up to (1,1)) share one mapping.
// XMax and YMax bound the plot; Axes asks for rulers and unit labels (the wide
// layout of decision 14, kept by decision 21); without them the plot is the
// square unit box alone.
public sealed record CurveFrame(
    double OriginX,
    double OriginY,
    double EndX,
    double EndY,
    double XMax,
    double YMax,
    bool Axes = false,
    string XUnit = "",
    string YUnit = "")
{
    public static readonly CurveFrame Unit = new(0, 0, 1, 1, 1, 1);

    // Unit-square coordinates to data coordinates.
    public double DataX(double u) => OriginX + u * (EndX - OriginX);
    public double DataY(double v) => OriginY + v * (EndY - OriginY);

    // Data coordinates back to the unit square; a degenerate box maps to 0.
    public double UnitU(double x) => EndX == OriginX ? 0 : (x - OriginX) / (EndX - OriginX);
    public double UnitV(double y) => EndY == OriginY ? 0 : (y - OriginY) / (EndY - OriginY);

    // The curve's data y at a data x: held at the origin's level before the box,
    // at the end's level after it, the eased value between.
    public double Sample(CubicBezier curve, double x)
    {
        double u = UnitU(x);
        if (EndX >= OriginX ? x <= OriginX : x >= OriginX) return OriginY;
        if (EndX >= OriginX ? x >= EndX : x <= EndX) return EndY;
        return DataY(curve.Solve(u));
    }
}

// A cubic Bézier easing on the unit square: given x in [0,1], the curve's y at
// that x, the function CSS `cubic-bezier(x1,y1,x2,y2)` defines. The curve is
// parametric, so y at a given x means inverting x(t) first — Newton-Raphson with
// a bisection fallback, WebKit's UnitBezier scheme — then reading y(t). X1 and X2
// are clamped to [0,1] so x(t) stays monotone and the inversion single-valued; Y
// is left free, so an overshooting handle draws what it does. The engines keep
// their own copies of this arithmetic (UnitBezier, AmbientBrightnessCurve); this
// one serves the shared editor and its tests.
public readonly struct CubicBezier
{
    private readonly double _ax, _bx, _cx;
    private readonly double _ay, _by, _cy;

    public CubicBezier(double x1, double y1, double x2, double y2)
    {
        x1 = Clean(x1, 0.42, clamp: true);
        x2 = Clean(x2, 0.58, clamp: true);
        y1 = Clean(y1, 0.0, clamp: false);
        y2 = Clean(y2, 1.0, clamp: false);

        _cx = 3.0 * x1;
        _bx = 3.0 * (x2 - x1) - _cx;
        _ax = 1.0 - _cx - _bx;

        _cy = 3.0 * y1;
        _by = 3.0 * (y2 - y1) - _cy;
        _ay = 1.0 - _cy - _by;
    }

    public CubicBezier(CurvePoints p) : this(p.X1, p.Y1, p.X2, p.Y2) { }

    private double SampleX(double t) => ((_ax * t + _bx) * t + _cx) * t;
    private double SampleY(double t) => ((_ay * t + _by) * t + _cy) * t;
    private double SampleDerivativeX(double t) => (3.0 * _ax * t + 2.0 * _bx) * t + _cx;

    // y at the given x in [0,1]; the input is clamped so an out-of-range progress
    // cannot escape the unit interval.
    public double Solve(double x, double epsilon = 1e-6)
        => SampleY(SolveForT(Math.Clamp(x, 0.0, 1.0), epsilon));

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

    private static double Clean(double v, double fallback, bool clamp)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
        return clamp ? Math.Clamp(v, 0.0, 1.0) : v;
    }
}
