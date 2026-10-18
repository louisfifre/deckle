using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Speech;

// PLACEHOLDER backend for the read-aloud skeleton. It proves the
// engine → speaker-output chain without the heavy ONNX port: SynthesizeAsync
// ignores text / voice / temperature and returns a short 440 Hz tone at
// 24 kHz — audible, asset-free, and unmistakably a stub.
//
// The real implementation will run the 4-graph pure-ONNX Chatterbox decode:
// speech_encoder, embed_tokens, language_model_fp16, conditional_decoder.
public sealed class ChatterboxSpeechBackend : ISpeechBackend
{
    private const int SampleRate = 24000; // Chatterbox S3Gen output rate

    public string Name => "chatterbox";
    public bool IsModelLoaded => true;            // stub: nothing to load
    public string? DetectedAccelerator => "stub";

    public Task LoadModelAsync(CancellationToken ct) => Task.CompletedTask;

    public void UnloadModel() { }

    public Task<float[]> SynthesizeAsync(string text, SpeechVoice voice, double temperature, CancellationToken ct)
    {
        // TODO(speech): replace with the real Chatterbox ONNX decode (the
        // autoregressive T3 loop + S3Gen vocoder).
        DeckleSpeechSource.Log.StubSynthesis();

        const double seconds = 0.4;
        const double freq    = 440.0;
        const double amp     = 0.25;
        int n = (int)(SampleRate * seconds);
        var samples = new float[n];
        for (int i = 0; i < n; i++)
            samples[i] = (float)(amp * System.Math.Sin(2.0 * System.Math.PI * freq * i / SampleRate));
        return Task.FromResult(samples);
    }

    public void Dispose() { }
}
