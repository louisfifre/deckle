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
    public const string FileName  = "silero_vad.onnx";
    public const string Url       = "https://raw.githubusercontent.com/snakers4/silero-vad/v5.0/files/silero_vad.onnx";
    public const long   SizeBytes = 2_313_101L;
}
