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
    /// <summary>A non-word spell-fixed to the single common French word one edit away.</summary>
    TypoCorrection,
    /// <summary>A dropped elision apostrophe restored in a glued proclitic (cest → c'est).</summary>
    Elision,
    /// <summary>Ambiguity resolved by the post-sentence bidirectional reranker.</summary>
    SentenceReranker,
    /// <summary>The first word of a sentence raised to a capital.</summary>
    Capitalization,
    /// <summary>A verb re-conjugated to agree with its adjacent subject pronoun.</summary>
    SubjectVerbAgreement,
    /// <summary>The étape-2 toy hotstring — dev only.</summary>
    ToyHotstring,
}

// A rewrite the engine wants applied to a just-committed word.
public sealed record CorrectionDecision(string Original, string Replacement, CorrectionReason Reason);

// Decides whether a just-committed word should be rewritten.
// leftContext is the preceding words on this surface (already corrected),
// most recent last, empty at the start of an utterance or after a reset.
// Returns null to leave the literal untouched — the conservative default.
//
// trace is an optional decision ledger: when non-null the policy records its exit
// reason, candidate pool and safety gauges into it for the decision telemetry. It
// is observation only — the decision must never read back from it — and null by
// default, so the live chain runs untouched and the tests stay terse.
public interface ICorrectionPolicy
{
    CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext, CorrectionTrace? trace = null);
}
