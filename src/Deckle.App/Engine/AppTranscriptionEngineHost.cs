using Deckle.Audio;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;
using Deckle.Modules;
using Deckle.Transcription;

namespace Deckle.App;

// No caching: a setting changed in the UI takes effect on the next read, with no
// event subscription.
internal sealed class AppTranscriptionEngineHost : ITranscriptionEngineHost
{
    public TranscriptionSettings Transcription => TranscriptionSettingsService.Instance.Current;
    public CaptureSettings       Audio         => CaptureSettingsService.Instance.Current;
    public TelemetrySettings     Telemetry     => TelemetrySettingsService.Instance.Current;

    // With Rewrite absent the engine sees a disabled, profile-less LlmSettings,
    // and LlmSettingsService is never instantiated: its constructor would create
    // modules/llm/settings.json on disk.
    public LlmSettings Llm => RewriteAvailability.Settings(
        ModulePresence.IsPresent(ModuleIds.Rewrite),
        () => LlmSettingsService.Instance.Current);

    public string ResolveModelsDirectory() => TranscriptionSettingsService.Instance.ResolveModelsDirectory();
}
