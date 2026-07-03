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
        IReadOnlyList<double>? thresholds = null)
    {
        var slots = new List<SlotReplayResult>();
        int sentences = 0;
        foreach (SentenceCorpus.SentenceRecord record in corpus)
        {
            var (typed, final) = SentenceAlignment.Align(record);
            if (typed.Count == 0)
                continue;
            sentences++;
            slots.AddRange(SentenceReplay.ReplaySentence(typed, final, probe, reranker));
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
        IReadOnlyList<double>? thresholds = null) =>
        Run(CorpusReader.Read(corpusPath), probe, reranker, thresholds);
}

// The replay's whole output: the corpus-level counts, every judged slot (the raw
// material), the margin curve, and its rendered markdown for the maintainer.
public readonly record struct ReplayReport(
    ReplaySummary Summary,
    IReadOnlyList<SlotReplayResult> Slots,
    IReadOnlyList<CalibrationRow> Calibration,
    string Markdown);
