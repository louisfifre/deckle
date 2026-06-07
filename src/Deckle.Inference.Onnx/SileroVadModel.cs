namespace Deckle.Inference.Onnx;

// Identity of the Silero VAD model this module runs, exposed so the hosting module
// can provision the file (download it to the models directory) without the
// inference module reaching up into the transcription or download layers —
// dependencies point one way, toward this module.
//
// The file is the v6.2 silero_vad.onnx (the float32 model; 16 kHz / 8 kHz),
// MIT-licensed, ~2.33 MB. From v6 the ONNX ships inside the Python package, so the
// URL points at src/silero_vad/data/ under the immutable v6.2 tag rather than the
// old /files/ path — the tag is pinned so the URL never moves with master.
public static class SileroVadModel
{
    public const string FileName = "silero_vad.onnx";
    public const string Url      = "https://raw.githubusercontent.com/snakers4/silero-vad/v6.2/src/silero_vad/data/silero_vad.onnx";

    // SHA-256 of the 2 327 524-byte v6.2 file. The tag is immutable, so the hash is
    // stable: the host passes it to the downloader, which discards a transfer whose
    // bytes don't match (a proxy/CDN returning 200 with a truncated or wrong body)
    // instead of publishing a corrupt model.
    public const string Sha256 = "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3";
}
