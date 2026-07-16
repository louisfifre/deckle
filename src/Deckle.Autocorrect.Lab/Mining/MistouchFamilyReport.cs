using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Deckle.Autocorrect.Lab;

// Renders the mined families twice from one result: the markdown the maintainer
// reviews (the one-time gate before family adoption turns automatic), and the
// JSON the routing wiring will consume once reviewed. Same data, two readers —
// neither is ever edited by hand into the other.
public static class MistouchFamilyReport
{
    public static string RenderMarkdown(MistouchMiner.MiningResult result)
    {
        var md = new StringBuilder();
        md.AppendLine("# Mistouch families — mined from the typed-sentence corpus");
        md.AppendLine();
        md.AppendLine(
            "Recurrent mechanical keyboard slips (CONTEXT.md § Mistouch family), mined offline: "
            + "repaired = evidenced by a user backspace-and-retype in the corpus history; residue = "
            + "a non-word still standing in the final text that exactly one bounded mechanical repair "
            + "reads (flagged ambiguous when several do — the sentence-stage routing case). Validity "
            + "is tested against the French lexicon and the global-English seed only; the personal "
            + "vocabulary is live app state and is not consulted offline. Nothing here is active: "
            + "this batch is the maintainer-review gate.");
        md.AppendLine();
        md.AppendLine("| family | kind | evidence | repaired | residue | days | ambiguous | from-word | examples |");
        md.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (MistouchMiner.MistouchFamily f in result.Families)
        {
            string examples = string.Join("; ", f.Examples
                .Take(4)
                .Select(e => $"{e.From}→{e.To}"));
            md.Append("| ").Append(f.Signature)
              .Append(" | ").Append(f.Kind)
              .Append(" | ").Append(f.Evidence)
              .Append(" | ").Append(f.RepairedCount)
              .Append(" | ").Append(f.ResidueCount)
              .Append(" | ").Append(f.DistinctDays)
              .Append(" | ").Append(f.AmbiguousCount)
              .Append(" | ").Append(f.FromWordCount)
              .Append(" | ").Append(examples)
              .AppendLine(" |");
        }

        md.AppendLine();
        md.AppendLine("## Unclassified user repairs");
        md.AppendLine();
        md.AppendLine(
            "Backspace-and-retype pairs no mechanical signature reads — rewordings, multi-edit "
            + "fixes, or a family the miner does not know yet. Shown so mining's blind spot stays "
            + "visible; a recurring shape here is the next signature to add.");
        md.AppendLine();
        md.AppendLine($"{result.Unclassified.Count} pairs.");
        foreach (MistouchMiner.MistouchEvidence e in result.Unclassified.Take(30))
            md.AppendLine($"- `{e.From}` → `{e.To}` ({e.Process}, {e.Day})");
        return md.ToString();
    }

    public static string RenderJson(MistouchMiner.MiningResult result)
    {
        var dto = result.Families.Select(f => new
        {
            signature = f.Signature,
            kind = f.Kind,
            evidence = f.Evidence,
            repaired = f.RepairedCount,
            residue = f.ResidueCount,
            distinctDays = f.DistinctDays,
            ambiguous = f.AmbiguousCount,
            fromWord = f.FromWordCount,
            examples = f.Examples.Select(e => new
            {
                from = e.From,
                to = e.To,
                process = e.Process,
                day = e.Day,
                repaired = e.Repaired,
                fromIsWord = e.FromIsWord,
                ambiguous = e.Ambiguous,
            }),
        });
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
