namespace Deckle.Composition;

// The gauge's course: the CSS cubic-bezier(x1, y1, x2, y2) of
// UtteranceCountdownConfig's Course* fields, read as y at a given x. The
// capture reports the pause's progress linearly; the gauge runs it through
// this curve before it reaches the mask, so the light starts slow and brakes
// late without the capture knowing. The Playground's replay hands the same
// four points to the compositor's own cubic-bezier easing, so both paths
// draw one curve.
//
// Same arithmetic as Deckle.Transcription's UnitBezier and the Catalog's
// CubicBezier (Newton-Raphson, bisection fallback, WebKit's UnitBezier
// scheme), copied rather than shared: Composition sits below both.
public readonly struct UtteranceCountdownCourse
{
    private readonly double _ax, _bx, _cx;
    private readonly double _ay, _by, _cy;

    public UtteranceCountdownCourse(double x1, double y1, double x2, double y2)
    {
        // Control x's clamped to [0, 1] keep x(t) monotone, so y(x) is
        // single-valued.
        x1 = Math.Clamp(x1, 0.0, 1.0);
        x2 = Math.Clamp(x2, 0.0, 1.0);

        _cx = 3.0 * x1;
        _bx = 3.0 * (x2 - x1) - _cx;
        _ax = 1.0 - _cx - _bx;

        _cy = 3.0 * y1;
        _by = 3.0 * (y2 - y1) - _cy;
        _ay = 1.0 - _cy - _by;
    }

    public static UtteranceCountdownCourse Of(UtteranceCountdownConfig config)
        => new(config.CourseEaseP1X, config.CourseEaseP1Y, config.CourseEaseP2X, config.CourseEaseP2Y);

    private double SampleX(double t) => ((_ax * t + _bx) * t + _cx) * t;
    private double SampleY(double t) => ((_ay * t + _by) * t + _cy) * t;
    private double SampleDerivativeX(double t) => (3.0 * _ax * t + 2.0 * _bx) * t + _cx;

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
        while (t0 < t1)
        {
            double xt = SampleX(t);
            if (Math.Abs(xt - x) < epsilon) return t;
            if (x > xt) t0 = t; else t1 = t;
            t = (t1 - t0) * 0.5 + t0;
        }
        return t;
    }

    // y at the given progress, both clamped to [0, 1]: the endpoints are
    // fixed, so a closed gauge stays closed and a full one stays full.
    public float Solve(float progress)
    {
        double x = Math.Clamp(progress, 0.0, 1.0);
        if (x <= 0.0) return 0f;
        if (x >= 1.0) return 1f;
        return (float)Math.Clamp(SampleY(SolveForT(x, 1e-6)), 0.0, 1.0);
    }
}
