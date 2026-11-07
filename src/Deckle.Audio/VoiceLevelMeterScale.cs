namespace Deckle.Audio;

// ── VoiceLevelMeterScale ──────────────────────────────────────────────────────
//
// The arithmetic of the voice meter on the Recording page, kept out of the
// control so it is testable without XAML: where a dBFS value falls on the
// track, how the bar rises and falls between two packets, and how long the
// peak tick holds. Pure functions; the control owns every piece of state.
internal static class VoiceLevelMeterScale
{
    // The track spans a fixed dB scale, not one that follows the voice level:
    // the marks have to move when the field changes, or the user would not see
    // what the edit did. -80 to -10 dBFS puts the shipped default (-45) and the
    // levels a desk microphone plausibly gives (about -60 to -30) in the middle
    // third, with room on each side for a quiet room and a loud voice. A level
    // outside the span pins the bar at its end.
    public const double MinDbfs = -80.0;
    public const double MaxDbfs = -10.0;

    // The slider under the track spans the same scale, so its thumb is the
    // voice level's mark. It steps by a whole dB, the precision the field and
    // the readouts show, rather than by the magnitude grain of its span (2 dB):
    // every stored level then falls on a detent, so the thumb can never be
    // snapped beside the mark of a level such as -45 or -47.
    public const double SliderStepDb = 1.0;

    // What a frame of exact zeros reads as: the floor the DSP uses for the same
    // case, far under the track, so a muted microphone leaves the bar empty.
    public const double SilenceFloorDbfs = -120.0;

    // How long the peak tick holds the loudest value before it follows the
    // level down: long enough to read a figure, short enough to show the next
    // word's peak.
    public const double PeakHoldSeconds = 1.0;

    // The window of the "Loudest" readout.
    public const double LoudestWindowSeconds = 3.0;

    // Linear RMS → dBFS; zero or less is the silence floor.
    public static double RmsToDbfs(float rms)
        => rms <= 0f ? SilenceFloorDbfs : 20.0 * Math.Log10(rms);

    // Where a level sits on the track, from 0 (MinDbfs, left) to 1 (MaxDbfs,
    // right), clamped. NaN reads as the empty end.
    public static double Fraction(double dbfs)
    {
        if (double.IsNaN(dbfs)) return 0.0;
        return Math.Clamp((dbfs - MinDbfs) / (MaxDbfs - MinDbfs), 0.0, 1.0);
    }

    // The bar's ballistics, in time rather than per call: it follows each
    // packet's level (CaptureLevel, about 10 ms of audio) with a fast rise and a
    // slow fall, the way a hardware level meter does. The rise is near instant,
    // so a syllable shows as it is spoken; the fall is slow enough that the eye
    // can read the level between two words instead of a flicker. Both are time
    // constants of an exponential, applied to the dB value over the audio the
    // packet covers, so a 3 ms and a 10 ms device move the bar at the same speed.
    public const double AttackMs = 5.0;
    public const double ReleaseMs = 300.0;

    // The bar's next value after `elapsedMs` of audio at `sampleDbfs`. The first
    // packet of a run has no previous value and is taken as is.
    public static double Ballistic(double? previousDbfs, double sampleDbfs, double elapsedMs)
    {
        if (previousDbfs is not { } previous) return sampleDbfs;
        if (elapsedMs <= 0) return previous;
        double tau = sampleDbfs > previous ? AttackMs : ReleaseMs;
        double keep = Math.Exp(-elapsedMs / tau);
        return previous * keep + sampleDbfs * (1.0 - keep);
    }

    // The peak tick's next position. A value at or above the held peak takes
    // its place and restarts the hold; once the hold has run out the tick
    // follows the level down, frame by frame, without restarting the hold, so
    // it slides rather than stepping a second at a time.
    public static (double PeakDbfs, double HeldSinceSeconds) NextPeak(
        double peakDbfs, double heldSinceSeconds, double valueDbfs, double nowSeconds)
    {
        if (valueDbfs >= peakDbfs) return (valueDbfs, nowSeconds);
        if (nowSeconds - heldSinceSeconds >= PeakHoldSeconds) return (valueDbfs, heldSinceSeconds);
        return (peakDbfs, heldSinceSeconds);
    }
}
