using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Speech;

// ── ISpeechBackend ─────────────────────────────────────────────────────────
//
// Contract for an interchangeable TTS inference engine — the synthesis-side
// mirror of IAsrBackend. The orchestrator (SpeechEngine in this module) holds
// one backend, never touches its internals, and drives it through the methods
// below.
//
// The skeleton ships a single in-module implementation (ChatterboxSpeechBackend,
// a placeholder). The real Chatterbox port — the proven 4-graph pure-ONNX
// decode — moves to its own Deckle.Speech.Chatterbox module once it pulls the
// Microsoft.ML.OnnxRuntime dependency (that public-responsibility change is
// what earns the split).
//
// Threading. Every method may be called from a non-UI thread. SynthesizeAsync
// runs from SpeechEngine's background task; the implementation owns its own
// serialization.
public interface ISpeechBackend : IDisposable
{
    // Stable identifier for telemetry and logs ("chatterbox", ...). Not a
    // display name.
    string Name { get; }

    // True once a model is loaded in memory and ready to synthesize.
    bool IsModelLoaded { get; }

    // Compute device serving the loaded model ("CPU", "DirectML", "stub", ...);
    // null when nothing is loaded.
    string? DetectedAccelerator { get; }

    // Loads the model referenced by the module settings. The stub is a no-op;
    // the real backend pays the cold-load here.
    Task LoadModelAsync(CancellationToken ct);

    // Frees the model and any device memory it holds. Idempotent.
    void UnloadModel();

    // Synthesizes `text` in the given reference `voice` at the given LM
    // sampling `temperature`, returning 24 kHz mono float [-1, 1] samples.
    // `ct` cancels mid-synthesis.
    Task<float[]> SynthesizeAsync(string text, SpeechVoice voice, double temperature, CancellationToken ct);
}
