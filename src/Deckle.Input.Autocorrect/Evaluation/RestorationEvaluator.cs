using System.IO;
using Deckle.Input.Autocorrect.Engine;
using Deckle.Input.Autocorrect.Lexicon;
using Deckle.Input.Autocorrect.Tracking;

namespace Deckle.Input.Autocorrect.Evaluation;

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

        string? line;
        bool capped = false;
        while (!capped && (line = reference.ReadLine()) is not null)
        {
            foreach (string sentence in SplitSentences(line))
            {
                string? prevOut = null; // left context, carried within a sentence.

                foreach (string token in WordBoundaries.Tokenize(sentence))
                {
                    if (!HasLetter(token))
                    {
                        // Non-word tokens are not scored, but they still break
                        // left context — the policy would see them as prev.
                        prevOut = token.ToLowerInvariant();
                        continue;
                    }

                    if (opts.MaxTokens > 0 && report.TotalTokens >= opts.MaxTokens)
                    {
                        capped = true;
                        break;
                    }

                    // What the typist produces: accents stripped, case kept.
                    string typed = AccentFolding.StripDiacritics(token);
                    CorrectionDecision? decision = policy.Evaluate(typed, prevOut);
                    string output = decision?.Replacement ?? typed;

                    Classify(report, token, typed, output, missed, wrongForm, falseCorrections);

                    report.TotalTokens++;
                    prevOut = output.ToLowerInvariant();
                }

                if (capped)
                    break;
            }
        }

        report.TopMissed = Top(missed);
        report.TopWrongForm = Top(wrongForm);
        report.TopFalseCorrections = Top(falseCorrections);
        return report;
    }

    // Place one token in its class. The split hinges on whether the reference
    // carried accents (token != typed) and on what the policy emitted. Output
    // is compared to the reference ordinally — accents and case must match.
    private static void Classify(
        RestorationReport report,
        string token, string typed, string output,
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
            }
        }
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
}

// Tuning for the eval. The only knob is a token cap for quick smoke runs.
public sealed record EvaluatorOptions
{
    // Stop after this many scored tokens; 0 means no cap (the full reference).
    public int MaxTokens { get; init; } = 0;
}
