using Deckle.Audio;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;

namespace Deckle.Transcription;

// ── ITranscriptionEngineHost ──────────────────────────────────────────────────────────
//
// Bridge that lets the engine read its settings and call back into the App
// without any reference to the App project. The App-side implementation reads
// each module's settings service on every access.
public interface ITranscriptionEngineHost
{
    TranscriptionSettings Transcription { get; }
    CaptureSettings       Audio         { get; }
    TelemetrySettings     Telemetry     { get; }
    LlmSettings           Llm           { get; }

    // The speech model directory: TranscriptionSettings.ModelsDirectory when
    // set, else AppPaths.ModelsDirectory.
    string ResolveModelsDirectory();

    // Auto-calibration writes back to LevelWindow then asks the host to
    // persist it.
    void SaveSettings();

    // Pushes the recalibrated LevelWindow into the live audio RMS mapper.
    void ApplyLevelWindow(LevelWindowSettings lw);
}
