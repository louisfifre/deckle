namespace Deckle.Autocorrect;

// ── ConservativeTypoCorrector ────────────────────────────────────────────────
//
// Stage two of the engine, after the diacritics gate: an Android-style spell-fix
// for a just-committed word that is NOT a French word — a real keyboard typo
// ("bonjuor" → "bonjour"). It never touches a valid form (that is the diacritics
// gate's domain, and it runs first); it fires only when a single valid French
// word sits one edit away and is common enough to be the obvious intent.
// Conservative by construction: ambiguity, rarity, an identifier shape or a
// mid-utterance proper noun all leave the literal alone. Every correction is one
// Backspace away from reverting, which suppresses the pair for good — the engine
// owns that gesture, so this policy only proposes.
//
// Candidates come from Norvig's edits-1 generate-and-test against the French
// lexicon: a deletion (an extra key), a transposition (two keys swapped), a
// keyboard-adjacent substitution (a wrong key — only physically touching keys
// count as a slip) or an insertion (a missing key). Membership is the exact
// lowercased lexicon, whose forms carry their accents; restoring an accent is
// the diacritics gate's job, so a typo of an *accented* word is deliberately out
// of scope here rather than miscorrected.
public sealed class ConservativeTypoCorrector : ICorrectionPolicy
{
    private readonly FrequencyLexicon _french;
    private readonly FrequencyLexicon? _english;
    private readonly IPersonalLexicon? _personal;
    private readonly TypoOptions _options;

    public ConservativeTypoCorrector(
        FrequencyLexicon french,
        FrequencyLexicon? english = null,
        IPersonalLexicon? personal = null,
        TypoOptions? options = null)
    {
        _french = french;
        _english = english;
        _personal = personal;
        _options = options ?? new TypoOptions();
    }

    public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext)
    {
        if (word.Length < _options.MinWordLength)
            return null;

        // Letters only: an apostrophe, hyphen or digit takes the token out of
        // scope — elisions, compounds and identifiers are not plain typos.
        foreach (char c in word)
            if (!char.IsLetter(c))
                return null;

        // camelCase/PascalCase identifiers and already-accented forms are never
        // typo-corrected: the user meant the identifier, and an accented form is
        // another stage's concern.
        if (WordShape.HasInternalUpper(word) || AccentFolding.HasDiacritics(word))
            return null;

        // A capitalised word mid-utterance is almost always a proper noun (a
        // name, a brand): never spell-fix it. Sentence-initial capitals are
        // exempt — there a capital is the ordinary case.
        if (leftContext.Count > 0 && WordShape.IsTitleCase(word))
            return null;

        string lower = word.ToLowerInvariant();

        // The defining gate: this stage only ever acts on a NON-word. A valid
        // French form is the diacritics gate's business and was already settled.
        if (_french.Contains(lower))
            return null;

        // Never frenchify a word that is itself frequent English.
        if (_english is not null
            && _english.FrequencyOf(lower) >= _options.EnglishGuardMinPerMillion)
            return null;

        // The user's own adopted words shield themselves.
        if (_personal?.IsAdopted(lower) == true)
            return null;

        // The valid French words one edit away, best (most frequent) first.
        var candidates = NearestValidWords(lower);
        if (candidates.Count == 0)
            return null;

        Candidate top = candidates[0];

        // Common enough to be the obvious intent — a rare neighbour is no evidence.
        if (top.Frequency < _options.MinFrequencyPerMillion)
            return null;

        // With rivals, the best must dominate; a close second means real ambiguity.
        if (candidates.Count >= 2
            && top.Frequency < candidates[1].Frequency * _options.DominanceRatio)
            return null;

        return new CorrectionDecision(
            word, CasePattern.Apply(word, top.Form), CorrectionReason.TypoCorrection);
    }

    private readonly record struct Candidate(string Form, double Frequency);

    // Norvig edits-1 restricted to valid French words, deduped, frequency-desc.
    // Every branch is a hashset membership test against the lexicon — a few
    // hundred lookups for a typical word, trivial at typing speed.
    private List<Candidate> NearestValidWords(string w)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<Candidate>();

        void Consider(string candidate)
        {
            if (candidate.Length == 0 || candidate == w) return;
            if (!seen.Add(candidate)) return;
            if (!_french.Contains(candidate)) return;
            found.Add(new Candidate(candidate, _french.FrequencyOf(candidate)));
        }

        int n = w.Length;

        // Deletion: one key too many.
        for (int i = 0; i < n; i++)
            Consider(w.Remove(i, 1));

        // Transposition: two adjacent keys in the wrong order.
        for (int i = 0; i < n - 1; i++)
        {
            char[] a = w.ToCharArray();
            (a[i], a[i + 1]) = (a[i + 1], a[i]);
            Consider(new string(a));
        }

        // Substitution: a wrong key — only a physically touching key is a slip.
        for (int i = 0; i < n; i++)
        {
            foreach (char c in QwertyAdjacency.Neighbours(w[i]))
            {
                char[] a = w.ToCharArray();
                a[i] = c;
                Consider(new string(a));
            }
        }

        // Insertion: a missing key — any letter, at any gap.
        for (int i = 0; i <= n; i++)
            for (char c = 'a'; c <= 'z'; c++)
                Consider(w.Insert(i, c.ToString()));

        found.Sort(static (x, y) => y.Frequency.CompareTo(x.Frequency));
        return found;
    }
}
