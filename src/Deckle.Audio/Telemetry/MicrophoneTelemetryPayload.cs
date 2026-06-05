using System.Text.Json.Serialization;

namespace Deckle.Audio.Telemetry;

// ── MicrophoneTelemetryPayload ──────────────────────────────────────────────
//
// One row per Recording when the « Log microphone » consent toggle is on.
// dBFS percentile sweep over the 50 ms sub-window RMS series, plus the
// linear mean RMS (the value worth comparing against MaxDbfs window when
// calibrating). MeanDbfs is derived from the linear mean — log of the
// mean, not mean of the log, since arithmetic mean of dBFS values gets
// pulled too low by the silence floor.
//
// Used as an intermediate POCO between `MicrophoneTelemetryCalculator`
// (producer, pure compute) and `DeckleAudioSource.MicrophoneTelemetryRecorded`
// (consumer, flat 14-arg EventSource event). The auto-calibration loop in
// `TranscriptionEngine` also pins recent payloads to feed back into the HUD
// thresholds, hence the public surface.
//
// Carry-over from wave 6: this POCO used to live in `Deckle.Logging` (with
// `LatencyPayload`/`CorpusPayload`, which disappeared with the legacy module).
// Relocated here because it is the only payload still instantiated after the
// EventSource migration, and its natural producer is the Audio module
// calculator.
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
