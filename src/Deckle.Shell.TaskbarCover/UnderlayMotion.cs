namespace Deckle.Shell.TaskbarCover;

// The dissolve of the strip under the taskbar (TaskbarUnderlay): how opaque a
// strip is, at a time, while it fades in or out. Pure: no Win32 call, no
// state. A slide was tried first and dropped: it lagged behind the window it
// was meant to carry on (Louis, trial of 2026-11-26).
public static class UnderlayMotion
{
    // Windows' longest step for an element entering, on the curve below
    // (learn.microsoft.com, Windows motion: "Direct Entrance", 167, 250 or
    // 333 ms). 167 ms on a cubic ease-out read as too abrupt (Louis,
    // 2026-11-27): this curve reaches most of the way at once and leaves
    // the rest to a long settle, so it can take the longer step.
    public const long FadeMs = 333;

    // The alpha of a strip elapsedMs into a fade that started at from and
    // goes to to.
    public static byte FadeAlpha(byte from, byte to, long elapsedMs)
    {
        double p = Math.Clamp(elapsedMs / (double)FadeMs, 0, 1);
        return (byte)Math.Round(from + (to - from) * FastOutSlowIn(p));
    }

    public static bool FadeDone(long elapsedMs) => elapsedMs >= FadeMs;

    // Windows' "Fast Out, Slow In", cubic-bezier(0, 0, 0, 1), for what
    // enters the scene (learn.microsoft.com, Timing and easing). With both
    // control points at x = 0 the curve's x is s³ and its y is 3s² − 2s³,
    // so the progress p is reached at s = ∛p, with no numeric solve.
    public static double FastOutSlowIn(double p)
    {
        double s = Math.Cbrt(Math.Clamp(p, 0, 1));
        return s * s * (3 - 2 * s);
    }
}
