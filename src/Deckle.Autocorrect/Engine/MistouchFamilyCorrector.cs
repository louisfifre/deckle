namespace Deckle.Autocorrect;

// The deterministic detector-generator over the approved mistouch families
// (CONTEXT.md § Mistouch family): interprets family RECORDS (per-user data)
// through family KINDS (universal keyboard mechanics), at the commit stage,
// for the single-reading case only — what it cannot decide deterministically
// it leaves alone, per the engine doctrine.
//
// Boundary families act on the span between the previous word and the one
// just committed — territory no word-level ICorrectionPolicy sees, so this
// runs beside the policy chain in the engine, reading the tracker's separator
// run off the commit. Both current kinds meet the commit-stage conditions by
// construction: the trigger is impossible as it stands (an elision prefix
// glued by ';', two words glued by a comma), the repair is unique, and the
// left word is all the context needed. A reopened word or an unknown
// separator run abstains — conservative, like everything at this stage.
public sealed class MistouchFamilyCorrector
{
    // The span rewrite a family wants, in pieces — the words stand, only the
    // separator run between them changes; consumers that need the full span
    // (the injection) read Original/Replacement, consumers that need the parts
    // (the corpus separator edit) read them directly. Signature identifies the
    // family in telemetry and in the suppression an undo writes.
    public sealed record SpanRepair(
        string Previous, string OldSeparators, string NewSeparators, string Word, string Signature)
    {
        public string Original => Previous + OldSeparators + Word;
        public string Replacement => Previous + NewSeparators + Word;
    }

    private readonly IReadOnlyList<MistouchFamilyRecord> _families;
    private readonly Func<string, bool> _isWord;

    // isWord answers over every protected tier the engine sees (French,
    // global-English seed, personal vocabulary), on a lowercase form.
    public MistouchFamilyCorrector(
        IReadOnlyList<MistouchFamilyRecord> families, Func<string, bool> isWord)
    {
        _families = families;
        _isWord = isWord;
    }

    public SpanRepair? Evaluate(WordCommit commit)
    {
        if (commit.Reopened) return null; // the deliberate keystroke asserts intent
        string? previous = commit.PreviousWord;
        string separators = commit.PrecedingSeparators;
        if (previous is null || previous.Length == 0 || separators.Length == 0)
            return null;

        foreach (MistouchFamilyRecord family in _families)
        {
            SpanRepair? repair = family.Kind switch
            {
                MistouchFamilyKinds.BoundaryApostrophe =>
                    EvaluateApostrophe(family, previous, separators, commit.Word),
                MistouchFamilyKinds.BoundaryMissingSpace =>
                    EvaluateMissingSpace(family, previous, separators, commit.Word),
                _ => null, // a record whose kind this build does not know is inert
            };
            if (repair is not null)
                return repair;
        }
        return null;
    }

    // « qu;il » — the previous token is a bare elision prefix, the run is the
    // lone ';' (the key beside the apostrophe on QWERTY-US), and the committed
    // word is a known form: one reading, the apostrophe.
    private SpanRepair? EvaluateApostrophe(
        MistouchFamilyRecord family, string previous, string separators, string word)
    {
        if (separators != ";") return null;
        if (!WordBoundaries.IsElisionPrefix(previous)) return null;
        if (!_isWord(word.ToLowerInvariant())) return null;
        return new SpanRepair(previous, ";", "'", word, family.Signature);
    }

    // « mot,mot » — the run is the family's bare punctuation (no space landed)
    // and BOTH sides are known words: the glue is impossible as prose, the
    // space is the one reading. A non-word on either side abstains — an
    // identifier (« app.jsonl ») must never be split.
    private SpanRepair? EvaluateMissingSpace(
        MistouchFamilyRecord family, string previous, string separators, string word)
    {
        if (family.Punctuation.Length == 0 || separators != family.Punctuation) return null;
        if (!_isWord(previous.ToLowerInvariant()) || !_isWord(word.ToLowerInvariant())) return null;
        return new SpanRepair(previous, separators, separators + " ", word, family.Signature);
    }
}
