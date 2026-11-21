namespace Deckle.Transcription.Parakeet;

// Parakeet backend: local speech-to-text with NVIDIA Parakeet TDT 0.6B v3 exported to ONNX,
// run on the ONNX Runtime that Windows ML ships. This module holds the pieces of that backend
// (tokenization, transducer decoding, segment building, audio chunking) and none of the UI.
// The model is a folder, not a file: an encoder in FP32 or FP16, a decoder-joint network and a
// vocabulary. ParakeetModelFiles names where each of them sits in that folder, so that no
// other file hard-codes a relative path. The mel front end is not in the folder: Deckle ships its own.

/// <summary>Locates the files of a Parakeet model folder.</summary>
/// <param name="Directory">Root of the model folder, the one that holds vocab.txt.</param>
public sealed record ParakeetModelFiles(string Directory)
{
    /// <summary>Folder name the model takes inside the speech model directory when no folder is configured.</summary>
    public const string DefaultFolderName = "parakeet-tdt-0.6b-v3-ultra";

    /// <summary>The configured model folder, or the default one inside the speech model directory.</summary>
    public static ParakeetModelFiles Resolve(string configuredDirectory, string modelsDirectory) =>
        new(string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(modelsDirectory, DefaultFolderName)
            : configuredDirectory);

    // The mel front end is the nemo128.onnx that onnx-asr 0.12.0 bundles (see NOTICE.md), copied beside
    // the app by this project, not the nemo128.onnx of the model repository. The two graphs differ: the
    // repository's counts one frame more than the audio holds (N / 160 + 1) and normalizes with it, which
    // moved greedy decoding away from the reference on 62 of 167 dictations, short ones included.
    // onnx-asr's graph matches its numpy preprocessor to 1e-5 and counts N / 160 frames.
    /// <summary>Mel-spectrogram front end shared by every precision, shipped with the app and independent of the model folder.</summary>
    public static string BundledPreprocessor { get; } =
        Path.Combine(AppContext.BaseDirectory, "parakeet-nemo128.onnx");

    /// <summary>Encoder in FP32, the default: FP16 is opt-in because it can cost fidelity on French.</summary>
    public string EncoderFp32 => Path.Combine(Directory, "fp32", "encoder-model.onnx");

    /// <summary>Encoder in FP16, the optional lighter variant.</summary>
    public string EncoderFp16 => Path.Combine(Directory, "fp16", "encoder-model.fp16.onnx");

    /// <summary>Decoder and joint network, FP32 only: it is small and runs on the CPU.</summary>
    public string DecoderJoint => Path.Combine(Directory, "fp32", "decoder_joint-model.onnx");

    /// <summary>Token vocabulary, one entry per line.</summary>
    public string Vocabulary => Path.Combine(Directory, "vocab.txt");

    /// <summary>True when the default (all-FP32) configuration can run: every file it needs is present in the model folder.</summary>
    public bool IsComplete =>
        File.Exists(EncoderFp32)
        && File.Exists(DecoderJoint)
        && File.Exists(Vocabulary);

    /// <summary>True when the optional FP16 encoder is also present.</summary>
    public bool HasFp16Encoder => File.Exists(EncoderFp16);
}
