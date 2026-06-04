namespace Deckle.Diagnostics.Logging;

// ── AmbientCaptureGate ─────────────────────────────────────────────────────
//
// Volatile boolean remembering whether an ambient capture loop is active.
// Replaces the old legacy Deckle.Logging
// `TelemetryService.SetCaptureActive(bool)` without reintroducing a central
// telemetry hub.
//
// Consumption. The gate is consulted by delegates injected into
// `LogWindowEventListener` and into the app.jsonl predicate at App boot. The
// filter combines this gate with the user toggle
// `LoggingSettings.LogAmbientCaptureActivity` to decide whether a Verbose
// emission from Ambient / Vision / Lighting providers should land in the live
// or persistent journal. While the gate is open (capture loop active) AND the
// toggle is off, ambient Verbose events are silenced; outside capture,
// everything passes.
//
// Emission. The gate itself emits no EventSource event: it is pure shared
// state. Transitions are already logged at application level by
// `DeckleAmbientSource.PipelineStarted` / `PipelineStopped`, and UI surfaces
// can observe these events without going through the gate.
//
// Threading. `volatile` guarantees cross-thread visibility without requiring a
// lock: the ambient engine flips the gate from its driving thread, and each
// emission reads the value without synchronization. Races (a Verbose event
// arriving exactly during the flip) are benign: the event passes or is
// filtered, never corrupted.
public static class AmbientCaptureGate
{
    private static volatile bool _active;

    public static bool IsActive => _active;

    public static void SetActive(bool active) => _active = active;
}
