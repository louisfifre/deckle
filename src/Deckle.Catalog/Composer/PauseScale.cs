using System;

namespace Deckle.Catalog;

// The zoom of the pause strip: one utterance's end drawn at the scale of a word,
// where the cut curve's axis (0 to ~140 s) would make a 0.15 s margin less than
// a pixel. The strip shows a voiced burst from 0 to SpeechEnd, then the pause
// that follows it up to the cut, on a ruler from 0 to Span in TickStep steps.
//
// The burst is twice the minimum speech, at least half a second, so the
// minimum-speech bracket sits inside it with room to grow; a bracket dragged
// past the burst's end means that burst would now be dropped as a blip
// (IsBlip). The pause runs to the cut, or to the margin when the margin is the
// longer. The span is rounded up to a whole tick so the ruler ends on a label.
// Computed when no gesture is in flight: the strip freezes it during a drag, so
// nothing moves under the pointer. Pure, so it is tested without a control.
public sealed record PauseScale(double SpeechEnd, double Span, double TickStep)
{
    private const double MinBurst = 0.5;
    private const int TargetTicks = 5;
    private const double Headroom = 1.05;

    public static PauseScale For(double minimumSpeech, double margin, double cutAfter)
    {
        double burst = Math.Max(MinBurst, 2 * Clean(minimumSpeech));
        double pause = Math.Max(Clean(cutAfter), Clean(margin));
        double raw = (burst + pause) * Headroom;
        double step = AxisTicks.NiceStep(raw, TargetTicks);
        double span = step > 0 ? Math.Ceiling(raw / step - 1e-9) * step : raw;
        return new PauseScale(burst, MagnitudeGrain.Snap(span, 6), step);
    }

    // A fraction of the strip's width for a time in seconds from its origin.
    public double Fraction(double seconds) => Span <= 0 ? 0 : Math.Clamp(seconds / Span, 0, 1);

    // Seconds for a fraction of the width, the inverse a drag reads.
    public double Seconds(double fraction) => fraction * Span;

    // True when a burst of this scale's length would be shorter than the floor.
    public bool IsBlip(double minimumSpeech) => minimumSpeech > SpeechEnd;

    private static double Clean(double v) => double.IsNaN(v) || double.IsInfinity(v) || v < 0 ? 0 : v;
}
