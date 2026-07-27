namespace Deckle.Autocorrect;

// ── GrammarCorrector ──────────────────────────────────────────────────────────
//
// The grammar stage, after the diacritics gate, the elision corrector and the
// typo corrector: it acts on a word those leave alone — a perfectly valid French
// word that is the wrong inflection for its context. Its first rule is
// subject–verb agreement: a verb that disagrees with the subject pronoun right
// before it ("tu mange" → "tu manges", "ils mange" → "ils mangent") is
// re-conjugated to the form that agrees.
//
// Ultra-conservative by construction, like the rest of the engine — it fires
// only when every doubt is closed:
//   • the subject pronoun must be the IMMEDIATELY preceding word, and one of the
//     subject-only pronouns (je/tu/il/elle/on/ils/elles). nous and vous are
//     excluded: they double as preverbal object clitics ("il vous regarde"),
//     where the verb agrees with the real subject, not the clitic — a trap;
//   • the word must be a verb form and ONLY a verb form — a surface that also
//     reads as a noun or adjective ("ferme", "fait") is left untouched;
//   • it must actually disagree (a reading already matching the subject means
//     the literal is right);
//   • the agreeing form must be UNIQUE — if synthesis is empty or ambiguous, the
//     literal stands.
// As with every stage, a misfire is taken back through the correction inlay,
// whose undo writes the suppression — this proposes and never persists.
public sealed class GrammarCorrector : ICorrectionPolicy
{
    private readonly VerbMorphology _verbs;
    private readonly IPersonalLexicon? _personal;

    public GrammarCorrector(VerbMorphology verbs, IPersonalLexicon? personal = null)
    {
        _verbs = verbs;
        _personal = personal;
    }

    // The subject-only pronouns and the person-number a verb must take after
    // each. il/elle/on share the 3rd singular; ils/elles the 3rd plural. nous and
    // vous are deliberately absent (see the type comment).
    private static readonly Dictionary<string, string> SubjectPronouns = new(StringComparer.Ordinal)
    {
        ["je"] = "1s",
        ["tu"] = "2s",
        ["il"] = "3s",
        ["elle"] = "3s",
        ["on"] = "3s",
        ["ils"] = "3p",
        ["elles"] = "3p",
    };

    internal static bool TryGetRequiredPerson(
        IReadOnlyList<string> leftContext,
        out string personNumber)
    {
        personNumber = string.Empty;
        return leftContext.Count > 0
            && SubjectPronouns.TryGetValue(ContextTail(leftContext[^1]), out personNumber!);
    }

    // Elided conjunctions keep the grammatical word after the apostrophe:
    // qu'on behaves like on, just as j'ai exposes ai to the auxiliary rule.
    internal static string ContextTail(string word)
    {
        string lower = word.ToLowerInvariant();
        int apostrophe = Math.Max(lower.LastIndexOf('\''), lower.LastIndexOf('\u2019'));
        return apostrophe >= 0 && apostrophe + 1 < lower.Length
            ? lower[(apostrophe + 1)..]
            : lower;
    }

    // The verb modes that inflect for person and can stand after a subject
    // pronoun: indicative, subjunctive, conditional. The imperative also inflects
    // for person but takes no overt subject, so it never agrees here.
    private static bool IsFiniteMode(string mode) => mode is "ind" or "sub" or "cnd";

    public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext, CorrectionTrace? trace = null)
    {
        StageTrace? st = trace?.Open(CorrectionTrace.StageNames.Grammar);

        // The agreement window: a subject pronoun must sit immediately before.
        if (!TryGetRequiredPerson(leftContext, out string required))
            return Abstain(st, CorrectionTrace.Reasons.NoSubjectPronoun);

        string lower = word.ToLowerInvariant();

        IReadOnlyList<VerbReading> readings = _verbs.Analyses(lower);
        if (readings.Count == 0)
            return Abstain(st, CorrectionTrace.Reasons.NotAVerb);

        // A surface that also reads as a noun or adjective is never re-conjugated
        // — the user may have meant the other word.
        if (_verbs.IsAmbiguous(lower))
            return Abstain(st, CorrectionTrace.Reasons.VerbAmbiguous);

        // The user's own adopted words shield themselves.
        if (_personal?.IsAdopted(lower) == true)
            return Abstain(st, CorrectionTrace.Reasons.UserAdopted);

        // Keep the person-bearing readings; if a reading already agrees, the
        // literal is correct and we stand aside.
        var finite = new List<VerbReading>();
        foreach (VerbReading r in readings)
        {
            if (!IsFiniteMode(r.Mode) || r.PersonNumber.Length == 0)
                continue;
            if (r.PersonNumber == required)
                return Abstain(st, CorrectionTrace.Reasons.AlreadyAgrees);
            finite.Add(r);
        }
        if (finite.Count == 0)
            return Abstain(st, CorrectionTrace.Reasons.NotFinite);

        // Synthesise the agreeing form for each disagreeing reading. The fix is
        // safe only when they all resolve to one and the same surface: a missing
        // slot or two rival targets is real ambiguity, so the literal stands.
        var targets = new HashSet<string>(StringComparer.Ordinal);
        bool missing = false;
        foreach (VerbReading r in finite)
        {
            string? target = _verbs.Conjugate(r.Lemma, r.Mode, r.Tense, required);
            if (target is null)
                missing = true;
            else
                targets.Add(target);
        }

        if (st is not null)
            foreach (string t in targets)
                st.AddCandidate(t, 0.0, CorrectionTrace.Sources.Conjugation);

        if (missing || targets.Count != 1)
            return Abstain(st, CorrectionTrace.Reasons.NoUniqueTarget);

        string agreed = System.Linq.Enumerable.First(targets);
        if (agreed == lower)
            return Abstain(st, CorrectionTrace.Reasons.AlreadyAgrees);

        st?.Fire(CorrectionTrace.Reasons.SubjectVerbAgreement);
        return new CorrectionDecision(
            word, CasePattern.Apply(word, agreed), CorrectionReason.SubjectVerbAgreement);
    }

    private static CorrectionDecision? Abstain(StageTrace? st, string reason)
    {
        st?.Abstain(reason);
        return null;
    }
}
