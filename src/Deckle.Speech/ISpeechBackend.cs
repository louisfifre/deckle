using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Speech;

// ── ISpeechBackend ─────────────────────────────────────────────────────────
//
// Contract for an interchangeable TTS inference engine, the synthesis-side
// mirror of IAsrBackend. SpeechEngine holds one backend and never touches its
// internals.
//
// The real Chatterbox port moves to its own Deckle.Speech.Chatterbox module
// once it pulls Microsoft.ML.OnnxRuntime: that public dependency earns the split.
//
// Threading. Every method may be called from a non-UI thread. SynthesizeAsync
// runs from SpeechEngine's background task; the implementation owns its own
// serialization.
public interface ISpeechBackend : IDisposable
{
    // Stable identifier for telemetry and logs ("chatterbox", ...). Not a
    // display name.
    string Name { get; }

    bool IsModelLoaded { get; }

    // Compute device serving the loaded model ("CPU", "DirectML", "stub", ...);
    // null when nothing is loaded.
    string? DetectedAccelerator { get; }

    // The real backend pays the cold-load here.
    Task LoadModelAsync(CancellationToken ct);

    // Idempotent.
    void UnloadModel();

    // Synthesizes `text` in the given reference `voice` at the given LM
    // sampling `temperature`, returning 24 kHz mono float [-1, 1] samples.
    // `ct` cancels mid-synthesis.
    Task<float[]> SynthesizeAsync(string text, SpeechVoice voice, double temperature, CancellationToken ct);
}
