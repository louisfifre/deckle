namespace Deckle.Transcription;

// The swappable ASR inference engines the app can compose behind IAsrBackend. The
// composition root reads it once at startup; the values are persisted by name, so
// never rename one.
public enum AsrBackendKind
{
    Whisper,
    Parakeet,
}

// Numeric precision of the Parakeet encoder. The mel front end and the
// decoder-joint network always run in FP32.
public enum ParakeetEncoderPrecision
{
    Fp32,
    Fp16,
}
