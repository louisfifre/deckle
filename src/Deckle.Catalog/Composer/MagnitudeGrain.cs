using System;

namespace Deckle.Catalog;

// The grain of a Setting.Magnitude control: the step its slider detents on and the
// number of decimals its field shows. Pure arithmetic, kept apart from the WinUI
// composer so it can be tested without a control.
public static class MagnitudeGrain
{
    // The "nice" 1-2-5 step for a magnitude slider, derived from its range so a
    // magnitude declares only bounds. Aims for ~40 detents across the span, then
    // rounds that raw step UP to the nearest 1, 2 or 5 times a power of ten — the
    // classic axis-tick niceness — so the grain reads as a round number (0.05, 1,
    // 50) rather than an arbitrary fraction. The editable field still takes any
    // exact value; this only sets how coarsely the thumb detents.
    public static double StepFor(double minimum, double maximum)
    {
        double span = Math.Abs(maximum - minimum);
        if (span <= 0) return 1;

        double raw = span / 40.0;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude; // in [1, 10)
        double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    // How many decimals a step needs to be written exactly (0.05 gives 2), capped
    // at 6 so a degenerate step cannot loop.
    public static int DecimalsFor(double stepFrequency)
    {
        if (stepFrequency <= 0) return 0;
        int decimals = 0;
        double step = stepFrequency;
        while (decimals < 6 && Math.Abs(step - Math.Round(step)) > 1e-9)
        {
            step *= 10;
            decimals++;
        }
        return decimals;
    }

    // Puts a value on the display grain. A slider position such as 0.05 + 2 * 0.05
    // is 0.15000000000000002 in binary; Math.Round to the displayed decimals gives
    // the double nearest to the written number (0.15), so what is stored, compared
    // and persisted is what the field shows. NaN passes through, and a result of
    // negative zero is returned as zero so it never reads "-0".
    public static double Snap(double value, int decimals)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return value;
        double rounded = Math.Round(value, Math.Clamp(decimals, 0, 6), MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }
}
