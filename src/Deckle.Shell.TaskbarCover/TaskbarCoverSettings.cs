namespace Deckle.Shell.TaskbarCover;

// Persisted user intent for the taskbar cover module. Mutate then call
// TaskbarCoverSettingsService.Save() — the standard module-settings pattern.
public sealed class TaskbarCoverSettings
{
    /// <summary>
    /// Master switch — the cover host runs only when on. Written by the
    /// Settings page, the tray panel switch and the hotkey alike.
    /// </summary>
    public bool Enabled { get; set; } = false;

    // The approach-zone factor and the fade curve are constants in
    // CoverGeometry (ApproachZoneFactor, Opacity); the switch is the only
    // setting until the approach levels (near, standard, far) are decided.
}
