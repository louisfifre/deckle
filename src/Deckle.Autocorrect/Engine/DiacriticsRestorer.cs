using Deckle.Autocorrect;

namespace Deckle.Autocorrect;

// ── DiacriticsRestorer ──────────────────────────────────────────────────────
//
// The lexical gate, stage one of the engine: given a just-committed word, it
// restores the French diacritics a QWERTY-US typist omitted ("francais" →
// "français"). It is a skip chain — a long sequence of guards that each return
// null to leave the literal untouched, because the literal always wins by
// default. A correction is the exception, earned only when the evidence is
// unambiguous.
//
// The order of the guards is doctrine, not convenience: cheap blacklist checks
// first, then the literal-protection gates (a valid French or English form is
// never second-guessed), then the candidate machinery. Each guard carries the
// one reason it exists.
public sealed class DiacriticsRestorer : ICorrectionPolicy, IAmbiguityProbe
{
    private readonly IFrequencyLexicon _french;
    private readonly IFrequencyLexicon? _english;
    private readonly AccentIndex _index;
    private readonly RestorerOptions _options;
    private readonly IPairDisambiguator? _context;
    private readonly IPersonalLexicon? _personal;
    private readonly Func<string, IReadOnlyList<AccentVariant>>? _personalVariants;

    public DiacriticsRestorer(
        IFrequencyLexicon french,
        IFrequencyLexicon? english,
        AccentIndex index,
        RestorerOptions? options = null,
        IPairDisambiguator? context = null,
        IPersonalLexicon? personal = null,
        Func<string, IReadOnlyList<AccentVariant>>? personalVariants = null)
    {
        _french = french;
        _english = english;
        _index = index;
        _options = options ?? new RestorerOptions();
        _context = context;
        _personal = personal;
        _personalVariants = personalVariants;
    }

    public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext, CorrectionTrace? trace = null)
    {
        StageTrace? st = trace?.Open(CorrectionTrace.StageNames.Diacritics);

        // 1. Too short to carry signal — and the single-char class is blacklisted.
        if (word.Length < _options.MinWordLength)
            return Abstain(st, CorrectionTrace.Reasons.TooShort);

        foreach (char c in word)
        {
            // 2. A digit anywhere makes it a token class we never touch (win11).
            if (char.IsDigit(c))
                return Abstain(st, CorrectionTrace.Reasons.HasDigit);

            // 3. Only letters, apostrophes and hyphens are word material; anything
            //    else (punctuation, symbols) is out of scope for restoration.
            if (!char.IsLetter(c) && c is not '\'' and not '’' and not '-')
                return Abstain(st, CorrectionTrace.Reasons.NonWordChar);
        }

        // 4. Internal uppercase on a not-all-uppercase word is an identifier
        //    (camelCase, fooBar) — never a dictated French word.
        if (WordShape.HasInternalUpper(word))
            return Abstain(st, CorrectionTrace.Reasons.InternalCaps);

        // 5. A trailing apostrophe is an elision token ("l'") — the prefix, not a word.
        if (word[^1] is '\'' or '’')
            return Abstain(st, CorrectionTrace.Reasons.TrailingApostrophe);

        // 6. The user typed accents deliberately — never second-guess an
        //    already-accented word.
        if (AccentFolding.HasDiacritics(word))
            return Abstain(st, CorrectionTrace.Reasons.AlreadyAccented);

        // 6b. Proper-noun guard (opt-in): a title-cased word mid-utterance is a
        //     name (Git, Azure), not a dictated French word. Sentence-initial
        //     capitals are exempt — there a capital is the ordinary case.
        if (_options.GuardCapitalizedMidSentence && leftContext.Count > 0 && WordShape.IsTitleCase(word))
            return Abstain(st, CorrectionTrace.Reasons.ProperNounGuard);

        string lower = word.ToLowerInvariant();

        // 7. A valid French form is never touched — the literal always wins.
        //    In the eval-only context mode the literal instead joins the
        //    candidates as first-rank, and only the pair model may overturn it.
        bool literalValid = _french.Contains(lower);
        // Calibrated valid-form exception: "voila" is a rare passé simple verb,
        // but in fast prose it overwhelmingly means the discourse particle
        // "voilà". Let the ordinary dominance gate arbitrate this one fold.
        bool allowDominantValidLiteral = lower == "voila";
        if (literalValid && !allowDominantValidLiteral
            && !(_options.CorrectValidFormsWithContext && _context is not null))
            return Abstain(st, CorrectionTrace.Reasons.ValidFrench);


        // 8. Global-English guard: the loaded artifact is already the restricted
        //    seed, so membership is the protection contract. Nothing is corrected
        //    toward English; a seed literal is only left untouched.
        if (_english?.Contains(lower) == true)
            return Abstain(st, CorrectionTrace.Reasons.ValidEnglish);

        // 9. The user's own adopted words shield themselves from correction.
        if (_personal?.IsAdopted(lower) == true)
            return Abstain(st, CorrectionTrace.Reasons.UserAdopted);

        // 10. Gather candidates from the index, merge the personal dictionary's
        //     own variants (personal wins ties), filter by frequency floor and
        //     drop any pair the user has suppressed. A valid literal competes
        //     as a candidate of its own.
        var candidates = BuildCandidates(lower, literalValid, applyRarityGate: false, out var fromPersonal);
        st?.WithCandidates(candidates, v =>
            fromPersonal.Contains(v.Form) ? CorrectionTrace.Sources.Personal
            : v.Form == lower             ? CorrectionTrace.Sources.Literal
            :                               CorrectionTrace.Sources.Index);

        // 11. Nothing to propose — leave the literal.
        if (candidates.Count == 0)
            return Abstain(st, CorrectionTrace.Reasons.NoCandidates);

        // 12. A single candidate is deterministic — the lexical gate fires.
        //     (With a valid literal the single candidate is the literal itself:
        //     nothing to do.)
        if (candidates.Count == 1)
        {
            string form = candidates[0].Form;
            if (form == lower)
                return Abstain(st, CorrectionTrace.Reasons.LiteralSingleton);
            bool personal = fromPersonal.Contains(form);
            st?.Fire(personal ? CorrectionTrace.Reasons.PersonalWord : CorrectionTrace.Reasons.LexicalGate);
            return new CorrectionDecision(word, CasePattern.Apply(word, form),
                personal ? CorrectionReason.PersonalWord : CorrectionReason.LexicalGate);
        }

        // 13. Multiple candidates: let the left-context pair model decide. It
        //     returns null unless one variant clears its margin; choosing the
        //     bare literal means « keep it ». The disambiguator records its own
        //     per-candidate scores and margin gauges into this stage's trace.
        if (_context is not null)
        {
            string? chosen = _context.Choose(LowercaseContext(leftContext), candidates, st);
            if (chosen is not null && chosen != lower)
            {
                bool personal = fromPersonal.Contains(chosen);
                st?.Fire(personal ? CorrectionTrace.Reasons.PersonalWord : CorrectionTrace.Reasons.ContextPair);
                return new CorrectionDecision(word, CasePattern.Apply(word, chosen),
                    personal ? CorrectionReason.PersonalWord : CorrectionReason.ContextPair);
            }
            if (chosen is not null)
                return Abstain(st, CorrectionTrace.Reasons.ContextKeptLiteral);
        }

        // Only the pair model may overturn a valid form — dominance never does.
        if (literalValid && !allowDominantValidLiteral)
            return Abstain(st, CorrectionTrace.Reasons.ContextKeptLiteral);

        // 14. No context verdict — fall back to frequency dominance. Correct only
        //     when the top variant overwhelms the runner-up AND is common enough;
        //     never a bare argmax.
        AccentVariant top = candidates[0];
        AccentVariant second = candidates[1];
        double ratio = second.FrequencyPerMillion > 0.0
            ? top.FrequencyPerMillion / second.FrequencyPerMillion
            : double.PositiveInfinity;
        st?.Gauge("dominance", ratio)
          .Gauge("dominance_min", _options.DominanceRatio)
          .Gauge("top_freq", top.FrequencyPerMillion)
          .Gauge("top_freq_min", _options.MinDominantFrequencyPerMillion);
        bool dominant =
            second.FrequencyPerMillion > 0.0
            && ratio >= _options.DominanceRatio
            && top.FrequencyPerMillion >= _options.MinDominantFrequencyPerMillion;
        if (dominant)
        {
            st?.Fire(CorrectionTrace.Reasons.FrequencyDominance);
            return new CorrectionDecision(
                word,
                CasePattern.Apply(word, top.Form),
                CorrectionReason.FrequencyDominance);
        }

        // 15. Ambiguity without evidence — the literal stays.
        return Abstain(st, CorrectionTrace.Reasons.NotDominant);
    }

    // Records the abstain reason onto the stage trace (when present) and leaves
    // the literal untouched — the single conservative return.
    private static CorrectionDecision? Abstain(StageTrace? st, string reason)
    {
        st?.Abstain(reason);
        return null;
    }

    // IAmbiguityProbe: the closed candidate set when the fold carries >=2 forms,
    // for a post-sentence reranker to resolve. Mirrors the candidate machinery of
    // Evaluate (index variants + valid literal + personal, floor- and
    // suppression-filtered) but without the guard chain or any decision — it only
    // answers "is this an ambiguous slot, and which forms?". The blacklist guards
    // (digits, internal upper, elision, already-accented) still matter: an
    // already-accented or non-word token is never an ambiguous slot.
    public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
        SentenceCandidates(word, includeTypedLiteral: false);

    public IReadOnlyList<AccentVariant> SentenceCandidates(string word, bool includeTypedLiteral)
    {
        if (word.Length == 0)
            return Array.Empty<AccentVariant>();
        foreach (char c in word)
            if (char.IsDigit(c) || (!char.IsLetter(c) && c is not '\'' and not '\u2019' and not '-'))
                return Array.Empty<AccentVariant>();
        if (WordShape.HasInternalUpper(word) || word[^1] is '\'' or '\u2019' || AccentFolding.HasDiacritics(word))
            return Array.Empty<AccentVariant>();

        string lower = word.ToLowerInvariant();

        // The gate blacklists sub-MinWordLength tokens outright. The reranker may
        // still resolve a one-char ambiguity (a/a-accent) when the bare form is a
        // real candidate, but it must never force an accent onto a stray letter.
        bool literalValid = _french.Contains(lower);
        if (word.Length < _options.MinWordLength && !literalValid && !includeTypedLiteral)
            return Array.Empty<AccentVariant>();

        var merged = BuildCandidates(lower, literalValid, applyRarityGate: true, out _);
        if (includeTypedLiteral && !merged.Exists(v => string.Equals(v.Form, lower, StringComparison.Ordinal)))
        {
            double frequency = literalValid ? _french.FrequencyOf(lower) : 0.0;
            merged.Add(new AccentVariant(lower, frequency));
            merged.Sort(static (a, b) => b.FrequencyPerMillion.CompareTo(a.FrequencyPerMillion));
        }

        return merged.Count >= 2 ? merged : Array.Empty<AccentVariant>();
    }

    // Builds the filtered candidate list and records which surface forms came
    // from the personal dictionary. Dedup is by exact Form — distinct accented
    // variants that fold to the same key (élève vs élevé) must both survive.
    // A merged personal variant re-sorts the list to keep frequency-desc order.
    private List<AccentVariant> BuildCandidates(
        string lower, bool literalValid, bool applyRarityGate, out HashSet<string> fromPersonal)
    {
        fromPersonal = new HashSet<string>(StringComparer.Ordinal);

        var merged = new List<AccentVariant>(_index.VariantsOf(lower));
        if (literalValid)
            merged.Add(new AccentVariant(lower, _french.FrequencyOf(lower)));

        if (_personalVariants is not null)
        {
            foreach (var pv in _personalVariants(lower))
            {
                int existing = merged.FindIndex(v => v.Form == pv.Form);
                if (existing >= 0)
                    merged[existing] = pv; // personal wins ties on the same surface form.
                else
                    merged.Add(pv);
                fromPersonal.Add(pv.Form);
            }
        }

        // Sentence-stage rarity gate: measured against the typed literal's own
        // frequency, a folded variant far rarer than it is almost always a wrong
        // change (mais→maïs, le→lé). Drop anything under literalFreq × ratio —
        // never the typed literal itself. Off unless the caller is the sentence
        // stage (applyRarityGate) and the literal is a valid form with a
        // meaningful reference frequency; a misspelled literal has none. Zero when
        // off, so the per-candidate test below is a no-op.
        double rarityFloor = applyRarityGate && literalValid
            ? _french.FrequencyOf(lower) * _options.MinCandidateFrequencyRatio
            : 0.0;

        // Filter: frequency floor, the rarity gate, then drop user-suppressed pairs
        // (suppression is case-insensitive on both the original and the candidate form).
        var kept = new List<AccentVariant>(merged.Count);
        foreach (var v in merged)
        {
            if (v.FrequencyPerMillion < _options.MinCandidateFrequencyPerMillion)
                continue;
            if (rarityFloor > 0.0
                && v.FrequencyPerMillion < rarityFloor
                && !string.Equals(v.Form, lower, StringComparison.Ordinal))
                continue;
            if (_personal?.IsSuppressed(lower, v.Form.ToLowerInvariant()) == true)
            {
                fromPersonal.Remove(v.Form);
                continue;
            }
            kept.Add(v);
        }

        kept.Sort(static (a, b) => b.FrequencyPerMillion.CompareTo(a.FrequencyPerMillion));
        return kept;
    }

    // The disambiguator's contract is lowercased context; the live engine hands
    // raw-case words, so fold here. Idempotent on the already-lowercased eval input.
    private static IReadOnlyList<string> LowercaseContext(IReadOnlyList<string> context)
    {
        if (context.Count == 0)
            return context;
        var lower = new string[context.Count];
        for (int i = 0; i < context.Count; i++)
            lower[i] = context[i].ToLowerInvariant();
        return lower;
    }
}
