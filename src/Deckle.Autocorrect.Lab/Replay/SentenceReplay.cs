using System;
using System.Collections.Generic;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// One ambiguous slot replayed through a reranker: what the user typed there, the
// corpus's recorded final form, and what the reranker (e.g. the ONNX judge) would
// say — its chosen form, the margin it cleared or missed, and its abstain reason.
// This is the raw material for calibrating the sentence-stage margin against real
// typing, without any live application.
public readonly record struct SlotReplayResult(
    string TypedForm,
    string FinalForm,
    int SlotIndex,
    string? JudgeChosen,
    double Margin,
    double Threshold,
    string? AbstainReason)
{
    public bool Abstained => JudgeChosen is null;

    // The reranker landed on the form the sentence actually ended with — a proxy
    // for "the judge would have agreed", meaningful only when it did not abstain.
    public bool AgreesWithFinal =>
        JudgeChosen is not null && string.Equals(JudgeChosen, FinalForm, StringComparison.Ordinal);
}

// Aggregate counts over a replay pass — the shape a calibration report reads.
public readonly record struct ReplaySummary(
    int Sentences, int AmbiguousSlots, int Chosen, int Abstained, int AgreedWithFinal);

// Replays the typed-sentence corpus through a reranker offline: every slot whose
// active probe exposes two or more closed choices (accent variants or bounded
// typo neighbours) is judged with the FINAL sentence as context — mirroring the
// live sentence stage — but nothing is applied. The judge
// (a full-sentence forced-decoding scorer) is seconds per slot, so this is a serial
// offline pass over collected corpus, never a hot-path stage. The reranker is
// injected, so the same runner diffs any two engine versions over the same corpus.
public static class SentenceReplay
{
    // Judges every ambiguous slot in one corpus sentence. typedWords and finalWords
    // are the sentence's word-forms as typed and as it ended; the caller aligns them
    // slot-for-slot (the I/O layer that reads the corpus owns tokenization). A fixed
    // right-context horizon skips slots that had not reached it and hides every
    // later word from the judge.
    public static IReadOnlyList<SlotReplayResult> ReplaySentence(
        IReadOnlyList<string> typedWords,
        IReadOnlyList<string> finalWords,
        IAmbiguityProbe probe,
        ISentenceReranker reranker,
        int? rightContextWords = null)
    {
        if (rightContextWords < 0)
            throw new ArgumentOutOfRangeException(nameof(rightContextWords));

        var results = new List<SlotReplayResult>();
        int n = Math.Min(typedWords.Count, finalWords.Count);
        for (int i = 0; i < n; i++)
        {
            if (rightContextWords is int requiredRight && i + requiredRight >= n)
                continue;

            // The typed literal joins the set so the judge can weigh taking a
            // commit-stage correction back — the sentence stage's real candidate set.
            IReadOnlyList<AccentVariant> candidates =
                probe.SentenceCandidates(typedWords[i], includeTypedLiteral: true);
            if (candidates.Count < 2)
                continue;

            IReadOnlyList<string> context = finalWords;
            if (rightContextWords is int right)
                context = finalWords.Take(i + right + 1).ToArray();

            RerankOutcome outcome = reranker.Rerank(context, i, candidates);
            results.Add(new SlotReplayResult(
                typedWords[i], finalWords[i], i,
                outcome.Chosen, outcome.Margin, outcome.Threshold, outcome.AbstainReason));
        }

        return results;
    }

    public static ReplaySummary Summarize(IEnumerable<SlotReplayResult> results, int sentenceCount)
    {
        int slots = 0, chosen = 0, abstained = 0, agreed = 0;
        foreach (SlotReplayResult r in results)
        {
            slots++;
            if (r.Abstained) abstained++;
            else chosen++;
            if (r.AgreesWithFinal) agreed++;
        }

        return new ReplaySummary(sentenceCount, slots, chosen, abstained, agreed);
    }
}
