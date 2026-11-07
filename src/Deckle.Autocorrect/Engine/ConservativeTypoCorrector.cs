namespace Deckle.Autocorrect;

// The physical-error stage: an Android-style spell-fix
// for a just-committed word that is NOT a French word — a real keyboard typo
// ("bonjuor" → "bonjour"). It never touches a valid form. Two tiers: a near tier resolves a word one
// edit away when it is common and clearly dominates any rival; only when nothing
// sits one edit away does the far tier reach two edits, for bigger faults, held to
// a stricter length/frequency/dominance bar. Ambiguity, rarity, an identifier
// shape or a mid-utterance proper noun still leave the literal alone. These bars
// carry the safety on their own: this policy only proposes, and the engine
// enforces learned suppressions.
//
// Candidates come from Norvig's generate-and-test against the French lexicon: a
// deletion (an extra key), a transposition (two keys swapped), a keyboard-adjacent
// substitution (a wrong key — only physically touching keys count as a slip) or an
// insertion (a missing key); the far tier composes two such edits. Membership is
// checked both against exact French forms and, when the accent index is supplied,
// against accented forms behind the repaired fold. This composes one physical
// slip with missing diacritics ("preaprer" → "préparer") without relaxing either
// tier's frequency or dominance bars.
public sealed class ConservativeTypoCorrector : ICorrectionPolicy
{
    private readonly IFrequencyLexicon _french;
    private readonly IFrequencyLexicon? _english;
    private readonly IPersonalLexicon? _personal;
    private readonly TypoOptions _options;
    private readonly AccentIndex? _accentIndex;

    public ConservativeTypoCorrector(
        IFrequencyLexicon french,
        IFrequencyLexicon? english = null,
        IPersonalLexicon? personal = null,
        TypoOptions? options = null,
        AccentIndex? accentIndex = null)
    {
        _french = french;
        _english = english;
        _personal = personal;
        _options = options ?? new TypoOptions();
        _accentIndex = accentIndex;
    }

    public CorrectionDecision? Evaluate(
        string word,
        IReadOnlyList<string> leftContext,
        CorrectionTrace? trace = null)
    {
        StageTrace? st = trace?.Open(CorrectionTrace.StageNames.Typo);

        if (word.Length < _options.MinWordLength)
            return Abstain(st, CorrectionTrace.Reasons.TooShort);

        // Letters only: an apostrophe, hyphen or digit takes the token out of
        // scope — elisions, compounds and identifiers are not plain typos.
        foreach (char c in word)
            if (!char.IsLetter(c))
                return Abstain(st, CorrectionTrace.Reasons.NonWordChar);

        // camelCase/PascalCase identifiers and already-accented forms are never
        // typo-corrected: the user meant the identifier, and an accented form is
        // another stage's concern.
        if (WordShape.HasInternalUpper(word))
            return Abstain(st, CorrectionTrace.Reasons.InternalCaps);
        if (AccentFolding.HasDiacritics(word))
            return Abstain(st, CorrectionTrace.Reasons.AlreadyAccented);

        // A capitalised word mid-utterance is almost always a proper noun (a
        // name, a brand): never spell-fix it. Sentence-initial capitals are
        // exempt — there a capital is the ordinary case.
        if (leftContext.Count > 0 && WordShape.IsTitleCase(word))
            return Abstain(st, CorrectionTrace.Reasons.ProperNounGuard);

        string lower = word.ToLowerInvariant();

        // Protected literals stay out of the typo path.
        if (_english?.Contains(lower) == true)
            return Abstain(st, CorrectionTrace.Reasons.ValidEnglish);
        if (_personal?.IsAdopted(lower) == true)
            return Abstain(st, CorrectionTrace.Reasons.UserAdopted);

        // The defining gate: this stage only ever acts on a NON-word. A valid
        // French form is the diacritics gate's business.
        if (_french.Contains(lower))
            return Abstain(st, CorrectionTrace.Reasons.ValidFrench);

        var near = ValidNeighbours(lower, twoEdits: false);
        if (near.Count > 0)
        {
            // A direct accent restoration costs no physical edit. A keyboard
            // neighbour may outrank it only with the same strong frequency
            // dominance required between typo rivals (bine -> bien, while
            // modele stays reserved for modèle rather than modèles).
            if (_accentIndex is not null
                && _accentIndex.VariantsOf(lower) is { Count: > 0 } accents
                && near[0].Frequency < accents[0].FrequencyPerMillion * _options.DominanceRatio)
                return Abstain(st, CorrectionTrace.Reasons.AccentFoldPreferred);

            return Decide(word, near, _options.MinFrequencyPerMillion, _options.DominanceRatio,
                st, CorrectionTrace.Reasons.TypoNear);
        }

        if (_options.MaxEditDistance >= 2
            && lower.Length >= _options.Edits2MinWordLength
            && lower.Length <= Edits2MaxWordLength)
        {
            var far = ValidNeighbours(lower, twoEdits: true);
            if (far.Count > 0)
                return Decide(
                    word, far, _options.Edits2MinFrequencyPerMillion, _options.Edits2DominanceRatio,
                    st, CorrectionTrace.Reasons.TypoFar);
        }

        return Abstain(st, CorrectionTrace.Reasons.NoNeighbour);
    }

    private static CorrectionDecision? Abstain(StageTrace? st, string reason)
    {
        st?.Abstain(reason);
        return null;
    }
    // The word length past which the far tier is skipped: its candidate space
    // grows with the square of the length, and a very long token is rarely a
    // two-slip typo of a common word anyway. Bounds the per-word cost.
    private const int Edits2MaxWordLength = 14;

    private readonly record struct Candidate(string Form, double Frequency);

    private CorrectionDecision? Decide(
        string word, List<Candidate> candidates, double minFrequency, double dominanceRatio,
        StageTrace? st, string fireReason)
    {
        Candidate top = candidates[0];
        TraceEvidence(candidates, minFrequency, dominanceRatio, st);

        // Common enough to be the obvious intent — a rare neighbour is no evidence.
        if (top.Frequency < minFrequency)
            return Abstain(st, CorrectionTrace.Reasons.TooRare);

        // With rivals, the best must dominate; a close second means real ambiguity.
        if (candidates.Count >= 2 && top.Frequency < candidates[1].Frequency * dominanceRatio)
            return Abstain(st, CorrectionTrace.Reasons.NotDominant);

        st?.Fire(fireReason);
        return new CorrectionDecision(
            word, CasePattern.Apply(word, top.Form), CorrectionReason.TypoCorrection);
    }

    private static void TraceEvidence(
        List<Candidate> candidates,
        double minFrequency,
        double dominanceRatio,
        StageTrace? st)
    {
        if (st is null)
            return;

        foreach (Candidate candidate in candidates)
            st.AddCandidate(candidate.Form, candidate.Frequency, CorrectionTrace.Sources.Index);

        double ratio = candidates.Count >= 2 && candidates[1].Frequency > 0.0
            ? candidates[0].Frequency / candidates[1].Frequency
            : double.PositiveInfinity;
        st.Gauge("top_freq", candidates[0].Frequency)
          .Gauge("top_freq_min", minFrequency)
          .Gauge("dominance", ratio)
          .Gauge("dominance_min", dominanceRatio);
    }

    // The far tier composes two single edits, so its cost is the square of the
    // near tier's: bounded by Edits2MaxWordLength and run only when the near tier
    // came up empty.
    private List<Candidate> ValidNeighbours(string w, bool twoEdits)
    {
        var generated = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<Candidate>();

        void Add(string form, double frequency)
        {
            if (form.Length == 0 || form == w || !seen.Add(form)) return;
            found.Add(new Candidate(form, frequency));
        }

        void Consider(string candidate)
        {
            if (candidate.Length == 0 || candidate == w || !generated.Add(candidate)) return;
            if (_french.Contains(candidate))
                Add(candidate, _french.FrequencyOf(candidate));

            if (_accentIndex is null) return;
            foreach (AccentVariant variant in _accentIndex.VariantsOf(candidate))
                Add(variant.Form, variant.FrequencyPerMillion);
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
