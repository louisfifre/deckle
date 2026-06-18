namespace Deckle.Autocorrect;

// ── ElisionCorrector ─────────────────────────────────────────────────────────
//
// Restores a dropped elision apostrophe in a glued proclitic: "cest" → "c'est",
// "jai" → "j'ai", "larrache" → "l'arrache", "quil" → "qu'il". French elides a
// short proclitic (ce, de, je, le/la, me, ne, se, te, que) before a vowel or a
// mute h; typing fast, the apostrophe is the first thing dropped. This splits the
// glued form back apart — Louis's "taper à l'arrache et que ça réécrive".
//
// The load-bearing guard is that the glued token is NOT itself a French word:
// "dune", "quelle", "tas", "ces" are real words and stay untouched, while "cest",
// "jai", "quil" exist only as dropped elisions. On top of that the tail must be a
// valid French word that genuinely begins with a vowel or h, so the split yields
// real French and not a coincidence — and at least two letters long, so "ca" is
// left for the cedilla ("ça"), never hijacked into "c'a".
//
// Runs BEFORE the typo corrector: a plain edits-1 would otherwise mangle "cest"
// into "est" or "ces" before the apostrophe is ever considered.
public sealed class ElisionCorrector : ICorrectionPolicy
{
    // The elidable proclitics, "qu" first so it wins over a bare "q"-less letter.
    // A closed set — French has no other proclitics that drop a vowel here.
    private static readonly string[] Proclitics =
        { "qu", "c", "d", "j", "l", "m", "n", "s", "t" };

    private readonly FrequencyLexicon _french;
    private readonly IPersonalLexicon? _personal;

    public ElisionCorrector(FrequencyLexicon french, IPersonalLexicon? personal = null)
    {
        _french = french;
        _personal = personal;
    }

    public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext, CorrectionTrace? trace = null)
    {
        StageTrace? st = trace?.Open(CorrectionTrace.StageNames.Elision);

        // A proclitic (>=1) plus a two-letter tail: nothing shorter can elide.
        if (word.Length < 3)
            return Abstain(st, CorrectionTrace.Reasons.TooShort);

        // Letters only: an apostrophe means the elision is already there; a digit
        // or hyphen is out of scope.
        foreach (char c in word)
            if (!char.IsLetter(c))
                return Abstain(st, CorrectionTrace.Reasons.NonWordChar);

        // A camelCase/PascalCase identifier is never an elision — the user meant
        // it. An accented tail, by contrast, IS in scope ("jétais" → "j'étais"):
        // there the accent was typed and only the apostrophe was dropped.
        if (WordShape.HasInternalUpper(word))
            return Abstain(st, CorrectionTrace.Reasons.InternalCaps);

        // A capitalised word mid-utterance is almost always a proper noun; a
        // sentence-initial capital is the ordinary case and stays in scope.
        if (leftContext.Count > 0 && WordShape.IsTitleCase(word))
            return Abstain(st, CorrectionTrace.Reasons.ProperNounGuard);

        string lower = word.ToLowerInvariant();

        // The defining guard: a glued elision is never itself a valid word.
        // "dune", "quelle", "tas", "ces" are real and must pass through untouched.
        if (_french.Contains(lower))
            return Abstain(st, CorrectionTrace.Reasons.ValidFrench);
        if (_personal?.IsAdopted(lower) == true)
            return Abstain(st, CorrectionTrace.Reasons.UserAdopted);

        foreach (string proclitic in Proclitics)
        {
            // The tail must be at least two letters — "ca" stays for the cedilla.
            if (lower.Length < proclitic.Length + 2
                || !lower.StartsWith(proclitic, System.StringComparison.Ordinal))
                continue;

            string tail = lower[proclitic.Length..];

            // The tail must be a real French word that truly begins with a vowel
            // or mute h — the very condition that makes an elision happen.
            if (!IsElisionVowel(tail[0]) || !_french.Contains(tail))
                continue;

            string restored = proclitic + "'" + tail;
            st?.AddCandidate(restored, _french.FrequencyOf(tail), CorrectionTrace.Sources.Index)
              .Fire(CorrectionTrace.Reasons.Elision);
            return new CorrectionDecision(
                word, CasePattern.Apply(word, restored), CorrectionReason.Elision);
        }

        return Abstain(st, CorrectionTrace.Reasons.NoProclitic);
    }

    // Records the abstain reason onto the stage trace (when present) and leaves
    // the literal untouched.
    private static CorrectionDecision? Abstain(StageTrace? st, string reason)
    {
        st?.Abstain(reason);
        return null;
    }

    // The vowels and mute h that license an elision — accented vowels included,
    // since a fast-typed tail keeps its own accent ("léglise" → "l'église").
    private static bool IsElisionVowel(char c) =>
        "aeiouyàâäéèêëîïôöûüùœæh".IndexOf(char.ToLowerInvariant(c)) >= 0;
}
