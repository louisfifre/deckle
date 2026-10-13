using System;
using System.Collections.Generic;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

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

    // A proxy only: the form the sentence ended with is not ground truth.
    public bool AgreesWithFinal =>
        JudgeChosen is not null && string.Equals(JudgeChosen, FinalForm, StringComparison.Ordinal);
}

public readonly record struct ReplaySummary(
    int Sentences, int AmbiguousSlots, int Chosen, int Abstained, int AgreedWithFinal);

// Replays the typed-sentence corpus through a reranker offline: every slot whose
// probe exposes two or more closed choices is judged with the FINAL sentence as
// context, mirroring the live sentence stage, but nothing is applied.
public static class SentenceReplay
{
    // The caller aligns typedWords and finalWords slot for slot. A fixed
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
