namespace Deckle.Lighting.Ambient;

internal readonly struct AmbientBrightnessCurve
{
    private readonly double _ax, _bx, _cx;
    private readonly double _ay, _by, _cy;

    public AmbientBrightnessCurve(double x1, double y1, double x2, double y2)
    {
        x1 = Clean01(x1, 0.42);
        y1 = Clean01(y1, 0.00);
        x2 = Clean01(x2, 1.00);
        y2 = Clean01(y2, 1.00);

        _cx = 3.0 * x1;
        _bx = 3.0 * (x2 - x1) - _cx;
        _ax = 1.0 - _cx - _bx;

        _cy = 3.0 * y1;
        _by = 3.0 * (y2 - y1) - _cy;
        _ay = 1.0 - _cy - _by;
    }

    public static (byte R, byte G, byte B) Apply(
        byte r,
        byte g,
        byte b,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        int max = Math.Max(r, Math.Max(g, b));
        if (max == 0) return (r, g, b);

        double ratio = max / 255.0;
        double shaped = new AmbientBrightnessCurve(x1, y1, x2, y2).Solve(ratio);
        double scale = shaped / ratio;

        return (
            (byte)Math.Clamp((int)Math.Round(r * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(g * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(b * scale), 0, 255));
    }

    public double Solve(double x, double epsilon = 1e-6)
        => Math.Clamp(SampleY(SolveForT(Math.Clamp(x, 0.0, 1.0), epsilon)), 0.0, 1.0);

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

    private static double Clean01(double value, double fallback)
        => double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Clamp(value, 0.0, 1.0);
}
