namespace Deckle.Transcription.Parakeet.Segments;

/// <summary>One token the transducer decoder emitted.</summary>
/// <param name="Id">Vocabulary id; kept for logs, the builder does not read it.</param>
/// <param name="Text">
/// Piece text as the vocabulary spells it. A leading "▁" (SentencePiece word marker) or a
/// leading space starts a new word; the builder turns both into a plain space.
/// </param>
/// <param name="Frame">Encoder frame at which the decoder emitted the token.</param>
/// <param name="Probability">Softmax probability of the chosen token, in [0, 1].</param>
internal readonly record struct DecodedToken(int Id, string Text, int Frame, float Probability);
