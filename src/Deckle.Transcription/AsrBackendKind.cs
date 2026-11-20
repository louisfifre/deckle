using System.Text.Json.Serialization;

namespace Deckle.Transcription;

// The swappable ASR inference engines the app can compose behind IAsrBackend. The
// composition root reads it once at startup; the values are persisted by name, so
// never rename one.
[JsonConverter(typeof(JsonStringEnumConverter<AsrBackendKind>))]
public enum AsrBackendKind
{
    Whisper,
    Parakeet,
}

// Numeric precision of the Parakeet encoder. The mel front end and the
// decoder-joint network always run in FP32. Persisted by name, like the engine choice.
[JsonConverter(typeof(JsonStringEnumConverter<ParakeetEncoderPrecision>))]
public enum ParakeetEncoderPrecision
{
    Fp32,
    Fp16,
}
