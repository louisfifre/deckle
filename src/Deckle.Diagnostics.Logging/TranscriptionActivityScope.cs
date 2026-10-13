using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// Process-wide activity state for a complete transcription workflow: dictation
// (monolithic or streaming) and file transcription, including final delivery.
// Sister to AmbientCaptureGate, same shape, same rationale: transcription
// producers combine it with LoggingSettings.LogTranscriptionActivity before
// emitting Verbose detail, and the gate itself emits no event.
public static class TranscriptionActivityGate
{
    public static bool IsActive
        => OperationalLogAdmission.IsActive(OperationalLogActivity.Transcription);

    public static void SetActive(bool active)
        => OperationalLogAdmission.SetActive(OperationalLogActivity.Transcription, active);
}

// `using var _ = TranscriptionActivityScope.Open();` encloses the whole worker,
// so an early return or exception can never leave the activity stuck open.
public readonly struct TranscriptionActivityScope : System.IDisposable
{
    public static TranscriptionActivityScope Open()
    {
        TranscriptionActivityGate.SetActive(true);
        return default;
    }

    public void Dispose() => TranscriptionActivityGate.SetActive(false);
}
