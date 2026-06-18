using System.Globalization;
using System.Text;

namespace Deckle.Autocorrect;

// ── CorrectionTrace ───────────────────────────────────────────────────────────
//
// The optional decision ledger threaded through the synchronous correction chain.
// Each policy stage, handed a non-null trace, records why it acted or stood aside
// — its guard exit, the candidate pool it weighed, and the safety gauges it
// compared against their thresholds — so the engine can emit one structured
// telemetry record per committed word: the full "why did (n't) it correct?".
//
// Null trace = the chain runs untouched at zero cost; the engine allocates one
// only when the decision-telemetry toggle is on. This is observation, never
// behaviour: a stage's decision must never read back from the trace. Text lives
// here by design (a candidate form IS a word), and leaves solely through the
// dedicated, opt-in autocorrect.decisions telemetry — never through stage logic,
// never onto the module's count-only EventSource path.
//
// Vocabularies (stage names, exit reasons, candidate sources) are closed and live
// here, one spelling in one place, so a grep on a reason finds every occurrence
// (logging doctrine). A new magnitude is added below before its first use.
public sealed class CorrectionTrace
{
    private readonly List<StageTrace> _stages = new();
    private bool _suppressed;

    // Opens a stage's sub-trace and returns it for the stage to fill. Stages are
    // appended in the order the composite ran them, so the trail reads top-down.
    public StageTrace Open(string stage)
    {
        var entry = new StageTrace(stage);
        _stages.Add(entry);
        return entry;
    }

    public IReadOnlyList<StageTrace> Stages => _stages;

    // A stage fired but a learned revert vetoed the correction: the word stayed
    // literal on screen. PrimaryStage/Reason still name what would have fired.
    public void MarkSuppressed() => _suppressed = true;

    // The outcome headline: vetoed by a learned revert, a correction fired, or the
    // literal stood untouched.
    public string Outcome =>
        _suppressed                    ? Outcomes.Suppressed
        : _stages.Exists(s => s.Fired) ? Outcomes.Corrected
        :                                Outcomes.Literal;

    // The stage that carries the story: the one that fired, else the richest one
    // that actually weighed candidates (the diacritics pool is the informative
    // case for a left-alone real-word ambiguity), else the last stage reached.
    public StageTrace? Primary
    {
        get
        {
            if (_stages.Count == 0) return null;
            StageTrace? fired = _stages.Find(s => s.Fired);
            if (fired is not null) return fired;

            StageTrace? richest = null;
            foreach (StageTrace s in _stages)
                if (s.Candidates.Count > 0 && (richest is null || s.Candidates.Count > richest.Candidates.Count))
                    richest = s;
            return richest ?? _stages[^1];
        }
    }

    // The decisive stage name / exit reason for the flat headline fields.
    public string PrimaryStage  => Primary?.Stage  ?? StageNames.None;
    public string PrimaryReason => Primary?.Reason ?? Reasons.NotEvaluated;

    // "form@freq@source|…" for the decisive stage's candidate pool — the variants
    // it ranked, each with its frequency-per-million and where it came from.
    public string RenderCandidates() => RenderCandidates(Primary);

    // "name=value|…" for the decisive stage's gauges — every safety magnitude it
    // compared, paired with its threshold (…_min / …_max) so the margin is read
    // straight off the line.
    public string RenderGauges() => RenderGauges(Primary);

    // "stage:reason|…" across every stage the composite ran, in order — the full
    // abstain chain behind a literal, or the run-up to the stage that fired.
    public string RenderTrail()
    {
        var sb = new StringBuilder();
        foreach (StageTrace s in _stages)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(s.Stage).Append(':').Append(s.Reason ?? Reasons.NotEvaluated);
        }
        return sb.ToString();
    }

    internal static string RenderCandidates(StageTrace? stage)
    {
        if (stage is null || stage.Candidates.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (TraceCandidate c in stage.Candidates)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(c.Form).Append('@').Append(Num(c.FrequencyPerMillion)).Append('@').Append(c.Source);
        }
        return sb.ToString();
    }

    internal static string RenderGauges(StageTrace? stage)
    {
        if (stage is null || stage.Gauges.Count == 0) return "";
        var sb = new StringBuilder();
        foreach ((string name, double value) in stage.Gauges)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(name).Append('=').Append(Num(value));
        }
        return sb.ToString();
    }

    // One number rendering, invariant culture: infinity is a ratio with a zero
    // denominator (a runaway dominance), shown as ∞ rather than a 1.7E308 wall.
    internal static string Num(double value)
    {
        if (double.IsPositiveInfinity(value)) return "∞";
        if (double.IsNaN(value)) return "nan";
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    // ── Revert gesture (same dataset, joined by id) ──────────────────────────
    //
    // The revert record is the third line of the decisions dataset (after the
    // synchronous decision and the deferred reranker verdict), so its closed
    // vocabularies live here too — one spelling in one place. A revert is not
    // threaded through the chain; these are plain classifiers, not stage state.

    // The boundary char a revert Backspace consumed, bucketed. The known misfire
    // lives in `Punctuation`: deleting a misplaced comma/period right after a
    // correction, misread as an undo. Closed vocabulary.
    public static class BoundaryKinds
    {
        public const string Whitespace  = "whitespace";
        public const string Punctuation = "punctuation";
        public const string Apostrophe  = "apostrophe";
        public const string Other       = "other";
    }

    // The fate of the revert injection. Closed vocabulary.
    public static class RevertOutcomes
    {
        public const string Restored = "restored"; // the literal was rewritten back
        public const string Desynced = "desynced"; // the rewrite did not land — screen disagrees
    }

    // Buckets the consumed boundary char. Apostrophe first: it is punctuation to
    // the framework but a distinct, attached boundary class in this engine.
    public static string ClassifyBoundary(char boundary)
    {
        if (boundary == '\'')                                      return BoundaryKinds.Apostrophe;
        if (char.IsWhiteSpace(boundary))                           return BoundaryKinds.Whitespace;
        if (char.IsPunctuation(boundary) || char.IsSymbol(boundary)) return BoundaryKinds.Punctuation;
        return BoundaryKinds.Other;
    }

    // Renders the consumed boundary char readably — whitespace as a name so the
    // line never carries an invisible or line-breaking glyph, else the char itself.
    public static string RenderBoundary(char boundary) => boundary switch
    {
        ' '  => "space",
        '\t' => "tab",
        '\n' => "newline",
        '\r' => "return",
        _    => boundary.ToString(),
    };

    // The committed word's fate. Closed vocabulary.
    public static class Outcomes
    {
        public const string Corrected  = "corrected";
        public const string Literal    = "literal";
        public const string Suppressed = "suppressed"; // a fire vetoed by a learned revert
    }

    // The correction stages, in chain order. Closed vocabulary.
    public static class StageNames
    {
        public const string Diacritics = "diacritics";
        public const string Elision    = "elision";
        public const string Typo       = "typo";
        public const string Grammar    = "grammar";
        public const string None       = "none";
    }

    // Where a candidate surface form came from. Closed vocabulary.
    public static class Sources
    {
        public const string Index       = "index";       // the accent index variants
        public const string Literal     = "literal";     // the typed form, itself valid French
        public const string Personal    = "personal";    // the user's adopted dictionary
        public const string Conjugation = "conjugation"; // synthesised from the verb paradigm
    }

    // Every guard exit and firing reason a stage can record. Closed vocabulary —
    // a grep on a name finds every place a word met that fate. Grouped by the
    // stage that owns it; the shared blacklist guards are listed once.
    public static class Reasons
    {
        // No stage opened (trace built but the chain never ran) / no primary.
        public const string NotEvaluated = "not_evaluated";

        // Shared blacklist guards (token class never touched).
        public const string TooShort          = "too_short";
        public const string HasDigit          = "has_digit";
        public const string NonWordChar       = "non_word_char";
        public const string InternalCaps      = "internal_caps";
        public const string TrailingApostrophe = "trailing_apostrophe";
        public const string AlreadyAccented   = "already_accented";
        public const string ProperNounGuard   = "proper_noun_guard";

        // Literal-protection guards.
        public const string ValidFrench    = "valid_french";
        public const string FrequentEnglish = "frequent_english";
        public const string UserAdopted    = "user_adopted";

        // Diacritics outcomes.
        public const string NoCandidates       = "no_candidates";
        public const string LiteralSingleton   = "literal_singleton";   // lone candidate IS the literal
        public const string LexicalGate        = "lexical_gate";        // fired: single accented variant
        public const string PersonalWord       = "personal_word";       // fired: from the personal dict
        public const string ContextPair        = "context_pair";        // fired: pair model cleared its margin
        public const string ContextKeptLiteral = "context_kept_literal"; // pair model chose the bare form
        public const string BelowMargin        = "below_margin";        // pair/dominance margin not cleared
        public const string FrequencyDominance = "frequency_dominance"; // fired: top variant overwhelms
        public const string NotDominant        = "not_dominant";        // dominance ratio / floor not met

        // Elision outcomes.
        public const string NoProclitic = "no_proclitic"; // no glued proclitic+vowel split into valid French
        public const string Elision     = "elision";      // fired: apostrophe restored

        // Typo outcomes. (A valid French word is the diacritics gate's domain and
        // exits this stage with the shared ValidFrench reason above.)
        public const string NoNeighbour = "no_neighbour"; // no in-lexicon word within edit distance
        public const string TooRare   = "too_rare";        // best neighbour below the frequency floor
        public const string TypoNear  = "typo_near";       // fired: one edit away
        public const string TypoFar   = "typo_far";        // fired: two edits away

        // Grammar outcomes.
        public const string NoSubjectPronoun = "no_subject_pronoun"; // no subject pronoun immediately before
        public const string NotAVerb         = "not_a_verb";          // the form carries no verb reading
        public const string VerbAmbiguous    = "verb_ambiguous";      // form doubles as a noun/adjective
        public const string NotFinite        = "not_finite";          // no person-bearing reading to agree
        public const string AlreadyAgrees    = "already_agrees";      // a reading already matches the subject
        public const string NoUniqueTarget   = "no_unique_target";    // zero or several agreeing forms — unsafe
        public const string SubjectVerbAgreement = "subject_verb_agreement"; // fired: re-conjugated to the subject
    }
}

// One stage's slice of the trace: its exit reason, whether it fired, the candidate
// pool it weighed, and the gauges it compared. Mutable and fluent — the stage fills
// it inline as it decides, with no branching beyond the null-trace check at entry.
public sealed class StageTrace
{
    internal StageTrace(string stage) => Stage = stage;

    public string Stage { get; }
    public string? Reason { get; private set; }
    public bool Fired { get; private set; }
    public List<TraceCandidate> Candidates { get; } = new();
    public List<(string Name, double Value)> Gauges { get; } = new();

    // The stage stood aside, leaving the literal — with the guard that decided it.
    public StageTrace Abstain(string reason)
    {
        Reason = reason;
        Fired = false;
        return this;
    }

    // The stage corrected the word — with the firing reason.
    public StageTrace Fire(string reason)
    {
        Reason = reason;
        Fired = true;
        return this;
    }

    // Records the candidate pool the stage ranked. Source tags where each form
    // came from (index / literal / personal).
    public StageTrace WithCandidates(IEnumerable<AccentVariant> candidates, Func<AccentVariant, string> source)
    {
        foreach (AccentVariant v in candidates)
            Candidates.Add(new TraceCandidate(v.Form, v.FrequencyPerMillion, source(v)));
        return this;
    }

    // Adds one candidate explicitly — for stages whose pool is not AccentVariants.
    public StageTrace AddCandidate(string form, double frequencyPerMillion, string source)
    {
        Candidates.Add(new TraceCandidate(form, frequencyPerMillion, source));
        return this;
    }

    // Records one safety gauge — a measured value or its threshold. Pair a value
    // with its bound (e.g. "margin" then "margin_min") so the line is self-reading.
    public StageTrace Gauge(string name, double value)
    {
        Gauges.Add((name, value));
        return this;
    }
}

// One weighed surface form: the candidate, its frequency-per-million, and its source.
public readonly record struct TraceCandidate(string Form, double FrequencyPerMillion, string Source);
