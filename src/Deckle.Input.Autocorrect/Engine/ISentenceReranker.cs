using Deckle.Input.Autocorrect.Lexicon;

namespace Deckle.Input.Autocorrect.Engine;

// A post-sentence, bidirectional disambiguator: unlike IPairDisambiguator (which
// sees only the left context, word by word), this one runs once the sentence is
// complete and weighs the FULL context — words on both sides of the slot. It is
// still a CORRECTION, not a rewrite: it only ranks the closed set of surface
// forms the lexicon already proposed, and never invents one.
public interface ISentenceReranker
{
    // Given the sentence as ordered output word-forms and the index of one
    // ambiguous slot within it, return the chosen surface form — only when the
    // model clears its confidence margin — or null to leave the slot as is
    // (the conservative default). candidates is the closed set for that slot.
    string? Rerank(IReadOnlyList<string> sentence, int slotIndex, IReadOnlyList<AccentVariant> candidates);
}
