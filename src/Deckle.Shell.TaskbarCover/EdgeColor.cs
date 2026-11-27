using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// The colour the underlay takes: the dominant colour of the maximized app's
// pixel line that touches the taskbar, so the faded taskbar's place continues
// the app. Pure: no Win32 call, no state.
public static class EdgeColor
{
    // The one-pixel line of the app just past the taskbar, along its whole
    // length. A maximized app fills the work area, which starts there.
    public static NativeMethods.RECT SampleLine(NativeMethods.RECT bar, TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Left  => new() { left = bar.right,    top = bar.top,        right = bar.right + 1, bottom = bar.bottom },
        TaskbarEdge.Right => new() { left = bar.left - 1, top = bar.top,        right = bar.left,      bottom = bar.bottom },
        TaskbarEdge.Top   => new() { left = bar.left,     top = bar.bottom,     right = bar.right,     bottom = bar.bottom + 1 },
        _                 => new() { left = bar.left,     top = bar.top - 1,    right = bar.right,     bottom = bar.top },
    };

    // Pixels come as 0xAARRGGBB (a 32-bit DIB read as uint); the answer is a
    // GDI COLORREF, 0x00BBGGRR. The pixels are grouped by their 4 high bits
    // per channel, and the largest group gives its mean: a plain edge gives
    // its own colour, and a blurred or noisy one (an acrylic panel) gives the
    // colour most of its length shows rather than a muddy mean of everything.
    public static uint Dominant(ReadOnlySpan<uint> pixels) => Dominant(pixels, out _);

    // share: the part of the line the dominant group covers, 0 to 1. A line
    // that is nearly all one group is plain to the eye, a blur of it too.
    public static uint Dominant(ReadOnlySpan<uint> pixels, out double share)
    {
        share = 0;
        if (pixels.IsEmpty) return 0;

        Span<int> counts = stackalloc int[4096];
        counts.Clear();
        foreach (uint p in pixels) counts[Bucket(p)]++;

        int best = 0;
        for (int i = 1; i < counts.Length; i++)
            if (counts[i] > counts[best]) best = i;

        long r = 0, g = 0, b = 0;
        foreach (uint p in pixels)
        {
            if (Bucket(p) != best) continue;
            r += (p >> 16) & 0xFF;
            g += (p >> 8) & 0xFF;
            b += p & 0xFF;
        }
        int n = counts[best];
        share = (double)n / pixels.Length;
        return (uint)(r / n) | (uint)(g / n) << 8 | (uint)(b / n) << 16;
    }

    // Below this share the edge shows more than one colour: a see-through
    // backdrop over the wallpaper, or the app's own content.
    public const double PlainShare = 0.9;

    // Two COLORREFs no channel of which differs by more than a few steps:
    // the same edge read twice, its dominant group shifting a little.
    public static bool Close(uint a, uint b)
    {
        for (int shift = 0; shift < 24; shift += 8)
            if (Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF)) > 8) return false;
        return true;
    }

    // Two COLORREFs one channel of which differs by more than a window's
    // own change as it loses the focus: another window's edge.
    public static bool Far(uint a, uint b)
    {
        for (int shift = 0; shift < 24; shift += 8)
            if (Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF)) > 48) return true;
        return false;
    }

    private static int Bucket(uint p)
        => (int)(((p >> 20) & 0xF) << 8 | ((p >> 12) & 0xF) << 4 | ((p >> 4) & 0xF));
}
