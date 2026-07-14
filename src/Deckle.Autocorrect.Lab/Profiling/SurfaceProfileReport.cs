using System.Collections.Generic;
using System.Text;

namespace Deckle.Autocorrect.Lab;

// Renders the ventilated surface profiles as the maintainer's markdown report —
// one table, busiest surface first, the whole-corpus row on top as the baseline.
// Same posture as the replay calibration report: a measured artifact to read,
// never a configuration to edit.
public static class SurfaceProfileReport
{
    public static string Render(SurfaceProfile overall, IReadOnlyList<SurfaceProfile> profiles)
    {
        var md = new StringBuilder();
        md.AppendLine("# Surface profiles — typed-sentence corpus ventilation");
        md.AppendLine();
        md.AppendLine(
            "How typing behaves per surface, measured from the corpus (CONTEXT.md § Surface profile): "
            + "how sentences end (a sentence boundary, an Enter, an interruption), at what rhythm, with "
            + "what pauses. Enter-heavy surfaces are where the sentence stage arrives too late and the "
            + "pause pass will matter; gap percentiles are inter-word-commit milliseconds, the raw "
            + "material of its threshold. Timed = records carrying a timing string (the gap population).");
        md.AppendLine();
        md.AppendLine(
            "| surface | sentences | words | %sentence | %enter | %interrupted | timed "
            + "| gaps | p50 | p75 | p90 | p99 | max |");
        md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        Row(md, overall);
        foreach (SurfaceProfile p in profiles)
            Row(md, p);
        return md.ToString();
    }

    private static void Row(StringBuilder md, SurfaceProfile p)
    {
        md.Append("| ").Append(p.Process)
          .Append(" | ").Append(p.Sentences)
          .Append(" | ").Append(p.Words)
          .Append(" | ").Append(Percent(p.SentenceClosed, p.Sentences))
          .Append(" | ").Append(Percent(p.EnterClosed, p.Sentences))
          .Append(" | ").Append(Percent(p.Interrupted + p.OtherClosed, p.Sentences))
          .Append(" | ").Append(p.TimedSentences)
          .Append(" | ").Append(p.Gaps.Count)
          .Append(" | ").Append(p.Gaps.P50)
          .Append(" | ").Append(p.Gaps.P75)
          .Append(" | ").Append(p.Gaps.P90)
          .Append(" | ").Append(p.Gaps.P99)
          .Append(" | ").Append(p.Gaps.Max)
          .AppendLine(" |");
    }

    private static string Percent(int part, int whole) =>
        whole == 0 ? "-" : $"{100.0 * part / whole:0}%";
}
