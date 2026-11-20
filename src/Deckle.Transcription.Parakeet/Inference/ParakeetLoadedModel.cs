using Deckle.Transcription.Parakeet.Decoding;
using Deckle.Transcription.Parakeet.Tokenization;

namespace Deckle.Transcription.Parakeet;

/// <summary>A Parakeet model held in memory: everything a transcription call needs, and what frees it.</summary>
internal sealed class ParakeetLoadedModel : IDisposable
{
    private readonly IDisposable[] _owned;

    public ParakeetLoadedModel(
        ParakeetVocabulary vocabulary,
        IParakeetEncoder encoder,
        ITdtJoint joint,
        string accelerator,
        string? gpuFallbackExceptionType = null,
        params IDisposable[] owned)
    {
        Vocabulary = vocabulary;
        Encoder = encoder;
        Joint = joint;
        Accelerator = accelerator;
        GpuFallbackExceptionType = gpuFallbackExceptionType;
        _owned = owned;
    }

    public ParakeetVocabulary Vocabulary { get; }
    public IParakeetEncoder Encoder { get; }
    public ITdtJoint Joint { get; }

    /// <summary>Where the encoder runs: <see cref="ParakeetAccelerators"/>.</summary>
    public string Accelerator { get; }

    /// <summary>
    /// Set when the GPU was asked for and the encoder runs on the CPU instead: the type of the
    /// error that refused the device. Null when the GPU was not asked for or was obtained.
    /// </summary>
    public string? GpuFallbackExceptionType { get; }

    public void Dispose()
    {
        foreach (IDisposable owned in _owned) owned.Dispose();
    }
}

/// <summary>The accelerator names <see cref="IAsrBackend.DetectedAccelerator"/> reports for Parakeet.</summary>
internal static class ParakeetAccelerators
{
    public const string Cpu = "CPU";
    public const string DirectMl = "DirectML";
}

/// <summary>Result of loading a model: either the model, or a stable reason it could not be.</summary>
/// <param name="Model">The loaded model; null on failure.</param>
/// <param name="ErrorReason">Stable reason ("file_not_found", "init_failed"); null on success.</param>
/// <param name="FailedPath">The file the load stopped on or the model folder; null on success.</param>
/// <param name="ExceptionType">Type of the error behind "init_failed"; empty otherwise.</param>
/// <param name="ExceptionMessage">Message of that error; empty otherwise.</param>
internal sealed record ParakeetLoadOutcome(
    ParakeetLoadedModel? Model,
    string? ErrorReason,
    string? FailedPath,
    string ExceptionType = "",
    string ExceptionMessage = "")
{
    public static ParakeetLoadOutcome Loaded(ParakeetLoadedModel model) => new(model, null, null);

    public static ParakeetLoadOutcome Failed(string reason, string path, string exceptionType = "", string exceptionMessage = "") =>
        new(null, reason, path, exceptionType, exceptionMessage);
}

/// <summary>Builds a <see cref="ParakeetLoadedModel"/> from a model folder; the seam the backend loads through.</summary>
internal interface IParakeetModelLoader
{
    ParakeetLoadOutcome Load(ParakeetModelFiles files, ParakeetEncoderPrecision precision, bool useGpu);
}
