using System.IO;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// ── RestorationEvaluator ────────────────────────────────────────────────────
//
// The measuring harness. It reads accented reference text, strips the accents
// off each token to synthesize what a QWERTY-US typist produces, feeds that to
// a correction policy exactly as the live engine would, and scores the output
// against the original reference.
//
// It is the only way to answer the questions that calibrate the engine: does
// the lexical gate alone restore the bulk of accents, does context cut into the
// ambiguous residue, and — the one that matters most — how often does the
// policy wreck a word the typist got right.
//
// Tokenization and sentence splitting MUST match the trainer (and through it
// the live tracker): the previous word handed to the policy has to be the same
// previous word the engine would see, or the eval measures a different model
// than the one that ships.
public static class RestorationEvaluator
{
    private static readonly char[] SentenceBreaks = { '.', '!', '?', ';', ':', '…' };

    public static RestorationReport Evaluate(
        TextReader reference,
        ICorrectionPolicy policy,
        EvaluatorOptions? options = null)
    {
        var opts = options ?? new EvaluatorOptions();
        var report = new RestorationReport();

        // Offender tallies, materialized to top-25 at the end.
        var missed = new Dictionary<string, long>(StringComparer.Ordinal);
        var wrongForm = new Dictionary<string, long>(StringComparer.Ordinal);
        var falseCorrections = new Dictionary<string, long>(StringComparer.Ordinal);

        // Per-stage emitted-correction tally, keyed by the reason the policy gave.
        var byStage = new Dictionary<CorrectionReason, StageTally>();

        string? line;
        bool capped = false;
        while (!capped && (line = reference.ReadLine()) is not null)
        {
            foreach (string sentence in SplitSentences(line))
            {
                // Two-deep left context, carried within a sentence: prev1 is the
                // immediate previous output, prev2 the one before it.
                string? prev1 = null;
                string? prev2 = null;

                foreach (string token in WordBoundaries.Tokenize(sentence))
                {
                    if (!HasLetter(token))
                    {
                        // Non-word tokens are not scored, but they still break
                        // left context — the policy would see them as prev.
                        prev2 = prev1;
                        prev1 = token.ToLowerInvariant();
                        continue;
                    }

                    if (opts.MaxTokens > 0 && report.TotalTokens >= opts.MaxTokens)
                    {
                        capped = true;
                        break;
                    }

                    // What the typist produces: accents stripped, case kept.
                    string typed = AccentFolding.StripDiacritics(token);
                    CorrectionDecision? decision = policy.Evaluate(typed, LeftContext(prev2, prev1));
                    string output = decision?.Replacement ?? typed;

                    Classify(report, token, typed, output, decision, byStage,
                        missed, wrongForm, falseCorrections);

                    report.TotalTokens++;
                    prev2 = prev1;
                    prev1 = output.ToLowerInvariant();
                }

                if (capped)
                    break;
            }
        }

        report.ByStage = byStage;
        report.TopMissed = Top(missed);
        report.TopWrongForm = Top(wrongForm);
        report.TopFalseCorrections = Top(falseCorrections);
        return report;
    }

    // Like Evaluate, but with a post-sentence reranker stage. The gate runs per
    // word first, leaving ambiguous slots as literals; once the sentence is
    // complete the reranker reconsiders each ambiguous slot with the FULL
    // bidirectional context and may override the literal. Binning sees the final
    // output. This is the offline measurement of the live two-stage design —
    // no keyboard, no threading, no cursor: pure scoring against the reference.
    public static RestorationReport EvaluateReranked(
        TextReader reference,
        ICorrectionPolicy policy,
        IAmbiguityProbe probe,
        ISentenceReranker reranker,
        EvaluatorOptions? options = null)
    {
        var opts = options ?? new EvaluatorOptions();
        var report = new RestorationReport();
        var missed = new Dictionary<string, long>(StringComparer.Ordinal);
        var wrongForm = new Dictionary<string, long>(StringComparer.Ordinal);
        var falseCorrections = new Dictionary<string, long>(StringComparer.Ordinal);
        var byStage = new Dictionary<CorrectionReason, StageTally>();

        string? line;
        bool capped = false;
        while (!capped && (line = reference.ReadLine()) is not null)
        {
            foreach (string sentence in SplitSentences(line))
            {
                // Pass 1 — the gate, word by word, collecting the sentence's word
                // outputs and marking the ambiguous slots it left as literals.
                var words = new List<WordEval>();
                string? prev1 = null;
                string? prev2 = null;

                foreach (string token in WordBoundaries.Tokenize(sentence))
                {
                    if (!HasLetter(token))
                    {
                        prev2 = prev1;
                        prev1 = token.ToLowerInvariant();
                        continue;
                    }
                    if (opts.MaxTokens > 0 && report.TotalTokens >= opts.MaxTokens)
                    {
                        capped = true;
                        break;
                    }

                    string typed = AccentFolding.StripDiacritics(token);
                    CorrectionDecision? decision = policy.Evaluate(typed, LeftContext(prev2, prev1));
                    string output = decision?.Replacement ?? typed;

                    // Only slots the gate left as the literal (output == typed)
                    // and whose fold is ambiguous are the reranker's to reconsider.
                    IReadOnlyList<AccentVariant> candidates =
                        string.Equals(output, typed, StringComparison.Ordinal)
                            ? probe.AmbiguousCandidates(typed)
                            : Array.Empty<AccentVariant>();

                    words.Add(new WordEval(token, typed, output, decision, candidates));
                    report.TotalTokens++;
                    prev2 = prev1;
                    prev1 = output.ToLowerInvariant();
                }

                // Pass 2 — rerank ambiguous slots with the full sentence context,
                // left to right so a resolved slot informs the next.
                var sequence = new List<string>(words.Count);
                foreach (var w in words) sequence.Add(w.Output);

                for (int i = 0; i < words.Count; i++)
                {
                    var w = words[i];
                    if (w.Candidates.Count < 2)
                        continue;
                    string? chosen = reranker.Rerank(sequence, i, w.Candidates).Chosen;
                    if (chosen is null)
                        continue;
                    string newOutput = CasePattern.Apply(w.Typed, chosen);
                    if (string.Equals(newOutput, w.Output, StringComparison.Ordinal))
                        continue;
                    words[i] = w with
                    {
                        Output = newOutput,
                        Decision = new CorrectionDecision(w.Typed, newOutput, CorrectionReason.SentenceReranker),
                    };
                    sequence[i] = newOutput;
                }

                // Pass 3 — bin the final outputs.
                foreach (var w in words)
                    Classify(report, w.Reference, w.Typed, w.Output, w.Decision, byStage,
                        missed, wrongForm, falseCorrections);

                if (capped)
                    break;
            }
        }

        report.ByStage = byStage;
        report.TopMissed = Top(missed);
        report.TopWrongForm = Top(wrongForm);
        report.TopFalseCorrections = Top(falseCorrections);
        return report;
    }

    // Runs the live policy (the gate) then the post-sentence reranker over a
    // line of text AS TYPED — no accent-stripping unless asked, no scoring, no
    // reference. This is the engine of the `dry-run` command: it answers "what
    // would the engine do to this exact text?", word by word. Sentence
    // splitting and tokenization match Evaluate and the live tracker, so the
    // verdict is the one that would ship. `strip` simulates a typist from
    // already-accented text (paste real French, see what we recover).
    public static IReadOnlyList<WordOutcome> RestoreLine(
        string line,
        ICorrectionPolicy policy,
        IAmbiguityProbe probe,
        ISentenceReranker reranker,
        bool strip = false)
    {
        var outcomes = new List<WordOutcome>();

        foreach (string sentence in SplitSentences(line))
        {
            // Pass 1 — the gate, word by word. Non-word tokens are skipped: they
            // are not corrected and the reranker scores a word-only sequence.
            var words = new List<WordEval>();
            foreach (string token in WordBoundaries.Tokenize(sentence))
            {
                if (!HasLetter(token))
                    continue;

                string typed = strip ? AccentFolding.StripDiacritics(token) : token;
                // Reranker-direct mode carries no bigram, so the gate never
                // reads left context — an empty context is exactly what the live
                // engine would feed it here.
                CorrectionDecision? decision = policy.Evaluate(typed, Array.Empty<string>());
                string output = decision?.Replacement ?? typed;

                IReadOnlyList<AccentVariant> candidates =
                    string.Equals(output, typed, StringComparison.Ordinal)
                        ? probe.AmbiguousCandidates(typed)
                        : Array.Empty<AccentVariant>();

                words.Add(new WordEval(typed, typed, output, decision, candidates));
            }

            // Pass 2 — rerank ambiguous slots with the full sentence context,
            // left to right so a resolved slot informs the next.
            var sequence = new List<string>(words.Count);
            foreach (var w in words) sequence.Add(w.Output);

            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                if (w.Candidates.Count < 2)
                    continue;
                string? chosen = reranker.Rerank(sequence, i, w.Candidates).Chosen;
                if (chosen is null)
                    continue;
                string newOutput = CasePattern.Apply(w.Typed, chosen);
                if (string.Equals(newOutput, w.Output, StringComparison.Ordinal))
                    continue;
                words[i] = w with
                {
                    Output = newOutput,
                    Decision = new CorrectionDecision(w.Typed, newOutput, CorrectionReason.SentenceReranker),
                };
                sequence[i] = newOutput;
            }

            foreach (var w in words)
                outcomes.Add(new WordOutcome(w.Typed, w.Output, w.Decision?.Reason, w.Candidates.Count >= 2));
        }

        return outcomes;
    }

    // Place one token in its class. The split hinges on whether the reference
    // carried accents (token != typed) and on what the policy emitted. Output
    // is compared to the reference ordinally — accents and case must match.
    private static void Classify(
        RestorationReport report,
        string token, string typed, string output,
        CorrectionDecision? decision,
        Dictionary<CorrectionReason, StageTally> byStage,
        Dictionary<string, long> missed,
        Dictionary<string, long> wrongForm,
        Dictionary<string, long> falseCorrections)
    {
        bool accented = !string.Equals(token, typed, StringComparison.Ordinal);

        if (accented)
        {
            report.AccentedRef++;
            if (string.Equals(output, token, StringComparison.Ordinal))
            {
                report.Restored++;
                BumpStage(byStage, decision, correct: true);
            }
            else if (string.Equals(output, typed, StringComparison.Ordinal))
            {
                report.Missed++;
                Bump(missed, token);
            }
            else
            {
                report.WrongForm++;
                Bump(wrongForm, token);
                BumpStage(byStage, decision, correct: false);
            }
        }
        else
        {
            report.BareRef++;
            if (string.Equals(output, token, StringComparison.Ordinal))
                report.Untouched++;
            else
            {
                report.FalseCorrections++;
                Bump(falseCorrections, token); // the word the typist got right.
                BumpStage(byStage, decision, correct: false);
            }
        }
    }

    // Credit one emitted correction to the stage that produced it. The three
    // acted branches always carry a non-null decision (output != typed implies
    // the policy returned one); the guard keeps it total regardless.
    private static void BumpStage(
        Dictionary<CorrectionReason, StageTally> byStage, CorrectionDecision? decision, bool correct)
    {
        if (decision is null)
            return;
        if (!byStage.TryGetValue(decision.Reason, out var tally))
            byStage[decision.Reason] = tally = new StageTally();
        tally.Acted++;
        if (correct) tally.Correct++;
        else tally.Wrong++;
    }

    private static void Bump(Dictionary<string, long> tally, string word) =>
        tally[word] = tally.TryGetValue(word, out long c) ? c + 1 : 1;

    private static IReadOnlyList<(string Word, long Count)> Top(Dictionary<string, long> tally)
    {
        var list = new List<(string Word, long Count)>(tally.Count);
        foreach (var (word, count) in tally)
            list.Add((word, count));
        list.Sort(static (a, b) => b.Count.CompareTo(a.Count));
        if (list.Count > 25)
            list.RemoveRange(25, list.Count - 25);
        return list;
    }

    // Mirrors PairModelTrainer.SplitSentences exactly — same notion of a
    // sentence boundary, blank fragments skipped — so the eval feeds the policy
    // the same left context the trainer learned from.
    private static IEnumerable<string> SplitSentences(string line)
    {
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (Array.IndexOf(SentenceBreaks, line[i]) >= 0)
            {
                string fragment = line[start..i];
                if (!string.IsNullOrWhiteSpace(fragment))
                    yield return fragment;
                start = i + 1;
            }
        }
        string tail = line[start..];
        if (!string.IsNullOrWhiteSpace(tail))
            yield return tail;
    }

    private static bool HasLetter(string token)
    {
        foreach (char c in token)
            if (char.IsLetter(c))
                return true;
        return false;
    }

    // The left context handed to the policy, most recent last: empty at sentence
    // start, one word once there is a previous, two from the third word on.
    private static IReadOnlyList<string> LeftContext(string? prev2, string? prev1) =>
        prev1 is null ? Array.Empty<string>()
        : prev2 is null ? new[] { prev1 }
        : new[] { prev2, prev1 };

    // One word token's evaluation in the reranked pass: the reference form, the
    // typist's stripped input, the current output, the decision behind it, and
    // the closed candidate set when it is an ambiguous slot (else empty).
    private readonly record struct WordEval(
        string Reference, string Typed, string Output,
        CorrectionDecision? Decision, IReadOnlyList<AccentVariant> Candidates);
}

// Tuning for the eval. The only knob is a token cap for quick smoke runs.
public sealed record EvaluatorOptions
{
    // Stop after this many scored tokens; 0 means no cap (the full reference).
    public int MaxTokens { get; init; } = 0;
}

// One word's dry-run verdict (RestoreLine): the text as the policy saw it, the
// final form after gate + rerank, the stage that acted (null = left untouched),
// and whether it was an ambiguous slot the reranker was offered — true with a
// SentenceReranker reason means it resolved it, true with no change means it
// was offered the choice and abstained.
public readonly record struct WordOutcome(
    string Typed, string Output, CorrectionReason? Reason, bool WasAmbiguous);
