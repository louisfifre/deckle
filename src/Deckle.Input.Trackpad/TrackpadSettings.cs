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
    /// under telemetry/trackpad/ (independent of the master switch, so real
    /// Bluetooth sessions can be captured with the recognizer off).
    /// </summary>
    public bool RecordFrames { get; set; } = false;

    /// <summary>
    /// Pick-up delay — how long the button press waits after three fingers
    /// land, in milliseconds; the cursor follows from the first frame either
    /// way. A four-finger gesture passes through three fingers, so the wait
    /// keeps a late fourth finger from turning into a click. The default
    /// comes from two months of recorded Magic Trackpad sessions: 50 ms
    /// holds back about 70% of those stray presses before the grabbed
    /// object visibly lags the cursor. Exposed because the right wait
    /// depends on the hand and the pad.
    /// </summary>
    public double PressDelayMs { get; set; } = 50;

    /// <summary>The range the page offers; the engine clamps a hand-edited file to it.</summary>
    public const double MinPressDelayMs = 0;
    public const double MaxPressDelayMs = 200;

    // Grace delay and start threshold stay TrackpadEngine constants, not
    // settings.
}
