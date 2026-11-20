namespace Deckle.Transcription.Parakeet;

/// <summary>A contiguous slice of a longer 16 kHz mono buffer, addressed by its sample offsets.</summary>
/// <param name="Offset">Index of the first sample of the slice in the source buffer.</param>
/// <param name="Length">Number of samples in the slice.</param>
internal readonly record struct AudioChunk(int Offset, int Length)
{
    /// <summary>Index one past the last sample of the slice.</summary>
    public int End => Offset + Length;
}
