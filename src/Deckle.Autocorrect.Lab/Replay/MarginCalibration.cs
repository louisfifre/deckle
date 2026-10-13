using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Deckle.Autocorrect.Lab;

public readonly record struct CalibrationRow(
    double Threshold, int Applied, int Agreed, int Held, double Precision, double Coverage);

// Sweeps a margin threshold over replay results collected at margin 0, where the
// judge returns its argmax and top-vs-second gap for every slot without
// abstaining, and reports the precision/coverage tradeoff at each bar. It picks
// the sentence stage's operating margin offline, against real typing.
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

    public static string Render(ReplaySummary summary, IReadOnlyList<CalibrationRow> rows)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, summary);
        AppendTable(sb, rows);
        return sb.ToString();
    }

    public static string Render(
        ReplaySummary summary, CorpusIntake intake, IReadOnlyList<CalibrationRow> rows)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, summary);
        AppendIntake(sb, intake);
        AppendTable(sb, rows);
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, ReplaySummary summary)
    {
        sb.AppendLine("# Sentence-stage margin calibration");
        sb.AppendLine();
        sb.Append(Invariant($"Corpus: {summary.Sentences} sentences, {summary.AmbiguousSlots} ambiguous slots judged"));
        sb.AppendLine(Invariant($" — {summary.AgreedWithFinal} argmax matches, {summary.Abstained} model abstentions."));
    }

    private static void AppendIntake(StringBuilder sb, CorpusIntake intake)
    {
        sb.AppendLine(Invariant(
            $"Records: {intake.Replayed} replayed ({intake.LegacyRepaired} legacy repaired via final-string), {intake.Skipped} skipped as unusable."));
        sb.AppendLine(Invariant(
            $"Closure mix (replayed): {intake.ClosedOnSentence} sentence, {intake.ClosedOnEnter} enter, {intake.Interrupted} interrupted."));
        sb.AppendLine(Invariant(
            $"Ground truth: {intake.TruthOverlaid} slots truth-overlaid — agreement uses maintainer truth where set, corpus final elsewhere."));
    }

    private static void AppendTable(StringBuilder sb, IReadOnlyList<CalibrationRow> rows)
    {
        sb.AppendLine();
        sb.AppendLine("| margin ≥ | applied | agree | held | precision | coverage |");
        sb.AppendLine("|---------:|--------:|------:|-----:|----------:|---------:|");
        foreach (CalibrationRow r in rows)
            sb.AppendLine(Invariant(
                $"| {r.Threshold:0.00} | {r.Applied} | {r.Agreed} | {r.Held} | {r.Precision:0.0%} | {r.Coverage:0.0%} |"));
    }

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
