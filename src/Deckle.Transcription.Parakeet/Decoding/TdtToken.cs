namespace Deckle.Transcription.Parakeet.Decoding;

/// <summary>A token the decoder emitted.</summary>
/// <param name="Id">Vocabulary index.</param>
/// <param name="Frame">Encoder frame at which it was emitted.</param>
/// <param name="Probability">Softmax probability of the token at that step, in (0, 1]; the confidence.</param>
internal readonly record struct TdtToken(int Id, int Frame, float Probability);
