using System.Text.Json.Serialization;

namespace Deckle.Shell.TaskbarCover;

// Mutate, then call TaskbarCoverSettingsService.Save().
public sealed class TaskbarCoverSettings : IJsonOnDeserialized
{
    /// <summary>
    /// Written by the Settings page, the tray panel switch and the hotkey alike.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// How far past the taskbar the pointer starts to reveal it, in
    /// device-independent pixels, within ApproachDistance's range. Written by
    /// the Settings page; the App relays a change to the running cover host,
    /// whose bands convert it with their own monitor's scale.
    /// </summary>
    public int ApproachDistanceDip { get; set; } = ApproachDistance.DefaultDip;

    // The fade curve stays a constant in CoverGeometry.

    // A hand-edited figure outside the range lands on its nearest bound.
    public void OnDeserialized() => ApproachDistanceDip = ApproachDistance.Clamp(ApproachDistanceDip);
}
