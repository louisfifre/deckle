using System;

namespace Deckle.Catalog;

// The "nice" tick step of a ruler (1, 2 or 5 × 10ⁿ), so a ruler lands on round
// values. Shared by the curve's rulers and the pause strip's, so both scales
// read alike.
public static class AxisTicks
{
    public static double NiceStep(double range, int targetTicks)
    {
        if (range <= 0 || targetTicks <= 0) return 0;
        double raw = range / targetTicks;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double step = norm < 1.5 ? 1 : norm < 3 ? 2 : norm < 7 ? 5 : 10;
        return step * mag;
    }
}
