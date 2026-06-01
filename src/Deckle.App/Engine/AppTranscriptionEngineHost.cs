using Deckle.Audio;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;

namespace Deckle.App;

// ── AppTranscriptionEngineHost ────────────────────────────────────────────────────────
//
// App-side implementation of ITranscriptionEngineHost. The engine reads its
// settings through this bridge so Deckle.Transcription can stay free of any
// reference to the App project or to the shell SettingsService.
//
// After slice C2b each module owns its own settings file
// (modules/<id>/settings.json) and its own static service singleton —
// so this bridge just forwards to the relevant XxxSettingsService.Instance.
// Reads happen on every access (no caching): a setting flipped through the
// Settings UI takes effect on the next read with no event subscription needed.
internal sealed class AppTranscriptionEngineHost : ITranscriptionEngineHost
{
    public TranscriptionSettings Transcription => TranscriptionSettingsService.Instance.Current;
    public CaptureSettings       Audio         => CaptureSettingsService.Instance.Current;
    public TelemetrySettings     Telemetry     => TelemetrySettingsService.Instance.Current;
    public LlmSettings           Llm           => LlmSettingsService.Instance.Current;

    public string ResolveModelsDirectory() => TranscriptionSettingsService.Instance.ResolveModelsDirectory();

    // The single engine-side caller is the auto-calibration path which
    // mutates Audio.LevelWindow in place. So saving the audio module is
    // the only Save the engine drives. If a future engine path needs to
    // save a different module, add a typed hook on ITranscriptionEngineHost
    // rather than overloading this one.
    public void SaveSettings() => CaptureSettingsService.Instance.Save();

    public void ApplyLevelWindow(LevelWindowSettings lw) => App.ApplyLevelWindow(lw);
}
