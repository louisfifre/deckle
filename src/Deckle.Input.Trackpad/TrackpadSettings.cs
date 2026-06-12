namespace Deckle.Input.Trackpad;

// Persisted user intent for the trackpad module. Mutate then call
// TrackpadSettingsService.Save() — the standard module-settings pattern.
public sealed class TrackpadSettings
{
    /// <summary>Master switch — the three-finger drag engine runs only when on.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Drag speed — linear multiplier applied to contact deltas before
    /// injection (the single user-facing sensitivity control; no
    /// home-grown acceleration, the relative injection already rides the
    /// Windows pointer curve).
    /// </summary>
    public double DragSpeed { get; set; } = 1.0;

    /// <summary>
    /// Diagnostics — record raw contact frames to a dedicated JSONL file
    /// under telemetry/ (independent of the master switch, so real
    /// Bluetooth sessions can be captured with the recognizer off).
    /// </summary>
    public bool RecordFrames { get; set; } = false;

    // The tuning knobs (grace delay, start threshold) were frozen into
    // TrackpadEngine constants on 2026-06-12 after hands-on calibration —
    // a stale "Tuning" object in an existing settings.json is ignored.
}
