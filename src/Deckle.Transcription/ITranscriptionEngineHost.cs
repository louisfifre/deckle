using Deckle.Audio;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;

namespace Deckle.Transcription;

// ── ITranscriptionEngineHost ──────────────────────────────────────────────────────────
//
// Bridge that lets the engine read its dependencies without touching App's
// SettingsService. The App-side implementation reads from SettingsService
// on each access; this keeps the engine free of any reference to the App
// project or to the root AppSettings POCO.
public interface ITranscriptionEngineHost
{
    TranscriptionSettings Transcription { get; }
    CaptureSettings       Audio         { get; }
    TelemetrySettings     Telemetry     { get; }
    LlmSettings           Llm           { get; }

    // Used by the active IAsrBackend to resolve the speech model path.
    // Returns the directory where model .bin files live (typically
    // <UserDataRoot>\models\, may be overridden via TranscriptionSettings
    // .ModelsDirectory).
    string ResolveModelsDirectory();

    // Auto-calibration writes back to LevelWindow then asks the host to
    // persist. The host owns the SettingsService.Save() call.
    void SaveSettings();

    // Notify the host that LevelWindow values changed so it can push them
    // into HudChrono statics (App.ApplyLevelWindow). Called from the engine
    // after auto-calibration. Pass the live LevelWindowSettings.
    void ApplyLevelWindow(LevelWindowSettings lw);
}
