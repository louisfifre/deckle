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

// VAD Silero — pré-filtre qui détecte les segments de parole avant Whisper.
// Quand Enabled=false, tous les autres champs sont ignorés par l'engine.
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

// Seuils qui déclenchent le fallback température de whisper.cpp. Stockés en
// double pour l'UI (NumberBox/Slider WinUI travaillent en double) ; cast en
// float au moment du mapping vers la struct native.
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


// UseContext = inverse de no_context natif (vocabulaire orienté utilisateur).
// MaxTokens = -1 signifie « auto / illimité ».
public sealed class ContextSettings
{
    public bool UseContext { get; set; } = true;
    public int MaxTokens { get; set; } = -1;
}
