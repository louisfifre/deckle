namespace Deckle.Input;

// Persisted intent for mouse-wheel observation on the shared input host.
// Palier 0 of the wheel→touchpad work exposes a single diagnostic — record
// raw wheel events to JSONL for the measurement package. Mutate then call
// MouseWheelSettingsService.Save(), the standard module-settings pattern.
//
// It lives in Deckle.Input, beside the host and recorder it drives, because
// at this stage the only knob is a diagnostic of the input host itself.
// When the gesture model arrives it gets its own domain module and its own
// settings; this flag can move there then.
public sealed class MouseWheelSettings
{
    /// <summary>
    /// Diagnostics — record raw wheel events (cadence, deltas, axes) to a
    /// dedicated JSONL file under telemetry/. Holds the shared keyboard/mouse
    /// input host up for the duration, independent of any other consumer.
    /// </summary>
    public bool RecordEvents { get; set; } = false;
}
