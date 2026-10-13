using Deckle.Transcription;

namespace Deckle.Transcription;

// ── TranscriptionSettings ────────────────────────────────────────────────────
//
// Root POCO for the transcription module, read by the orchestrator
// (TranscriptionEngine) and by the active IAsrBackend.
public sealed class TranscriptionSettings
{
    // User override for the directory containing speech models (.bin files
    // for Whisper, plus the Silero VAD). Empty = fall back to
    // AppPaths.ModelsDirectory (= <UserDataRoot>/models/).
    public string ModelsDirectory { get; set; } = "";

    public EngineSettings          Engine          { get; set; } = new();
    public ConfidenceSettings      Confidence      { get; set; } = new();
    public OutputFilterSettings    OutputFilters   { get; set; } = new();
    public DecodingSettings        Decoding        { get; set; } = new();
    public ContextSettings         Context         { get; set; } = new();
    public StreamingSettings       Streaming       { get; set; } = new();
}

// Which pipeline runs a dictation. Monolithic captures the whole take, then one
// backend call does its own internal windowing. Streaming has the energy
// segmenter cut utterances on the live stream, each transcribed as it arrives.
public enum PipelineStrategyKind { Monolithic, Streaming }

// Strategy selects the dictation pipeline. Segmenter and SpeechTrim drive the
// segmented session, which streaming dictation and file transcription share.
public sealed class StreamingSettings
{
    public PipelineStrategyKind    Strategy   { get; set; } = PipelineStrategyKind.Monolithic;
    public EnergySegmenterSettings Segmenter  { get; set; } = new();
    public SpeechTrimSettings      SpeechTrim { get; set; } = new();
}

// External Silero VAD pre-trim (Deckle.Vad), applied in the segmented session.
// It cleans each chunk the energy segmenter cut, keeping only the speech spans
// (so a mid-utterance pause the energy threshold didn't split is removed) and
// dropping an utterance with no speech at all (an energy false positive on
// noise/silence) — the main guard against whisper hallucinating on near-silence.
// The model (silero_vad.onnx) is provisioned on demand; until it is present the
// trim is a silent no-op (the first take after enabling triggers a one-time
// background download and runs untrimmed). The four parameters default to the
// Silero reference values (SileroVadOptions) and map straight onto a
// SileroVadOptions built per take.
public sealed class SpeechTrimSettings
{
    public bool Enabled { get; set; } = true;

    // Speech probability at/above which a window counts as speech. Higher trims more
    // aggressively (risks clipping soft speech); the release threshold is derived
    // (Threshold - 0.15, hysteresis).
    public float Threshold { get; set; } = 0.5f;

    // A detected speech span shorter than this is discarded as a blip.
    public int MinSpeechDurationMs { get; set; } = 250;

    // A silence shorter than this does not close an open span, so an intra-phrase
    // pause is bridged rather than cut.
    public int MinSilenceDurationMs { get; set; } = 100;

    // Each kept span is padded by this much on both sides, so a word onset or tail
    // is not clipped.
    public int SpeechPadMs { get; set; } = 30;
}

// Bootstrap parameters for the active ASR engine. Model and UseGpu are
// load-time settings (the page asks for a restart when they change); Language
// and the prompt apply on every call.
public sealed class EngineSettings
{
    public string Model { get; set; } = "ggml-base.bin";
    public bool UseGpu { get; set; } = true;
    public string Language { get; set; } = "fr";

    // A style sample, not an instruction: Whisper is not instruction-tuned, so
    // meta phrases leak into the output. Never put a raw-speech → clean-text
    // example in it.
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

// Thresholds that trigger whisper.cpp temperature fallback. Stored as double
// for the UI (WinUI NumberBox/Slider work in double); cast to float when
// mapping to the native struct.
public sealed class ConfidenceSettings
{
    // whisper.cpp fails a decode when its token entropy falls below this value,
    // so a higher threshold is stricter, not laxer.
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

    // BeamSize applies only when UseBeamSearch is true.
    public bool UseBeamSearch { get; set; } = true;
    public int BeamSize { get; set; } = 5;
}


// UseContext = inverse of native no_context.
// MaxTokens <= 0 means "auto": whisper.cpp keeps its own default.
public sealed class ContextSettings
{
    public bool UseContext { get; set; } = true;
    public int MaxTokens { get; set; } = -1;
}
