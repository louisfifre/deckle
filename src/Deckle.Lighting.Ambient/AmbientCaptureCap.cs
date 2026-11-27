namespace Deckle.Lighting.Ambient;

/// <summary>
/// The Ambient screen capture rate: 60 frames a second, whatever the display
/// refreshes at, with no setting.
/// </summary>
/// <remarks>
/// Why 60. The cost per frame is nearly constant (about 0.3 ms of
/// capture-thread CPU and about 122 MB of GPU memory traffic on a 4K display,
/// measured on 2026-11-07, <c>docs/research/research--cout-de-capture--2026-11-07.md</c>),
/// so it grows linearly with the rate: about 2 to 3 % of one core at 60 Hz,
/// about 4 % at 120 Hz. The lamps take a colour every 20 ms (50 Hz
/// Entertainment), so 60 frames a second follow motion smoothly and a faster
/// capture only makes the latest sample fresher by a few milliseconds. A
/// display slower than 60 Hz is not followed down: the pacer asks for 60 and
/// DXGI delivers what the display gives.
/// <para>
/// History, so it is not lost. Until 2026-11-07 the rate was a user setting
/// (15, 30 or 60 Hz). It was dropped by the settings triage of 2026-10-14 and
/// put back on 2026-10-15, because the maintainer looked for the 60 fps choice
/// and found nothing. On 2026-11-07 it became a ceiling chosen in code by tier
/// (120 Hz, or 60 Hz under a global Performance mode). On 2026-11-25 the
/// maintainer removed the Performance mode and fixed the rate at 60 for
/// everyone. Do not bring a setting back; change the constant.
/// </para>
/// </remarks>
public static class AmbientCaptureCap
{
    /// <summary>The rate the capture is asked for, in Hz.</summary>
    public const int CaptureHz = 60;
}
