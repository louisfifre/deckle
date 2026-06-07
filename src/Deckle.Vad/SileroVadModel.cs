namespace Deckle.Vad;

// Identity of the Silero VAD model VadService provisions and loads: file name,
// download URL, and the SHA-256 both the download and the on-disk copy are checked
// against. The URL is pinned to an immutable release tag so it never moves with
// upstream master.
public static class SileroVadModel
{
    public const string FileName = "silero_vad.onnx";
    public const string Url      = "https://raw.githubusercontent.com/snakers4/silero-vad/v6.2/src/silero_vad/data/silero_vad.onnx";
    public const string Sha256   = "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3";
}
