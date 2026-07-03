using System;
using System.Collections.Generic;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// The whole replay in one call: read the typed-text corpus, align each sentence,
// judge its ambiguous slots with the final sentence as context, and calibrate the
// margin over the lot. The probe and reranker are injected — pass the live FR
// lexicon and the staged ONNX judge for a real calibration, or fakes for a test —
// so the same runner also diffs any two engine versions over one corpus. Pure but
// for reading the corpus file; the judge is seconds per slot, so a real run is a
// serial offline pass, never a hot path.
public static class ReplayRunner
{
    // A spread from argmax (0) up, dense where the operating margin is likely to
    // sit, so the precision/coverage curve has resolution where it matters.
    public static readonly double[] DefaultThresholds = { 0.0, 0.25, 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 };

    public static ReplayReport Run(
        IEnumerable<SentenceCorpus.SentenceRecord> corpus,
        IAmbiguityProbe probe,
        ISentenceReranker reranker,
        IReadOnlyList<double>? thresholds = null,
        Action<ReplayProgress>? onProgress = null)
    {
        var slots = new List<SlotReplayResult>();
        int sentences = 0;
        foreach (SentenceCorpus.SentenceRecord record in corpus)
        {
            var (typed, final) = SentenceAlignment.Align(record);
            if (typed.Count == 0)
                continue;
            sentences++;
            IReadOnlyList<SlotReplayResult> sentenceSlots =
                SentenceReplay.ReplaySentence(typed, final, probe, reranker);
            slots.AddRange(sentenceSlots);
            onProgress?.Invoke(new ReplayProgress(sentences, slots.Count, sentenceSlots));
        }

        ReplaySummary summary = SentenceReplay.Summarize(slots, sentences);
        IReadOnlyList<CalibrationRow> calibration =
            MarginCalibration.Sweep(slots, thresholds ?? DefaultThresholds);
        return new ReplayReport(summary, slots, calibration, MarginCalibration.Render(summary, calibration));
    }

    public static ReplayReport Run(
        string corpusPath,
        IAmbiguityProbe probe,
        ISentenceReranker reranker,
        IReadOnlyList<double>? thresholds = null,
        Action<ReplayProgress>? onProgress = null) =>
        Run(CorpusReader.Read(corpusPath), probe, reranker, thresholds, onProgress);
}

// A live tick emitted once per replayed sentence: how many sentences and ambiguous
// slots have been judged so far, and the slots this sentence produced — enough for a
// caller to stream progress over a long offline pass that is otherwise blind.
public readonly record struct ReplayProgress(
    int SentenceIndex,
    int TotalSlotsJudged,
    IReadOnlyList<SlotReplayResult> SentenceSlots);

// The replay's whole output: the corpus-level counts, every judged slot (the raw
// material), the margin curve, and its rendered markdown for the maintainer.
public readonly record struct ReplayReport(
    ReplaySummary Summary,
    IReadOnlyList<SlotReplayResult> Slots,
    IReadOnlyList<CalibrationRow> Calibration,
    string Markdown);
