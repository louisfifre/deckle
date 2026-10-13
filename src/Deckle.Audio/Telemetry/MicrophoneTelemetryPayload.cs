using System.Text.Json.Serialization;

namespace Deckle.Audio;

// ── MicrophoneTelemetryPayload ──────────────────────────────────────────────
//
// One recording's dBFS distribution over its sub-window RMS series. Public
// because TranscriptionEngine's auto-calibration keeps recent payloads. Emitted
// only when microphone telemetry is on, flattened field for field into
// DeckleAudioSource.MicrophoneTelemetryRecorded and
// DeckleWhispSource.PreprocessedTelemetryRecorded: a new field goes there too.
public sealed record MicrophoneTelemetryPayload(
    [property: JsonPropertyName("duration_seconds")] double DurationSeconds,
    [property: JsonPropertyName("samples")]          int    Samples,
    [property: JsonPropertyName("min_dbfs")]         double MinDbfs,
    [property: JsonPropertyName("p10_dbfs")]         double P10Dbfs,
    [property: JsonPropertyName("p25_dbfs")]         double P25Dbfs,
    [property: JsonPropertyName("p50_dbfs")]         double P50Dbfs,
    [property: JsonPropertyName("p75_dbfs")]         double P75Dbfs,
    [property: JsonPropertyName("p90_dbfs")]         double P90Dbfs,
    [property: JsonPropertyName("max_dbfs")]         double MaxDbfs,
    [property: JsonPropertyName("mean_rms")]         double MeanRms,
    [property: JsonPropertyName("mean_dbfs")]        double MeanDbfs,
    [property: JsonPropertyName("tail_rms")]         double TailRms,
    [property: JsonPropertyName("tail_dbfs")]        double TailDbfs,
    [property: JsonPropertyName("tail_state")]       string TailState);
