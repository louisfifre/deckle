namespace Deckle.Shell.TaskbarCover;

// Mutate, then call TaskbarCoverSettingsService.Save().
public sealed class TaskbarCoverSettings
{
    /// <summary>
    /// Written by the Settings page, the tray panel switch and the hotkey alike.
    /// </summary>
    public bool Enabled { get; set; } = false;

    // The approach-zone factor and the fade curve stay constants in
    // CoverGeometry until the approach levels (near, standard, far) are decided.
}
