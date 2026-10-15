namespace Deckle.Llm.Rewrite;

// One closed run of utterances that leaves for rewriting on its own, while the
// dictation keeps recording. Index is 0-based and follows the speech order, so
// the assembled text is the batches in Index order.
//
// EndsOnPause tells the assembly how to join this batch to the next one: a
// paragraph break when the speaker paused at the seam, a space otherwise. A
// batch that carried an unfinished sentence into the next one ends mid-
// paragraph, so it does not end on a pause (DictationBatcher).
public sealed record DictationBatch(int Index, string Text, bool EndsOnPause);
