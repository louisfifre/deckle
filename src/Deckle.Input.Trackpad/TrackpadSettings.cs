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

    /// <summary>
    /// TEMPORARY tuning knobs, exposed in the page's diagnostics expander
    /// while defaults get calibrated on real sessions. Frozen into engine
    /// constants (and removed from the page) at the value-freeze
    /// milestone.
    /// </summary>
    public TrackpadTuning Tuning { get; set; } = new();
}

// Defaults are engineering guesses pending calibration on real Magic
// Trackpad 2 Bluetooth sessions — that calibration is the whole reason
// these are settings at all.
public sealed class TrackpadTuning
{
    /// <summary>Grace delay after fingers lift before the drag releases, in milliseconds.</summary>
    public int GraceDelayMs { get; set; } = 350;

    /// <summary>
    /// Travel before a three-finger touch commits to a drag instead of a
    /// tap, as a fraction of the device's logical X range.
    /// </summary>
    public double StartThresholdRatio { get; set; } = 0.01;

    /// <summary>
    /// Baseline logical-units → mickeys factor the speed multiplier
    /// applies on top of.
    /// </summary>
    public double BaseScale { get; set; } = 0.25;
}
