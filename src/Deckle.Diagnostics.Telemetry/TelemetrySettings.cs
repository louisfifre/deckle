namespace Deckle.Diagnostics.Telemetry;

// User-facing consent toggles that gate the dataset JSONL sinks, persisted by
// TelemetrySettingsService.
//
// Every consent-bearing toggle ships off and only flips when the user
// explicitly opts in via a Settings page dialog. A detailed operational-log
// toggle never grants any of these consents.
public sealed class TelemetrySettings
{
    public bool LatencyEnabled       { get; set; } = false;
    // Off by default although it carries no voice: an RMS summary still
    // measures the user's microphone.
    public bool MicrophoneTelemetry  { get; set; } = false;
    public bool CorpusEnabled        { get; set; } = false;
    public bool RecordAudioCorpus    { get; set; } = false;

    // Which version of each take lands in the audio corpus WAV when
    // RecordAudioCorpus is on. Default MatchTranscription: store exactly
    // what the ASR backend received — the DSP-processed buffer when
    // transcription pre-processing is enabled, the raw capture otherwise —
    // so the corpus mirrors the engine's real input. AlwaysRaw forces the
    // untouched microphone signal regardless of the DSP, preserving a
    // re-derivable baseline.
    public AudioCorpusContent AudioCorpusContent { get; set; } = AudioCorpusContent.MatchTranscription;

    // Optional override for the purpose-specific dataset root. The application
    // log is not telemetry and is fixed under the diagnostics directory.
    public string StorageDirectory { get; set; } = "";

    // Gates the per-word autocorrect decision dataset (autocorrect.decisions.jsonl):
    // for every corrected or left-literal word on an enrolled surface, its
    // candidates, scores, margins and the guard that decided it. Carries typed
    // words by design, so it is consent-gated like every text capture.
    public bool AutocorrectDecisions { get; set; } = false;

    // Gates the typed-sentence corpus (autocorrect.text.jsonl) and the typing
    // stream (autocorrect.stream.jsonl): verbatim typed input, the heaviest text
    // capture, so it has its own consent toggle. Paste and dictation never
    // enter the typed-sentence corpus.
    public bool AutocorrectText { get; set; } = false;
}

// Selects which audio a recording contributes to the corpus WAV. Values
// persist as ints and the Whisper page casts its radio index straight to this
// enum: append only, never reorder.
public enum AudioCorpusContent
{
    MatchTranscription,
    AlwaysRaw,
}
