namespace Deckle.Inference.Onnx;

// Identity of the Silero VAD model this module runs, exposed so the hosting module
// can provision the file (download it to the models directory) without the
// inference module reaching up into the transcription or download layers —
// dependencies point one way, toward this module.
//
// The file is the unified v5 silero_vad.onnx (16 kHz / 8 kHz), MIT-licensed,
// ~2.31 MB, pinned to the immutable v5.0 tag so the URL never moves with master.
public static class SileroVadModel
{
    public const string FileName = "silero_vad.onnx";
    public const string Url      = "https://raw.githubusercontent.com/snakers4/silero-vad/v5.0/files/silero_vad.onnx";

    // SHA-256 of the 2 313 101-byte v5.0 file. The tag is immutable, so the hash is
    // stable: the host passes it to the downloader, which discards a transfer whose
    // bytes don't match (a proxy/CDN returning 200 with a truncated or wrong body)
    // instead of publishing a corrupt model.
    public const string Sha256 = "6b99cbfd39246b6706f98ec13c7c50c6b299181f2474fa05cbc8046acc274396";
}
