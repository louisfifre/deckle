namespace Deckle.Diagnostics.Telemetry;

// User-facing consent toggles that gate the dataset JSONL sinks, persisted by
// TelemetrySettingsService.
//
// Every consent-bearing toggle ships off and only flips when the user
// explicitly opts in via a Settings page dialog. A detailed operational-log
// toggle never grants any of these consents.
public sealed class TelemetrySettings
{
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
}

// Selects which audio a recording contributes to the corpus WAV. Values
// persist as ints and the Transcription page casts its radio index straight to this
// enum: append only, never reorder.
public enum AudioCorpusContent
{
    MatchTranscription,
    AlwaysRaw,
}
