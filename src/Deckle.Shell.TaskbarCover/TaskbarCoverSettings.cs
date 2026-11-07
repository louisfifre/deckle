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

    // The named level an older file carries under "approach". Read once and
    // folded into ApproachDistanceDip, then cleared, and null is not written,
    // so the next save drops the key. Never read at runtime.
    [JsonPropertyName("approach")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApproachLevel? LegacyApproach { get; set; }

    // The fade curve stays a constant in CoverGeometry.

    // A file written before the DIP figure existed holds only the level, so
    // the level decides; a file never holds both, since the level is cleared
    // before the first save that writes the figure. Then a hand-edited figure
    // is brought into range.
    public void OnDeserialized()
    {
        if (LegacyApproach is { } level)
        {
            ApproachDistanceDip = ApproachDistance.FromLevel(level);
            LegacyApproach = null;
        }
        ApproachDistanceDip = ApproachDistance.Clamp(ApproachDistanceDip);
    }
}
