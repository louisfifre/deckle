using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// The motion of the strip under the taskbar (TaskbarUnderlay): how far its
// slide has gone and how opaque a fading strip is, at a time, and the part of
// the taskbar's place the slide covers. Pure: no Win32 call, no state.
public static class UnderlayMotion
{
    // WinUI's ControlFastAnimationDuration and ControlNormalAnimationDuration,
    // the durations Windows' own controls use.
    public const long SlideMs = 167;
    public const long FadeMs  = 250;

    // The part of the place covered elapsedMs into a slide that started at
    // from (0 to 1). In: out of the app fast, settling at the screen's edge,
    // as the window that reached it. Out: slowly at first, gathering speed
    // back into the app, as the window that leaves.
    public static double SlideExtent(double from, bool slideIn, long elapsedMs)
    {
        double t = Progress(elapsedMs, SlideMs);
        double moved = slideIn ? EaseOut(t) : t * t * t;
        double target = slideIn ? 1 : 0;
        return from + (target - from) * moved;
    }

    public static bool SlideDone(long elapsedMs) => elapsedMs >= SlideMs;

    // The alpha of the strip fading out over the new one, elapsedMs in.
    public static byte FadeAlpha(long elapsedMs) =>
        (byte)Math.Round(255 * (1 - EaseOut(Progress(elapsedMs, FadeMs))));

    public static bool FadeDone(long elapsedMs) => elapsedMs >= FadeMs;

    // The part of place a slide at extent covers: grown from the app's side,
    // the side away from the screen's edge.
    public static NativeMethods.RECT Part(NativeMethods.RECT place, TaskbarEdge edge, double extent)
    {
        var r = place;
        bool across = edge is TaskbarEdge.Left or TaskbarEdge.Right;
        int reach = (int)Math.Round((across ? r.right - r.left : r.bottom - r.top) * Math.Clamp(extent, 0, 1));
        switch (edge)
        {
            case TaskbarEdge.Left:   r.left   = r.right - reach;  break;
            case TaskbarEdge.Right:  r.right  = r.left + reach;   break;
            case TaskbarEdge.Top:    r.top    = r.bottom - reach; break;
            case TaskbarEdge.Bottom: r.bottom = r.top + reach;    break;
        }
        return r;
    }

    private static double Progress(long elapsedMs, long durationMs) =>
        Math.Clamp(elapsedMs / (double)durationMs, 0, 1);

    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
}
