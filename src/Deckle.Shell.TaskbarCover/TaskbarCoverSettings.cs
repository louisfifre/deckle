namespace Deckle.Shell.TaskbarCover;

// Persisted user intent for the taskbar cover module. Mutate then call
// TaskbarCoverSettingsService.Save() — the standard module-settings pattern.
public sealed class TaskbarCoverSettings
{
    /// <summary>Master switch — the cover host runs only when on.</summary>
    public bool Enabled { get; set; } = false;

    // The reveal-zone depth and the re-cover delay are frozen constants
    // (CoverGeometry.RevealZoneDepth, TaskbarCoverHost.RecoverDelayMs) —
    // calibrated in the standalone utility, deliberately not exposed.
}
