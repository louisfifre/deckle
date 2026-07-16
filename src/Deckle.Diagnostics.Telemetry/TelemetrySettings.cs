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
    // re-derivable baseline required by the normalized corpus contract.
    public AudioCorpusContent AudioCorpusContent { get; set; } = AudioCorpusContent.MatchTranscription;

    // Optional override for the purpose-specific dataset root. The application
    // log is not telemetry and is fixed under the diagnostics directory.
    public string StorageDirectory { get; set; } = "";

    // AutocorrectDecisionsEnabled gates the per-word autocorrect decision dataset
    // (autocorrect.decisions.jsonl): for every corrected or left-literal word on an
    // enrolled surface, its candidates, scores, margins and the guard that decided
    // it. Carries typed words by design — the diagnostic surface for tuning the
    // corrector — so it is consent-gated and ships off like every text capture.
    public bool AutocorrectDecisions { get; set; } = false;

    // AutocorrectText gates the typed-sentence corpus (autocorrect.text.jsonl): for
    // each sentence typed at the keyboard on an enrolled surface, the verbatim typed
    // form paired with the corrected one. The substrate for modelling the user's own
    // error patterns (e.g. ';' for an apostrophe). The heaviest text capture — a
    // verbatim record of typed input — so it has its own consent toggle, off by
    // default. Paste and dictation never enter it.
    public bool AutocorrectText { get; set; } = false;
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
