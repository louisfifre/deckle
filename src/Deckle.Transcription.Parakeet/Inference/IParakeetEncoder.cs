namespace Deckle.Transcription.Parakeet;

// The audio-to-frames half of the model, seen from the backend. The backend is written
// against this seam so its orchestration (chunking, cancellation, segments, logging) can be
// tested with a scripted fake, and so the ONNX sessions stay out of it.

/// <summary>Turns a buffer of 16 kHz mono samples into encoder frames.</summary>
internal interface IParakeetEncoder
{
    /// <summary>Runs the mel front end and the encoder on one chunk of audio.</summary>
    /// <returns>
    /// One entry per encoder time step (80 ms each), each holding the encoder width of floats,
    /// already trimmed to the length the encoder reports: the decoder reads them as they are.
    /// </returns>
    IReadOnlyList<ReadOnlyMemory<float>> Encode(ReadOnlySpan<float> samples);
}
