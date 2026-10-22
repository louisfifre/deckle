namespace Deckle.Audio;

// Categorical decoding of MMSYSERR codes returned by waveInOpen during the
// pre-flight probe or by a failed capture (mapping in
// MicrophoneCapture.ClassifyError). Capture stays
// free of any localization dependency: the caller maps MicErrorKind to
// user-visible strings via Loc.Get on its side. The raw MMSYSERR value is also
// returned so the caller can include it verbatim in error logs / format
// strings (MicError_Unavailable_Body_Format uses {0} = err).
public enum MicErrorKind
{
    None,
    NotDetected,
    InUse,
    Unavailable,
}

// Result of MicrophoneCapture.Probe. Two-step Probe + Record so the caller
// can refuse to transition Idle → Starting when the mic is unavailable.
public readonly record struct ProbeResult(bool Ok, MicErrorKind Kind, uint MmsysErr);
