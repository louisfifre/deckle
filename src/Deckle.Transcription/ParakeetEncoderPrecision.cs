using System.Text.Json.Serialization;

namespace Deckle.Transcription;

// Numeric precision of the Parakeet encoder. The mel front end and the
// decoder-joint network always run in FP32. Persisted by name, so never rename a value.
[JsonConverter(typeof(JsonStringEnumConverter<ParakeetEncoderPrecision>))]
public enum ParakeetEncoderPrecision
{
    Fp32,
    Fp16,
}
