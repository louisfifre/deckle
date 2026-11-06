namespace Deckle.Transcription;

// What changed in the take's text since the previous snapshot.
//   Appended — an utterance joined the end of the text; the rest is as before.
//   Revised  — text already shown was replaced: a rewrite batch came back, or
//              the whole-take rewrite replaced the raw text at finalize.
public enum TranscriptChangeKind
{
    Appended,
    Revised,
}

// The text of the current take as it would be delivered at that moment: the
// raw utterances joined by the take's own rule, with every rewrite that has
// already returned in place of the raw text it covers. One source for every
// reader of the live text, so what a surface shows is what the clipboard
// gets; a reader never assembles utterances itself.
public sealed record TranscriptSnapshot(string Text, TranscriptChangeKind Kind);
