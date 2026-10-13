namespace Deckle.Input;

// Persisted intent for mouse-wheel observation on the shared input host: one
// diagnostic that records raw wheel events to JSONL for the measurement
// package. Mutate then call MouseWheelSettingsService.Save().
public sealed class MouseWheelSettings
{
    /// <summary>
    /// Diagnostics — record raw wheel events (cadence, deltas, axes) to a
    /// dedicated JSONL file under telemetry/mouse-wheel/. Holds the shared
    /// keyboard/mouse input host up for the duration, independent of any other
    /// consumer.
    /// </summary>
    public bool RecordEvents { get; set; } = false;
}
