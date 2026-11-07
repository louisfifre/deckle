using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// How far past the taskbar the pointer starts to reveal it, in
// device-independent pixels (DIP, 96-DPI units). The setting is a DIP figure
// so one value is one hand movement on every monitor: each band converts it
// with its own monitor's scale (CoverGeometry.ApproachSpan), where the hit
// test runs, so 64 DIP is 64 pixels at 100% and 128 at 200%.
//
// A magnitude rather than three named levels (2026-11-07): the maintainer read
// "Near · 32 px" as 32 physical pixels, nearly nothing on a 4K screen, and
// asked for a value to sweep with a preview beside it. The range and the
// default live here, the one source the settings file, the page and the host
// read.
public static class ApproachDistance
{
    // Below 10 DIP the fade runs over a few pixels and reads as a flicker,
    // not a reveal; 0 would leave no zone past the band at all. 320 DIP,
    // five times the default, reaches a third of a 1080p screen's height,
    // already further than a reveal gesture travels. With these bounds the
    // slider's grain is 10 DIP (MagnitudeGrain.StepFor: 310 / 40 rounds up
    // to 10).
    public const int MinimumDip = 10;
    public const int MaximumDip = 320;

    // A step of the design-system scale.
    public const int DefaultDip = Spacing.Large;

    // A hand-edited figure outside the range lands on its nearest bound.
    public static int Clamp(int dip) => Math.Clamp(dip, MinimumDip, MaximumDip);
}
