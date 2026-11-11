using Deckle.Localization;

namespace Deckle.Audio;

// Categorical decoding of the HRESULT returned when the microphone could not be
// opened, during the pre-flight probe or by a failed capture (mapping in
// MicrophoneCapture.ClassifyError). Capture stays free of any localization
// dependency: the caller maps MicErrorKind to user-visible strings via Loc.Get
// on its side. The raw HRESULT is also returned so the caller can include it
// verbatim in error logs / format strings (MicError_Unavailable_Body_Format
// uses {0} = the code in hex).
public enum MicErrorKind
{
    None,
    NotDetected,
    InUse,
    Unavailable,
}

// Result of MicrophoneCapture.Probe. Two-step Probe + Record so the caller
// can refuse to transition Idle → Starting when the mic is unavailable.
// Stage names the step that refused when Ok is false, in the vocabulary of
// CaptureOpenResult.FailedStage (load, initialize, resolve, open). "load" is the
// native library itself failing to load, which no device change explains; it
// stays null when the probe passed or when the provider named no stage.
public readonly record struct ProbeResult(bool Ok, MicErrorKind Kind, uint ErrorCode, string? Stage = null);
