using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Deckle.Autocorrect.Lab;

// One row of the calibration curve: at a given confidence margin, how many
// ambiguous slots the sentence stage would APPLY (the judge's top form cleared
// the bar), how many of those match what the sentence actually ended with
// (Agreed), how many it would HOLD instead, and the two rates the maintainer
// reads off — precision (agreed / applied, the trust) and coverage (applied /
// all ambiguous slots, the reach).
public readonly record struct CalibrationRow(
    double Threshold, int Applied, int Agreed, int Held, double Precision, double Coverage);

// Sweeps a margin threshold over replay results collected at margin 0 (the judge
// returns its raw argmax and the top-vs-second gap for every slot, never
// abstaining on the margin) and reports the precision/coverage tradeoff at each
// bar. This is what picks the sentence stage's operating margin against real
// typing, offline — the calibration the live engine cannot do on itself.
public static class MarginCalibration
{
    public static IReadOnlyList<CalibrationRow> Sweep(
        IReadOnlyList<SlotReplayResult> results, IReadOnlyList<double> thresholds)
    {
        int total = results.Count;
        var rows = new List<CalibrationRow>(thresholds.Count);
        foreach (double threshold in thresholds)
        {
            int applied = 0, agreed = 0;
            foreach (SlotReplayResult r in results)
            {
                // A model error (null verdict even at margin 0) is genuine
                // non-coverage: never applied, but it still counts in the total.
                if (r.Abstained || r.Margin < threshold)
                    continue;
                applied++;
                if (r.AgreesWithFinal)
                    agreed++;
            }

            double precision = applied == 0 ? 0.0 : (double)agreed / applied;
            double coverage = total == 0 ? 0.0 : (double)applied / total;
            rows.Add(new CalibrationRow(threshold, applied, agreed, total - applied, precision, coverage));
        }

        return rows;
    }

    // A self-reading markdown report: the corpus counts, then the curve. The
    // maintainer reads down the rows for the margin where precision is high
    // enough to trust and coverage still worth having.
    public static string Render(ReplaySummary summary, IReadOnlyList<CalibrationRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Sentence-stage margin calibration");
        sb.AppendLine();
        sb.Append(Invariant($"Corpus: {summary.Sentences} sentences, {summary.AmbiguousSlots} ambiguous slots judged"));
        sb.AppendLine(Invariant($" — {summary.AgreedWithFinal} argmax matches, {summary.Abstained} model abstentions."));
        sb.AppendLine();
        sb.AppendLine("| margin ≥ | applied | agree | held | precision | coverage |");
        sb.AppendLine("|---------:|--------:|------:|-----:|----------:|---------:|");
        foreach (CalibrationRow r in rows)
            sb.AppendLine(Invariant(
                $"| {r.Threshold:0.00} | {r.Applied} | {r.Agreed} | {r.Held} | {r.Precision:0.0%} | {r.Coverage:0.0%} |"));

        return sb.ToString();
    }

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
