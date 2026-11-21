using Deckle.Inference.Onnx;

namespace Deckle.Transcription.Parakeet;

// The two audio-side networks run back to back: nemo128 (the mel front end Deckle ships, on the CPU) turns the waveform into 128-band features, and the encoder
// (DirectML or CPU) turns those into one 1024-wide frame per 80 ms. Input and output
// names and types are those verified on the exported files: nemo128 takes
// waveforms f32 [B,N] and waveforms_lens i64 [B] and returns features_lens = N / 160, one fewer than
// its columns (the last is zeroed), which the encoder is given as it is; the encoder takes audio_signal f32
// [B,128,F] and length i64 [B], and its output is f32 [B,1024,T] with encoded_lengths
// i64 [B], in both precisions (the FP16 encoder keeps FP32 inputs and outputs).

/// <summary>Mel front end plus encoder, on ONNX sessions.</summary>
internal sealed class OnnxParakeetEncoder : IParakeetEncoder
{
    private static readonly string[] PreprocessorOutputs = ["features", "features_lens"];
    private static readonly string[] EncoderOutputs = ["outputs", "encoded_lengths"];

    private readonly OnnxModelSession _preprocessor;
    private readonly OnnxModelSession _encoder;

    public OnnxParakeetEncoder(OnnxModelSession preprocessor, OnnxModelSession encoder)
    {
        _preprocessor = preprocessor;
        _encoder = encoder;
    }

    public IReadOnlyList<ReadOnlyMemory<float>> Encode(ReadOnlySpan<float> samples)
    {
        int sampleCount = samples.Length;
        IReadOnlyList<OnnxTensorOutput> mel = _preprocessor.RunNamed(
        [
            OnnxTensorInput.Float("waveforms", samples.ToArray(), [1, sampleCount]),
            OnnxTensorInput.Int64("waveforms_lens", [sampleCount], [1]),
        ], PreprocessorOutputs);
        OnnxTensorOutput features = mel[0];
        OnnxTensorOutput featureLengths = mel[1];

        IReadOnlyList<OnnxTensorOutput> encoded = _encoder.RunNamed(
        [
            OnnxTensorInput.Float("audio_signal", features.Floats!, features.Shape),
            OnnxTensorInput.Int64("length", featureLengths.Longs!, [1]),
        ], EncoderOutputs);
        OnnxTensorOutput outputs = encoded[0];
        long encodedLength = encoded[1].Longs![0];

        // [1, width, T] in row-major order: element (d, t) sits at d * T + t, so a time step is a
        // strided column. Each step is copied into its own array, which is the layout the decoder reads.
        int width = outputs.Shape[1];
        int steps = outputs.Shape[2];
        int usable = (int)Math.Min(encodedLength, steps);
        float[] data = outputs.Floats!;

        var frames = new ReadOnlyMemory<float>[usable];
        for (int t = 0; t < usable; t++)
        {
            var frame = new float[width];
            for (int d = 0; d < width; d++) frame[d] = data[d * steps + t];
            frames[t] = frame;
        }
        return frames;
    }
}
