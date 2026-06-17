namespace Deckle.Autocorrect;

public enum CorrectionReason
{
    /// <summary>Single accented variant behind the folded form — deterministic.</summary>
    LexicalGate,
    /// <summary>Ambiguity resolved by the left-context pair model, above margin.</summary>
    ContextPair,
    /// <summary>Ambiguity resolved by overwhelming frequency dominance.</summary>
    FrequencyDominance,
    /// <summary>Candidate supplied by the personal dictionary.</summary>
    PersonalWord,
    /// <summary>Ambiguity resolved by the post-sentence bidirectional reranker.</summary>
    SentenceReranker,
    /// <summary>The étape-2 toy hotstring — dev only.</summary>
    ToyHotstring,
}

// A rewrite the engine wants applied to a just-committed word.
public sealed record CorrectionDecision(string Original, string Replacement, CorrectionReason Reason);

// Decides whether a just-committed word should be rewritten.
// leftContext is the preceding words on this surface (already corrected),
// most recent last, empty at the start of an utterance or after a reset.
// Returns null to leave the literal untouched — the conservative default.
public interface ICorrectionPolicy
{
    CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext);
}
