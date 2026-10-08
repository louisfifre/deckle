namespace Deckle.Transcription;

// The text of one utterance as the streaming consumer appends it to the take.
//   Index          — the source Utterance's 0-based emission order.
//   Text           — the trimmed, non-empty transcript of that utterance.
//   EndedOnSilence — true when the speaker made a real pause after it: the
//                    assembled text puts a paragraph break ("\n\n") before the
//                    next utterance, a single space otherwise. This is exactly
//                    the rule ConsumeUtterancesAsync applies.
public sealed record TranscribedUtterance(int Index, string Text, bool EndedOnSilence);
