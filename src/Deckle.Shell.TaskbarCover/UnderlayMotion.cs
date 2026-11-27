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

    // A COLORREF elapsedMs into a fade from one colour to another, each
    // channel on the alpha's curve. A plain colour fading into another is
    // what one strip dissolving over the other would show.
    public static uint FadeColor(uint from, uint to, long elapsedMs)
    {
        uint color = 0;
        for (int shift = 0; shift <= 16; shift += 8)
            color |= (uint)FadeAlpha((byte)(from >> shift), (byte)(to >> shift), elapsedMs) << shift;
        return color;
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

// What the strip shows: its opacity, its colour (a COLORREF), and how much of
// that colour tints the blur behind it, 255 for a plain strip.
public readonly record struct UnderlayLook(byte Alpha, uint Color, byte Tint);

// The strip's one motion. Whatever changes, appearing, leaving, a new colour,
// the blur on or off, goes from what the strip shows now to what was asked,
// on the same fade. A change asked mid-fade starts from where the strip
// stands. A strip that shows nothing takes its new colour while unseen, so
// only its opacity moves when it appears. Pure, like UnderlayMotion: the
// caller passes the time and paints Shown.
public sealed class UnderlayFade
{
    private UnderlayLook _from;
    private long _start = -1;

    public UnderlayLook Shown { get; private set; }
    public UnderlayLook Target { get; private set; }
    public bool Moving => _start >= 0;

    // Asks for target, faded when animate, at once otherwise.
    public void MoveTo(UnderlayLook target, bool animate, long now)
    {
        if (target == Target && (Moving || Shown == target)) return;
        if (Shown.Alpha == 0) Shown = Shown with { Color = target.Color, Tint = target.Tint };
        Target = target;
        if (!animate || Shown == target)
        {
            Shown = target;
            _start = -1;
            return;
        }
        _from = Shown;
        _start = now;
    }

    // Moves Shown to where the fade stands at now.
    public void Tick(long now)
    {
        if (!Moving) return;
        long elapsed = now - _start;
        Shown = new UnderlayLook(
            UnderlayMotion.FadeAlpha(_from.Alpha, Target.Alpha, elapsed),
            UnderlayMotion.FadeColor(_from.Color, Target.Color, elapsed),
            UnderlayMotion.FadeAlpha(_from.Tint, Target.Tint, elapsed));
        if (UnderlayMotion.FadeDone(elapsed)) _start = -1;
    }
}
