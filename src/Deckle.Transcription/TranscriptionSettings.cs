using Deckle.Transcription.Streaming;

namespace Deckle.Transcription;

// ── TranscriptionSettings ────────────────────────────────────────────────────
//
// Root POCO for the transcription module. Groups every section consumed by
// the orchestrator (TranscriptionEngine) and by the active IAsrBackend. The
// module owns its own JSON file under <UserDataRoot>/modules/transcription/
// settings.json.
public sealed class TranscriptionSettings
{
    // User override for the directory containing speech models (.bin files
    // for Whisper, plus the Silero VAD). Empty = fall back to
    // AppPaths.ModelsDirectory (= <UserDataRoot>/models/).
    public string ModelsDirectory { get; set; } = "";

    public EngineSettings          Engine          { get; set; } = new();
    public SpeechDetectionSettings SpeechDetection { get; set; } = new();
    public ConfidenceSettings      Confidence      { get; set; } = new();
    public OutputFilterSettings    OutputFilters   { get; set; } = new();
    public DecodingSettings        Decoding        { get; set; } = new();
    public ContextSettings         Context         { get; set; } = new();
    public StreamingSettings       Streaming       { get; set; } = new();
}

// Which transcription pipeline runs a recording. Monolithic = the delivered
// path (capture the whole take, one backend call doing its own internal
// windowing). Streaming = the producer/consumer socle (energy segmenter cuts
// utterances on the live stream, each transcribed as it arrives). Default
// Monolithic so an upgrade changes nothing until the user opts in; the
// monolithic path is slated for removal once streaming proves out.
public enum PipelineStrategyKind { Monolithic, Streaming }

// Settings for the streaming socle. Strategy selects the pipeline; Segmenter
// carries the energy-segmenter parameters (consulted only when Streaming is
// active). Auto-properties round-trip cleanly through JsonSettingsStore.
public sealed class StreamingSettings
{
    public PipelineStrategyKind    Strategy   { get; set; } = PipelineStrategyKind.Monolithic;
    public EnergySegmenterSettings Segmenter  { get; set; } = new();
    public SpeechTrimSettings      SpeechTrim { get; set; } = new();
}

// External Silero VAD pre-trim (Deckle.Inference.Onnx) — the active VAD now that
// the whisper-internal SpeechDetection VAD is unplugged. Streaming path only: it
// cleans each big chunk the energy segmenter cut, keeping only the speech spans
// (so a mid-utterance pause the energy threshold didn't split is removed) and
// dropping an utterance with no speech at all (an energy false positive on
// noise/silence) — the main guard against whisper hallucinating on near-silence.
// Surfaced as the single "Voice activity detection" toggle in the Whisper
// settings. The model (silero_vad.onnx) is provisioned on demand; until it is
// present the trim is a silent no-op (the first take after enabling triggers a
// one-time background download and runs untrimmed). Default on; detection
// parameters use the Silero reference defaults (SileroVadOptions) and are not
// exposed.
public sealed class SpeechTrimSettings
{
    public bool Enabled { get; set; } = true;
}

// Bootstrap parameters for the active ASR engine. The first three (Model /
// UseGpu / Language) are "heavy" settings — changing them requires reloading
// the backend's model context.
public sealed class EngineSettings
{
    public string Model { get; set; } = "ggml-large-v3.bin";
    public bool UseGpu { get; set; } = true;
    public string Language { get; set; } = "fr";
    public string InitialPrompt { get; set; } =
        "Bon. Je suis en train de coder une application Windows, je continue l'interface avec un contour animé qui tourne. " +
        "C'est plutôt propre, même si certaines parties restent fragiles. " +
        "Côté workflow, je travaille avec plusieurs branches Git, je merge sur la branche principale, je lance les tests à chaque itération. " +
        "Côté outils, .NET, Visual Studio, Python, Whisper, le shell. " +
        "Ouais, ça avance bien, même si parfois j'ai un truc cassé et il faut tout reprendre. " +
        "Voilà. Ok.";

    // Prepend initial_prompt to every 30s decode window (not just the first).
    // Stabilizes punctuation and register across long recordings.
    public bool CarryInitialPrompt { get; set; } = true;
}

// Whisper's built-in Silero VAD (a whisper_full parameter). Inert: the engine
// forces vad = 0 (see WhisperParamsMapper) and no UI binds to it anymore — the
// external Silero ONNX VAD (Streaming.SpeechTrim) replaced it. Kept, not removed,
// pending a later revisit of the built-in path; until then nothing reads these.
public sealed class SpeechDetectionSettings
{
    public bool Enabled { get; set; } = true;
    public float Threshold { get; set; } = 0.5f;
    public int MinSpeechDurationMs { get; set; } = 250;
    public int MinSilenceDurationMs { get; set; } = 500;
    public float MaxSpeechDurationSec { get; set; } = 30.0f;
    public int SpeechPadMs { get; set; } = 200;
    public float SamplesOverlap { get; set; } = 0.1f;
}

// Thresholds that trigger whisper.cpp temperature fallback. Stored as double
// for the UI (WinUI NumberBox/Slider work in double); cast to float when
// mapping to the native struct.
public sealed class ConfidenceSettings
{
    public double EntropyThreshold { get; set; } = 2.4;
    public double LogprobThreshold { get; set; } = -1.0;
    public double NoSpeechThreshold { get; set; } = 0.6;
}

public sealed class OutputFilterSettings
{
    public bool SuppressNonSpeechTokens { get; set; } = true;
    public bool SuppressBlank { get; set; } = true;
    public string SuppressRegex { get; set; } = "";
}

public sealed class DecodingSettings
{
    public double Temperature { get; set; } = 0.0;
    public double TemperatureIncrement { get; set; } = 0.2;

    // Beam search explores multiple hypotheses in parallel, picking the
    // best sequence overall. Higher quality than greedy at the cost of
    // latency. BeamSize only used when UseBeamSearch is true.
    public bool UseBeamSearch { get; set; } = true;
    public int BeamSize { get; set; } = 5;
}


// UseContext = inverse of native no_context (user-oriented vocabulary).
// MaxTokens = -1 means "auto / unlimited".
public sealed class ContextSettings
{
    public bool UseContext { get; set; } = true;
    public int MaxTokens { get; set; } = -1;
}
