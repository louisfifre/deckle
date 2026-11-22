using Deckle.Transcription;

namespace Deckle.Transcription;

// ── TranscriptionSettings ────────────────────────────────────────────────────
//
// Root POCO for the transcription module, read by the orchestrator
// (TranscriptionEngine) and by the active IAsrBackend.
public sealed class TranscriptionSettings
{
    // User override for the directory containing the speech models (the
    // Parakeet model folder and the Silero VAD). Empty = fall back to
    // AppPaths.ModelsDirectory (= <UserDataRoot>/models/).
    public string ModelsDirectory { get; set; } = "";

    public EngineSettings          Engine          { get; set; } = new();
    public StreamingSettings       Streaming       { get; set; } = new();
    public LexiconSettings         Lexicon         { get; set; } = new();
}

// The person's own words: names, product names, jargon the speech model does
// not know or writes in the wrong case. A transducer such as Parakeet has no
// text prompt to carry them, so they act in two places: the decoder gives the
// tokens that spell them a bonus (Boost), and the final text takes their exact
// casing (always on, it only changes letter case). The autocorrect reads the
// same list and treats these words as the person's own. The strength of the
// boost is a constant of the Parakeet backend, not a setting; a file from 0.144
// still carrying "boostStrength" loads, the key is ignored and dropped at the
// next save.
public sealed class LexiconSettings
{
    // One word or phrase per entry, written the way it must appear: "Deckle",
    // "WinUI", "Ada Lovelace". Order carries no meaning.
    public List<string> Terms { get; set; } = [];

    // Whether the decoder favors the terms. Off keeps only the casing pass.
    public bool BoostEnabled { get; set; } = true;
}

// Which pipeline runs a dictation. Monolithic captures the whole take, then one
// backend call does its own internal windowing. Streaming has the energy
// segmenter cut utterances on the live stream, each transcribed as it arrives.
public enum PipelineStrategyKind { Monolithic, Streaming }

// Strategy selects the dictation pipeline. Segmenter and SpeechTrim drive the
// segmented session, which streaming dictation and file transcription share.
public sealed class StreamingSettings
{
    public PipelineStrategyKind    Strategy   { get; set; } = PipelineStrategyKind.Streaming;
    public EnergySegmenterSettings Segmenter  { get; set; } = new();
    public SpeechTrimSettings      SpeechTrim { get; set; } = new();

    // The floor under which a sound is not speech. One value, two scopes: the
    // energy segmenter drops an utterance whose whole voiced extent is shorter,
    // and Silero drops each speech span inside an utterance that is shorter.
    // The segmenter's floor is the only guard when Silero is off or its model
    // is missing.
    public int MinSpeechMs { get; set; } = 250;
}

// External Silero VAD pre-trim (Deckle.Vad), applied to each utterance of the
// segmented session and, once, to the whole take of a monolithic dictation.
// It cleans each chunk the energy segmenter cut, keeping only the speech spans
// (so a mid-utterance pause the voice level didn't split is removed) and
// dropping an utterance with no speech at all (an energy false positive on
// noise/silence) — the main guard against the model writing text over near-silence.
// The model (silero_vad.onnx) is provisioned on demand; until it is present the
// trim is a silent no-op (the first take after enabling triggers a one-time
// background download and runs untrimmed). The three parameters default to the
// Silero reference values (SileroVadOptions) and map straight onto a
// SileroVadOptions built per take; its minimum speech duration is the shared
// StreamingSettings.MinSpeechMs.
public sealed class SpeechTrimSettings
{
    public bool Enabled { get; set; } = true;

    // Speech probability at/above which a window counts as speech. Higher trims more
    // aggressively (risks clipping soft speech); the release threshold is derived
    // (Threshold - 0.15, hysteresis).
    public float Threshold { get; set; } = 0.5f;

    // A silence shorter than this does not close an open span, so an intra-phrase
    // pause is bridged rather than cut.
    public int MinSilenceDurationMs { get; set; } = 100;

    // Each kept span is padded by this much on both sides, so a word onset or tail
    // is not clipped.
    public int SpeechPadMs { get; set; } = 30;
}

// Bootstrap parameters for the speech engine. The model folder, its encoder
// precision and UseGpu are load-time settings, read at each model load: a
// change applies at the next load, after the idle unload or the restart the
// page offers.
//
// An older settings file may still carry the engine choice ("backend") and
// the Whisper-only values (model, language, initialPrompt, carryInitialPrompt,
// and the confidence, outputFilters, decoding and context sections): the
// serializer ignores keys it does not know, so such a file loads with every
// other value intact, and the next save drops them.
public sealed class EngineSettings
{
    // The folder name the model takes inside the speech model directory when no
    // folder is configured. Here, beside the setting it answers, so the engine's
    // observability and the backend's file layout name the same default.
    public const string DefaultParakeetFolderName = "parakeet-tdt-0.6b-v3-ultra";

    // Folder holding the model (vocab.txt, fp32/, fp16/).
    // Empty = DefaultParakeetFolderName inside the speech model directory.
    public string ParakeetModelDirectory { get; set; } = "";

    // FP32 is the default because FP16 can cost fidelity on French speech;
    // FP16 halves the memory the encoder takes.
    public ParakeetEncoderPrecision ParakeetEncoder { get; set; } = ParakeetEncoderPrecision.Fp32;

    public bool UseGpu { get; set; } = true;
}