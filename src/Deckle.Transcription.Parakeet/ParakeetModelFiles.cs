namespace Deckle.Transcription.Parakeet;

// Parakeet backend: local speech-to-text with NVIDIA Parakeet TDT 0.6B v3 exported to ONNX,
// run on the ONNX Runtime that Windows ML ships. This module holds the pieces of that backend
// (tokenization, transducer decoding, segment building, audio chunking) and none of the UI.
// The model is a folder, not a file: a mel front end (nemo128), an encoder in FP32 or FP16,
// a decoder-joint network and a vocabulary. ParakeetModelFiles names where each of them sits
// in that folder, so that no other file hard-codes a relative path.

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

    /// <summary>Mel-spectrogram front end shared by every precision.</summary>
    public string Preprocessor => Path.Combine(Directory, "nemo128.onnx");

    /// <summary>Encoder in FP32, the default: FP16 is opt-in because it can cost fidelity on French.</summary>
    public string EncoderFp32 => Path.Combine(Directory, "fp32", "encoder-model.onnx");

    /// <summary>Encoder in FP16, the optional lighter variant.</summary>
    public string EncoderFp16 => Path.Combine(Directory, "fp16", "encoder-model.fp16.onnx");

    /// <summary>Decoder and joint network, FP32 only: it is small and runs on the CPU.</summary>
    public string DecoderJoint => Path.Combine(Directory, "fp32", "decoder_joint-model.onnx");

    /// <summary>Token vocabulary, one entry per line.</summary>
    public string Vocabulary => Path.Combine(Directory, "vocab.txt");

    /// <summary>True when the default (all-FP32) configuration can run: every file it needs is present.</summary>
    public bool IsComplete =>
        File.Exists(Preprocessor)
        && File.Exists(EncoderFp32)
        && File.Exists(DecoderJoint)
        && File.Exists(Vocabulary);

    /// <summary>True when the optional FP16 encoder is also present.</summary>
    public bool HasFp16Encoder => File.Exists(EncoderFp16);
}
