using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// ── AmbientCaptureGate ─────────────────────────────────────────────────────
//
// Volatile boolean remembering whether an ambient capture loop is active.
// Replaces the old legacy Deckle.Logging
// `TelemetryService.SetCaptureActive(bool)` without reintroducing a central
// telemetry hub.
//
// Consumption. Ambient producers combine this activity scope with
// `LoggingSettings.LogAmbientCaptureActivity` before emitting Verbose detail.
// While capture is active and the toggle is off, routine detail is skipped;
// outside capture, the supporting providers keep their own diagnostics.
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
    public static bool IsActive
        => OperationalLogAdmission.IsActive(OperationalLogActivity.Ambient);

    public static void SetActive(bool active)
        => OperationalLogAdmission.SetActive(OperationalLogActivity.Ambient, active);
}
