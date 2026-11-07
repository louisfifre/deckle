namespace Deckle.Lighting.Ambient;

/// <summary>
/// What the Ambient screen capture rate is decided from: no setting, a
/// ceiling chosen in code by tier, and the display's own refresh rate. The
/// capture follows the display up to the ceiling:
/// <c>target = min(display refresh, tier cap)</c>.
/// </summary>
/// <remarks>
/// The tiers, each with the why. Every figure comes from the cost
/// measurement of 2026-11-07 (<c>docs/research/research--cout-de-capture--2026-11-07.md</c>):
/// the cost per frame is nearly constant (about 0.3 ms of capture-thread CPU
/// and about 122 MB of GPU memory traffic on a 4K display), so it grows
/// linearly with the rate.
/// <list type="bullet">
/// <item><b>Normal, 120 Hz.</b> The highest rate the measurement supports:
/// on a 4K 120 Hz display it costs about 4 % of one core and about 2 % of
/// the GPU's memory bandwidth, with no visible effect. The lamps take a
/// colour every 20 ms (50 Hz Entertainment), so a faster capture only makes
/// the latest sample fresher by a few milliseconds.</item>
/// <item><b>Next tier, 240 Hz: written here, not enabled.</b> It is only
/// extrapolated (6 to 9 % of a core) and has never been measured on a
/// display that fast. It stays out until the research note's re-measure
/// command has run on a 240 Hz display and a visible gain is shown; the
/// command is under "Pour relancer la mesure" in the note
/// (<c>Deckle.Lighting.Ambient.Tests.exe -class
/// Deckle.Lighting.Ambient.Tests.AmbientCaptureCostInteractiveTests -explicit only</c>).
/// Enabling it is a change of <see cref="NormalCapHz"/> and nothing else.</item>
/// <item><b>Performance mode, 60 Hz.</b> The safe rate for a weak PC: the
/// measurement says nothing of integrated GPUs, where the 33 MB copy per
/// frame weighs on shared memory (not verified), so the mode takes the
/// lowest rate that still follows motion smoothly.</item>
/// </list>
/// A display slower than the cap is followed at its own rate. A display
/// faster than any tier (480, 700 Hz exist) is held at the cap: the cap, not
/// the display, is what bounds the cost. DXGI can deliver more frames than the
/// display refreshes (up to 140 per second on a 120 Hz display), so the cap
/// must start from the refresh rate Windows announces and not from what DXGI
/// delivers; <c>CapturePacer</c> then paces to the target.
/// </remarks>
public static class AmbientCaptureCap
{
    /// <summary>Ceiling of the normal tier, in Hz.</summary>
    public const int NormalCapHz = 120;

    /// <summary>Ceiling in performance mode, in Hz.</summary>
    public const int PerformanceCapHz = 60;

    /// <summary>Rate followed when Windows announces no refresh rate for the
    /// display (a driver reporting its "default" mode): the performance
    /// ceiling, the rate that is safe whatever the display is.</summary>
    public const int UnknownDisplayHz = PerformanceCapHz;

    /// <summary>The tier ceiling for the current mode, in Hz.</summary>
    public static int CapHz(bool performanceMode)
        => performanceMode ? PerformanceCapHz : NormalCapHz;

    /// <summary>
    /// The rate the capture is asked for: the display's refresh rate, held to
    /// the tier ceiling. A refresh rate that is zero or negative is unknown and
    /// gives <see cref="UnknownDisplayHz"/>, itself held to the ceiling.
    /// </summary>
    public static int TargetHz(int displayRefreshHz, bool performanceMode)
    {
        int cap = CapHz(performanceMode);
        int display = displayRefreshHz > 0 ? displayRefreshHz : UnknownDisplayHz;
        return Math.Min(display, cap);
    }
}
