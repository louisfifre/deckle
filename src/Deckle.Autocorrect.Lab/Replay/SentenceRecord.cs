namespace Deckle.Autocorrect.Lab;

// One typed sentence of the typed-text corpus the Lab reads back off disk (the
// autocorrect.text.jsonl files collected before the writer was removed): the two
// parallel strings for direct mining, the per-slot ordered history (empty when
// nothing changed), the closure that ended the run ("sentence" / "enter" /
// "interrupted"), and the typing rhythm as comma-joined per-slot inter-commit
// gaps in ms (first slot "0"; empty when no timestamps were available). The
// defaults match how a reader interprets a record without those two fields:
// closed on a sentence boundary, rhythm unknown.
public readonly record struct SentenceRecord(
    string Typed, string Final, string History, string Closure = "sentence", string Timing = "");