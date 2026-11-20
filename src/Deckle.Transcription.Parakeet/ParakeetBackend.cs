namespace Deckle.Transcription.Parakeet;

// IAsrBackend implementation backed by NVIDIA Parakeet TDT 0.6B v3 exported to ONNX and run on
// ONNX Runtime. Everything model-specific (sessions, tensors, tokens, frames) stays behind this
// class, so the orchestrator in Deckle.Transcription only deals with the IAsrBackend contract.
// The inference itself sits behind IParakeetModelLoader and IParakeetEncoder, which is what lets
// the orchestration below be tested without a model.
public sealed partial class ParakeetBackend : IAsrBackend
{
    public string Name => "parakeet";

    private readonly ITranscriptionEngineHost _host;
    private readonly IParakeetModelLoader _loader;

    // Held while a model loads or unloads. Transcription does not take it: a transcription takes
    // _transcribeLock, and Unload takes both in that order, so an unload waits for a running call
    // and no call ever reads a model that is being freed.
    private readonly object _modelLock = new();

    // The ONNX sessions are not meant to be driven by two calls at once, and the decoding state
    // (the joint's recurrent state) is per call. The engine already keeps the prime and the real
    // transcription apart; this lock keeps the backend safe without relying on its caller.
    private readonly object _transcribeLock = new();

    // volatile: a background thread (idle timer, status read) must see the real reference.
    private volatile ParakeetLoadedModel? _model;
    private bool _disposed;

    // Open while consecutive loads fail: the engine retries at every take, and the load-failure
    // mirror fires once per incident.
    private bool _loadFailureOpen;

    // Open while the encoder runs on the CPU although the GPU was asked for; closed by the next
    // load that gets what the settings ask.
    private bool _gpuFallbackOpen;

    public bool IsModelLoaded => _model is not null;
    public string? DetectedAccelerator => _model?.Accelerator;

    public ParakeetBackend(ITranscriptionEngineHost host)
        : this(host, new OnnxParakeetModelLoader())
    {
    }

    internal ParakeetBackend(ITranscriptionEngineHost host, IParakeetModelLoader loader)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(loader);
        _host = host;
        _loader = loader;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnloadModel();
    }
}
