using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// Whether an ambient capture loop is active. Ambient producers combine this
// activity scope with `LoggingSettings.LogAmbientCaptureActivity` before
// emitting Verbose detail: while capture is active and the toggle is off,
// routine detail is skipped; outside capture, the supporting providers keep
// their own diagnostics.
//
// The gate emits no event: `DeckleAmbientSource.PipelineStarted` /
// `PipelineStopped` already log the transitions.
//
// No lock: the ambient engine flips the gate from its own thread and each
// emission reads it without synchronization. A Verbose event racing the flip
// passes or is filtered, never corrupted.
public static class AmbientCaptureGate
{
    public static bool IsActive
        => OperationalLogAdmission.IsActive(OperationalLogActivity.Ambient);

    public static void SetActive(bool active)
        => OperationalLogAdmission.SetActive(OperationalLogActivity.Ambient, active);
}
