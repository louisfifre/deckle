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
// shape or a mid-utterance proper noun still leave the literal alone. A wrong
// correction is taken back through the correction inlay, whose undo writes a
// permanent suppression (the engine enforces it; this policy only proposes) —
// until the inlay ships, the only recourse is manual re-editing, so the bars
// above carry the safety on their own.
//
// Candidates come from Norvig's generate-and-test against the French lexicon: a
// deletion (an extra key), a transposition (two keys swapped), a keyboard-adjacent
// substitution (a wrong key — only physically touching keys count as a slip) or an
// insertion (a missing key); the far tier composes two such edits. Membership is
// checked both against exact French forms and, when the accent index is supplied,
// against accented forms behind the repaired fold. This composes one physical
// slip with missing diacritics ("preaprer" → "préparer") without relaxing either
// tier's frequency or dominance bars.
public sealed class ConservativeTypoCorrector : ICorrectionPolicy, IAmbiguityProbe
{
    private const int SentenceCandidateCap = 4;
    private const int ContextualFarMaxWordLength = 5;
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

    public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext, CorrectionTrace? trace = null)
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

        // The defining gate: this stage only ever acts on a NON-word. A valid
        // French form is the diacritics gate's business and was already settled.
        if (_french.Contains(lower))
            return Abstain(st, CorrectionTrace.Reasons.ValidFrench);

        // Never frenchify a word that belongs to the restricted global-English
        // seed. The seed is curated upstream; membership is the guard.
        if (_english?.Contains(lower) == true)
            return Abstain(st, CorrectionTrace.Reasons.ValidEnglish);

        // The user's own adopted words shield themselves.
        if (_personal?.IsAdopted(lower) == true)
            return Abstain(st, CorrectionTrace.Reasons.UserAdopted);

        // Near tier: a single edit away — the high-confidence case.
        var near = ValidNeighbours(lower, twoEdits: false);
        if (near.Count > 0)
            return Decide(word, near, _options.MinFrequencyPerMillion, _options.DominanceRatio,
                st, CorrectionTrace.Reasons.TypoNear);

        // Far tier: two edits away, for a bigger fault — only when nothing sits
        // one edit away, on a long-enough word, and held to a stricter bar.
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

    // Records the abstain reason onto the stage trace (when present) and leaves
    // the literal untouched.
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

    // Applies a tier's frequency floor and dominance ratio to the ranked
    // neighbours, returning the winning correction or null when the evidence is
    // too weak (a rare best, or a close rival). Records the tier's candidate pool,
    // its safety gauges and the exit reason onto the stage trace when present.
    private CorrectionDecision? Decide(
        string word, List<Candidate> candidates, double minFrequency, double dominanceRatio,
        StageTrace? st, string fireReason)
    {
        Candidate top = candidates[0];
        double ratio = candidates.Count >= 2 && candidates[1].Frequency > 0.0
            ? top.Frequency / candidates[1].Frequency
            : double.PositiveInfinity;

        if (st is not null)
        {
            foreach (Candidate c in candidates)
                st.AddCandidate(c.Form, c.Frequency, CorrectionTrace.Sources.Index);
            st.Gauge("top_freq", top.Frequency)
              .Gauge("top_freq_min", minFrequency)
              .Gauge("dominance", ratio)
              .Gauge("dominance_min", dominanceRatio);
        }

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

    // Valid French words one edit away (twoEdits=false) or exactly two edits away
    // (twoEdits=true, composed from two single edits), deduped and ranked
    // frequency-desc. Each branch is a hashset membership test; the near tier is a
    // few hundred lookups, the far tier its square — bounded by Edits2MaxWordLength
    // and only ever run when the near tier came up empty.
    private List<Candidate> ValidNeighbours(
        string w,
        bool twoEdits,
        bool includeCoherentHorizontalShifts = false)
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
            if (includeCoherentHorizontalShifts)
                foreach (string shifted in QwertyAdjacency.CoherentHorizontalShifts(w))
                    Consider(shifted);
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

    // The commit stage abstains when several plausible neighbours fail its
    // dominance bar. That residue is valuable sentence-stage work, not a dead
    // end: expose a bounded closed set plus the exact typed literal as KEEP.
    // No thresholds are relaxed in the instant path; the full-sentence judge
    // still has to clear its own calibrated margin before anything changes.
    public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
        SentenceCandidates(word, includeTypedLiteral: true);

    public IReadOnlyList<AccentVariant> SentenceCandidates(
        string word,
        bool includeTypedLiteral)
    {
        if (!CanProbeSentence(word, out string lower))
            return Array.Empty<AccentVariant>();

        List<Candidate> neighbours = ValidNeighbours(
            lower, twoEdits: false, includeCoherentHorizontalShifts: true);

        // Short hurried tokens often combine two physical slips ("miru" for
        // "mieux"). They are too ambiguous for the instant far tier, whose
        // minimum length remains six, but a bounded closed set can safely reach
        // them when the full-sentence judge retains KEEP and clears its margin.
        // Cap the extra search at five letters to keep input-thread work bounded.
        if (_options.MaxEditDistance >= 2 && lower.Length <= ContextualFarMaxWordLength)
        {
            var forms = new HashSet<string>(
                neighbours.Select(candidate => candidate.Form),
                StringComparer.Ordinal);
            foreach (Candidate candidate in ValidNeighbours(lower, twoEdits: true))
                if (forms.Add(candidate.Form))
                    neighbours.Add(candidate);
            neighbours.Sort(static (x, y) => y.Frequency.CompareTo(x.Frequency));
        }

        var candidates = new List<AccentVariant>(SentenceCandidateCap + 1);
        foreach (Candidate neighbour in neighbours)
        {
            if (neighbour.Frequency < _options.MinFrequencyPerMillion)
                continue;
            if (_personal?.IsSuppressed(lower, neighbour.Form) == true)
                continue;
            candidates.Add(new AccentVariant(neighbour.Form, neighbour.Frequency));
            if (candidates.Count == SentenceCandidateCap)
                break;
        }

        // A single generated repair plus KEEP is already a valid closed choice.
        // includeTypedLiteral is advisory for policy-revision calls; a typo slot
        // always requires KEEP because its typed non-word is still user intent.
        if (candidates.Count == 0)
            return Array.Empty<AccentVariant>();
        candidates.Add(new AccentVariant(lower, 0.0));
        return candidates;
    }

    private bool CanProbeSentence(string word, out string lower)
    {
        lower = string.Empty;
        if (word.Length < _options.MinWordLength
            || WordShape.HasInternalUpper(word)
            || WordShape.IsTitleCase(word)
            || AccentFolding.HasDiacritics(word))
            return false;
        foreach (char c in word)
            if (!char.IsLetter(c))
                return false;

        lower = word.ToLowerInvariant();
        return !_french.Contains(lower)
            && _english?.Contains(lower) != true
            && _personal?.IsAdopted(lower) != true;
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
