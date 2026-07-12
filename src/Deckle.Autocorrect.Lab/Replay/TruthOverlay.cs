using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Deckle.Autocorrect.Lab;

// One row of the ground-truth review sheet: a stable key, the sentence for context,
// the typed and corpus-final forms of the slot, the judge's pick, and the truth the
// maintainer fills by hand (blank while unresolved). The corpus final is NOT ground
// truth — the user may never have corrected their own accent, so a judge « fix » the
// final disagrees with can still be right. This sheet is where that gets resolved.
public readonly record struct TruthReviewRow(
    string Key, string FinalSentence, string TypedForm, string FinalForm, string JudgePick, string Truth);

// The maintainer-in-the-loop overlay for replay agreement. The replay lists every
// slot where the judge overruled the corpus final into a markdown sheet next to the
// calibration report; the maintainer fills the « truth » column at leisure; the next
// replay reads it back and measures agreement against the resolved truth where set,
// the corpus final elsewhere. Keys are a stable hash of (typed sentence, slot index),
// not row numbers, so the sheet survives corpus growth and a re-generation preserves
// already-filled cells for keys that still disagree.
//
// The sheet is a plain markdown table so it renders and hand-edits anywhere. Parsing
// is tolerant: only rows whose first cell is a hash key are read, the header and
// separator rows are skipped, and a blank truth cell is simply « unresolved ». Cell
// values are sanitized on write (a stray '|' or newline would break the table), so a
// round-trip is lossless for the one column the maintainer touches.
public static class TruthOverlay
{
    public const string FileName = "autocorrect.truth-review.md";

    public static string SheetPathFor(string corpusPath) =>
        Path.Combine(Path.GetDirectoryName(corpusPath) ?? ".", FileName);

    // A stable, machine-independent key: the first 12 hex chars of SHA-256 over the
    // typed sentence and slot index. String.GetHashCode is randomized per process
    // and would churn the sheet every run — this does not.
    public static string Key(string recordTyped, int slotIndex)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"{recordTyped}{slotIndex}");
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    // Reads the sheet back tolerantly; an absent file is an empty sheet, not an error.
    public static IReadOnlyList<TruthReviewRow> Read(string path) =>
        File.Exists(path) ? ParseLines(File.ReadLines(path)) : Array.Empty<TruthReviewRow>();

    // The same parse over an in-memory sheet — the round-trip the tests exercise, and
    // any caller holding the markdown rather than a path.
    public static IReadOnlyList<TruthReviewRow> Parse(string markdown) =>
        ParseLines(markdown.Split('\n'));

    private static IReadOnlyList<TruthReviewRow> ParseLines(IEnumerable<string> lines)
    {
        var rows = new List<TruthReviewRow>();
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] != '|')
                continue;

            string[] cells = SplitRow(line);
            if (cells.Length < 6)
                continue;

            string key = cells[0];
            if (key.Length == 0 || key == "key" || IsSeparator(key))
                continue; // header row, separator row, or a stray table line

            rows.Add(new TruthReviewRow(key, cells[1], cells[2], cells[3], cells[4], cells[5]));
        }

        return rows;
    }

    // The resolved subset: key → truth for every row whose truth cell is filled.
    public static IReadOnlyDictionary<string, string> ResolvedTruths(IEnumerable<TruthReviewRow> rows)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TruthReviewRow r in rows)
            if (r.Truth.Length > 0)
                map[r.Key] = r.Truth;
        return map;
    }

    // Merges a freshly-generated set of rows with an existing sheet: the fresh rows
    // are authoritative for which keys still exist (a slot that no longer disagrees
    // drops out), but a filled truth cell from the existing sheet is carried onto its
    // key when the fresh row left it blank — so hand-resolved truths are never lost to
    // regeneration. Duplicate keys (the same typed sentence twice in the corpus)
    // collapse to their first occurrence.
    public static IReadOnlyList<TruthReviewRow> Merge(
        IEnumerable<TruthReviewRow> fresh, IEnumerable<TruthReviewRow> existing)
    {
        var filled = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TruthReviewRow e in existing)
            if (e.Truth.Length > 0)
                filled[e.Key] = e.Truth;

        var merged = new List<TruthReviewRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TruthReviewRow f in fresh)
        {
            if (!seen.Add(f.Key))
                continue;
            string truth = f.Truth.Length > 0
                ? f.Truth
                : filled.TryGetValue(f.Key, out string? t) ? t : string.Empty;
            merged.Add(f with { Truth = truth });
        }

        return merged;
    }

    // Renders the sheet as markdown the maintainer edits in place.
    public static string Render(IEnumerable<TruthReviewRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Autocorrect replay — ground-truth review");
        sb.AppendLine();
        sb.AppendLine("Slots where the judge overruled the corpus final. The corpus final is not");
        sb.AppendLine("ground truth: the user may simply never have corrected their own accent, so a");
        sb.AppendLine("judge fix the final disagrees with can still be right. Fill the `truth` column");
        sb.AppendLine("with the correct surface form, or leave it blank while unresolved. Keys are");
        sb.AppendLine("stable across corpus growth; filled cells survive regeneration.");
        sb.AppendLine();
        sb.AppendLine("| key | sentence | typed | final | judge | truth |");
        sb.AppendLine("|-----|----------|-------|-------|-------|-------|");
        foreach (TruthReviewRow r in rows)
            sb.AppendLine(
                $"| {r.Key} | {Cell(r.FinalSentence)} | {Cell(r.TypedForm)} | {Cell(r.FinalForm)} | {Cell(r.JudgePick)} | {Cell(r.Truth)} |");

        return sb.ToString();
    }

    // Table-safe cell: a '|' would open a spurious column and a newline would end the
    // row, so both are neutralized. Keys, forms and truths never contain them; only a
    // free-text sentence might.
    private static string Cell(string value) =>
        value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string[] SplitRow(string line)
    {
        string inner = line;
        if (inner.StartsWith('|'))
            inner = inner[1..];
        if (inner.EndsWith('|'))
            inner = inner[..^1];

        string[] parts = inner.Split('|');
        for (int i = 0; i < parts.Length; i++)
            parts[i] = parts[i].Trim();
        return parts;
    }

    private static bool IsSeparator(string cell) =>
        cell.All(c => c is '-' or ':');
}
