namespace Deckle.Shell.TaskbarCover;

// The dissolve of the strip under the taskbar (TaskbarUnderlay): how opaque a
// strip is, at a time, while it fades in or out. Pure: no Win32 call, no
// state.
public static class UnderlayMotion
{
    // WinUI's ControlFastAnimationDuration, the duration Windows' own
    // controls use for a quick change. A slide was tried first and dropped:
    // it lagged behind the window it was meant to carry on (Louis, trial of
    // 2026-11-26).
    public const long FadeMs = 167;

    // The alpha of a strip elapsedMs into a fade that started at from and
    // goes to to: quick at first, settling at the end.
    public static byte FadeAlpha(byte from, byte to, long elapsedMs)
    {
        double t = Math.Clamp(elapsedMs / (double)FadeMs, 0, 1);
        double eased = 1 - Math.Pow(1 - t, 3);
        return (byte)Math.Round(from + (to - from) * eased);
    }

    public static bool FadeDone(long elapsedMs) => elapsedMs >= FadeMs;
}
