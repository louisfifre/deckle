namespace Deckle.Diagnostics.Telemetry;

// User-facing consent toggles that gate the structured telemetry
// listeners. POCO with per-module persistence — loaded / saved by
// TelemetrySettingsService.
//
// Defaults are cautious: every consent-bearing toggle ships off and
// only flips when the user explicitly opts in via a Settings page
// dialog. Reproduces the legacy Deckle.Logging.TelemetrySettings
// posture.
public sealed class TelemetrySettings
{
    public bool LatencyEnabled       { get; set; } = false;
    public bool MicrophoneTelemetry  { get; set; } = false;
    public bool CorpusEnabled        { get; set; } = false;
    public bool RecordAudioCorpus    { get; set; } = false;

    // Which version of each take lands in the audio corpus WAV when
    // RecordAudioCorpus is on. Default MatchTranscription: store exactly
    // what the ASR backend received — the DSP-processed buffer when
    // transcription pre-processing is enabled, the raw capture otherwise —
    // so the corpus mirrors the engine's real input. AlwaysRaw forces the
    // untouched microphone signal regardless of the DSP, preserving a
    // re-derivable baseline (the original ADR-0011 posture). See ADR-0011.
    public AudioCorpusContent AudioCorpusContent { get; set; } = AudioCorpusContent.MatchTranscription;

    // ApplicationLogToDisk gate l'écriture du journal applicatif sur
    // disque (app.jsonl). StorageDirectory est l'override optionnel
    // du dossier racine des fichiers JSONL ; vide = défaut résolu par
    // AppPaths.
    public bool   ApplicationLogToDisk { get; set; } = false;
    public string StorageDirectory     { get; set; } = "";
}

// Selects which audio a recording contributes to the corpus WAV. Two
// values today; a third (e.g. store both) can be appended without
// shifting the persisted ints. Order matters — it mirrors the
// RadioButtons order on the Diagnostics page (0 = MatchTranscription,
// 1 = AlwaysRaw).
public enum AudioCorpusContent
{
    MatchTranscription,
    AlwaysRaw,
}
