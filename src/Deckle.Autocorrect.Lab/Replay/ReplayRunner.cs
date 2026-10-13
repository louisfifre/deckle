using System;
using System.Collections.Generic;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// The whole replay in one call: read the typed-text corpus, align each sentence,
// judge its ambiguous slots with the final sentence as context, and calibrate the
// margin over the lot. The probe and reranker are injected (the live FR lexicon
// and the staged ONNX judge for a real calibration, fakes for a test), so the
// same runner also diffs two engine versions over one corpus. The judge is slow
// per slot, so a real run is a serial offline pass, never a hot path.
//
// Two validity guards run before any slot is judged. Alignment classifies each
// record (replayed, legacy-repaired via the final string, skipped as unusable)
// and those counts ride into the report, so records without a history field or
// with a corrupted typed string cannot skew the judge's measured precision
// unnoticed. And a ground-truth overlay lets a reviewer resolve the slots where
// the judge overruled the corpus final, which is not itself ground truth:
// agreement is then measured against the resolved truth where set.
public static class ReplayRunner
{
    // A spread from argmax (0) up, dense where the operating margin is likely to
    // sit, so the precision/coverage curve has resolution where it matters.
    public static readonly double[] DefaultThresholds = { 0.0, 0.25, 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 };

    public static ReplayReport Run(
        IEnumerable<CorpusEntry> corpus,
        IAmbiguityProbe probe,
        ISentenceReranker reranker,
        IReadOnlyDictionary<string, string>? resolvedTruths = null,
        IReadOnlyList<double>? thresholds = null,
        Action<ReplayProgress>? onProgress = null,
        int? rightContextWords = null)
    {
        var slots = new List<SlotReplayResult>();
        var review = new List<TruthReviewRow>();
        int replayed = 0, legacyRepaired = 0, skipped = 0, overlaid = 0;
        int closedSentence = 0, closedEnter = 0, interrupted = 0;

        foreach (CorpusEntry entry in corpus)
        {
            AlignmentResult alignment = SentenceAlignment.Align(entry);
            if (!alignment.Usable)
            {
                skipped++;
                continue;
            }

            replayed++;
            if (alignment.Status == AlignmentStatus.RepairedFromFinal)
                legacyRepaired++;
            TallyClosure(entry.Record.Closure, ref closedSentence, ref closedEnter, ref interrupted);

            IReadOnlyList<SlotReplayResult> raw =
                SentenceReplay.ReplaySentence(
                    alignment.Typed, alignment.Final, probe, reranker, rightContextWords);

            var sentenceSlots = new List<SlotReplayResult>(raw.Count);
            foreach (SlotReplayResult slot in raw)
            {
                string key = TruthOverlay.Key(entry.Record.Typed, slot.SlotIndex);
                string? truth = resolvedTruths is not null
                    && resolvedTruths.TryGetValue(key, out string? t) && t.Length > 0
                    ? t
                    : null;

                // The review sheet lists every slot the judge overruled the CORPUS
                // final on — recorded before any overlay rewrites that final — since
                // those are the cases whose true answer is unknown.
                if (slot.JudgeChosen is not null &&
                    !string.Equals(slot.JudgeChosen, slot.FinalForm, StringComparison.Ordinal))
                    review.Add(new TruthReviewRow(
                        key, entry.Record.Final, slot.TypedForm, slot.FinalForm, slot.JudgeChosen, truth ?? string.Empty));

                SlotReplayResult judged = slot;
                if (truth is not null)
                {
                    judged = slot with { FinalForm = truth };
                    overlaid++;
                }

                sentenceSlots.Add(judged);
            }

            slots.AddRange(sentenceSlots);
            onProgress?.Invoke(new ReplayProgress(replayed, slots.Count, sentenceSlots));
        }

        ReplaySummary summary = SentenceReplay.Summarize(slots, replayed);
        var intake = new CorpusIntake(
            replayed, legacyRepaired, skipped, overlaid, closedSentence, closedEnter, interrupted);
        IReadOnlyList<CalibrationRow> calibration =
            MarginCalibration.Sweep(slots, thresholds ?? DefaultThresholds);
        return new ReplayReport(
            summary, intake, slots, review, calibration,
            MarginCalibration.Render(summary, intake, calibration));
    }

    // Over raw records built inline (tests, an ad-hoc diff): every record is
    // treated as history-present, and no truth overlay applies.
    public static ReplayReport Run(
        IEnumerable<SentenceCorpus.SentenceRecord> corpus,
        IAmbiguityProbe probe,
        ISentenceReranker reranker,
        IReadOnlyList<double>? thresholds = null,
        Action<ReplayProgress>? onProgress = null,
        int? rightContextWords = null) =>
        Run(
            WrapPresent(corpus), probe, reranker, resolvedTruths: null,
            thresholds, onProgress, rightContextWords);

    public static ReplayReport Run(
        string corpusPath,
        IAmbiguityProbe probe,
        ISentenceReranker reranker,
        IReadOnlyList<double>? thresholds = null,
        Action<ReplayProgress>? onProgress = null,
        int? rightContextWords = null)
    {
        IReadOnlyDictionary<string, string> resolved =
            TruthOverlay.ResolvedTruths(TruthOverlay.Read(TruthOverlay.SheetPathFor(corpusPath)));
        return Run(
            CorpusReader.Read(corpusPath), probe, reranker, resolved,
            thresholds, onProgress, rightContextWords);
    }

    private static IEnumerable<CorpusEntry> WrapPresent(IEnumerable<SentenceCorpus.SentenceRecord> records)
    {
        foreach (SentenceCorpus.SentenceRecord record in records)
            yield return new CorpusEntry(record, HistoryPresent: true);
    }

    // An unknown closure falls to the sentence-ending default, matching
    // CorpusReader's own default.
    private static void TallyClosure(string closure, ref int sentence, ref int enter, ref int interrupted)
    {
        switch (closure)
        {
            case "enter": enter++; break;
            case "interrupted": interrupted++; break;
            default: sentence++; break;
        }
    }
}

public readonly record struct ReplayProgress(
    int SentenceIndex,
    int TotalSlotsJudged,
    IReadOnlyList<SlotReplayResult> SentenceSlots);

public readonly record struct CorpusIntake(
    int Replayed,
    int LegacyRepaired,
    int Skipped,
    int TruthOverlaid,
    int ClosedOnSentence,
    int ClosedOnEnter,
    int Interrupted);

public readonly record struct ReplayReport(
    ReplaySummary Summary,
    CorpusIntake Intake,
    IReadOnlyList<SlotReplayResult> Slots,
    IReadOnlyList<TruthReviewRow> TruthReview,
    IReadOnlyList<CalibrationRow> Calibration,
    string Markdown);
