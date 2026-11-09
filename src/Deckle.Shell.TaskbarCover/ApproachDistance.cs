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
    // not a reveal; 0 would leave no zone past the band at all.
    //
    // The ceiling is two different things (2026-11-09). The maintainer found
    // 320 DIP too short on a 4K screen and asked for half the screen. Half
    // the screen depends on the monitor, so the stored figure only gets a
    // generous absolute bound, 3840 DIP (the widest 100% screen in common
    // use), that no hand-edit or screen change can exceed and that never
    // loses a figure chosen on a large screen. The reach actually applied is
    // capped per band at half its own monitor (CoverGeometry.ApproachSpan),
    // and the Settings slider stops at that half (SliderMaximum).
    public const int MinimumDip = 10;
    public const int MaximumDip = 3840;

    // The slider's maximum when the primary taskbar's monitor cannot be read:
    // half of a 4K screen at 200%, the maintainer's own.
    public const int FallbackSliderMaximumDip = 960;

    // The slider's maximum for a monitor whose half extent is halfScreenDip,
    // kept inside the range so the slider always has room to move.
    public static int SliderMaximum(int halfScreenDip)
        => Math.Clamp(halfScreenDip, MinimumDip * 2, MaximumDip);

    // The unit the page shows beside the figure. Never "px": a person reads
    // that as physical pixels, the very misreading that made the setting a DIP
    // figure, and the page's reach readout is where physical pixels appear
    // (2026-11-07, bug note "Approach distance read as physical pixels").
    public const string UnitLabel = "DIP";

    // A step of the design-system scale.
    public const int DefaultDip = Spacing.Large;

    // A hand-edited figure outside the range lands on its nearest bound.
    public static int Clamp(int dip) => Math.Clamp(dip, MinimumDip, MaximumDip);
}
