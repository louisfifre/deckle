namespace Deckle.Autocorrect;

// ── ConservativeTypoCorrector ────────────────────────────────────────────────
//
// Stage two of the engine, after the diacritics gate: an Android-style spell-fix
// for a just-committed word that is NOT a French word — a real keyboard typo
// ("bonjuor" → "bonjour"). It never touches a valid form (that is the diacritics
// gate's domain, and it runs first). Two tiers: a near tier resolves a word one
// edit away when it is common and clearly dominates any rival; only when nothing
// sits one edit away does the far tier reach two edits, for bigger faults, held to
// a stricter length/frequency/dominance bar. Ambiguity, rarity, an identifier
// shape or a mid-utterance proper noun still leave the literal alone — and every
// correction is one Backspace away from reverting, which suppresses the pair for
// good (the engine owns that gesture, so this policy only proposes). That revert
// is what lets the far tier be aggressive without being reckless.
//
// Candidates come from Norvig's generate-and-test against the French lexicon: a
// deletion (an extra key), a transposition (two keys swapped), a keyboard-adjacent
// substitution (a wrong key — only physically touching keys count as a slip) or an
// insertion (a missing key); the far tier composes two such edits. Membership is
// the exact lowercased lexicon, whose forms carry their accents; restoring an
// accent is the diacritics gate's job, so a typo of an *accented* word is
// deliberately out of scope here rather than miscorrected.
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

        // Near tier: a single edit away — the high-confidence case.
        var near = ValidNeighbours(lower, twoEdits: false);
        if (near.Count > 0)
            return Decide(word, near, _options.MinFrequencyPerMillion, _options.DominanceRatio);

        // Far tier: two edits away, for a bigger fault — only when nothing sits
        // one edit away, on a long-enough word, and held to a stricter bar.
        if (_options.MaxEditDistance >= 2
            && lower.Length >= _options.Edits2MinWordLength
            && lower.Length <= Edits2MaxWordLength)
        {
            var far = ValidNeighbours(lower, twoEdits: true);
            if (far.Count > 0)
                return Decide(
                    word, far, _options.Edits2MinFrequencyPerMillion, _options.Edits2DominanceRatio);
        }

        return null;
    }

    // The word length past which the far tier is skipped: its candidate space
    // grows with the square of the length, and a very long token is rarely a
    // two-slip typo of a common word anyway. Bounds the per-word cost.
    private const int Edits2MaxWordLength = 14;

    private readonly record struct Candidate(string Form, double Frequency);

    // Applies a tier's frequency floor and dominance ratio to the ranked
    // neighbours, returning the winning correction or null when the evidence is
    // too weak (a rare best, or a close rival).
    private CorrectionDecision? Decide(
        string word, List<Candidate> candidates, double minFrequency, double dominanceRatio)
    {
        Candidate top = candidates[0];

        // Common enough to be the obvious intent — a rare neighbour is no evidence.
        if (top.Frequency < minFrequency)
            return null;

        // With rivals, the best must dominate; a close second means real ambiguity.
        if (candidates.Count >= 2 && top.Frequency < candidates[1].Frequency * dominanceRatio)
            return null;

        return new CorrectionDecision(
            word, CasePattern.Apply(word, top.Form), CorrectionReason.TypoCorrection);
    }

    // Valid French words one edit away (twoEdits=false) or exactly two edits away
    // (twoEdits=true, composed from two single edits), deduped and ranked
    // frequency-desc. Each branch is a hashset membership test; the near tier is a
    // few hundred lookups, the far tier its square — bounded by Edits2MaxWordLength
    // and only ever run when the near tier came up empty.
    private List<Candidate> ValidNeighbours(string w, bool twoEdits)
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

        if (!twoEdits)
        {
            foreach (string e in Edits1(w))
                Consider(e);
        }
        else
        {
            foreach (string e1 in Edits1(w))
                foreach (string e2 in Edits1(e1))
                    Consider(e2);
        }

        found.Sort(static (x, y) => y.Frequency.CompareTo(x.Frequency));
        return found;
    }

    // The single-edit neighbourhood of a token: a deletion, an adjacent
    // transposition, a keyboard-adjacent substitution, or an a-z insertion.
    // Yields raw strings — membership against the lexicon is the caller's test.
    private static IEnumerable<string> Edits1(string w)
    {
        int n = w.Length;

        // Deletion: one key too many.
        for (int i = 0; i < n; i++)
            yield return w.Remove(i, 1);

        // Transposition: two adjacent keys in the wrong order.
        for (int i = 0; i < n - 1; i++)
        {
            char[] a = w.ToCharArray();
            (a[i], a[i + 1]) = (a[i + 1], a[i]);
            yield return new string(a);
        }

        // Substitution: a wrong key — only a physically touching key is a slip.
        for (int i = 0; i < n; i++)
        {
            foreach (char c in QwertyAdjacency.Neighbours(w[i]))
            {
                char[] a = w.ToCharArray();
                a[i] = c;
                yield return new string(a);
            }
        }

        // Insertion: a missing key — any letter, at any gap.
        for (int i = 0; i <= n; i++)
            for (char c = 'a'; c <= 'z'; c++)
                yield return w.Insert(i, c.ToString());
    }
}
