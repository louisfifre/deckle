namespace Deckle.Shell.TaskbarCover;

// Mutate, then call TaskbarCoverSettingsService.Save().
public sealed class TaskbarCoverSettings
{
    /// <summary>
    /// Written by the Settings page, the tray panel switch and the hotkey alike.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// How far from the taskbar the pointer starts to reveal it. Written by the
    /// Settings page; the App relays a change to the running cover host.
    /// </summary>
    public ApproachDistance Approach { get; set; } = ApproachDistance.Standard;

    // The fade curve stays a constant in CoverGeometry.
}
