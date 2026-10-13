using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Deckle.Autocorrect.Lab;

// Truth is filled by hand, blank while unresolved. The corpus final is NOT ground
// truth: the user may never have corrected their own accent, so a judge « fix » the
// final disagrees with can still be right.
public readonly record struct TruthReviewRow(
    string Key, string FinalSentence, string TypedForm, string FinalForm, string JudgePick, string Truth);

// The reviewer-in-the-loop overlay for replay agreement. The replay lists every
// slot where the judge overruled the corpus final in a markdown sheet beside the
// calibration report; a reviewer fills the « truth » column; the next replay
// measures agreement against the resolved truth where set. Keys hash (typed
// sentence, slot index), not row numbers, so the sheet survives corpus growth and
// regeneration keeps the filled cells.
public static class TruthOverlay
{
    public const string FileName = "autocorrect.truth-review.md";

    public static string SheetPathFor(string corpusPath) =>
        Path.Combine(Path.GetDirectoryName(corpusPath) ?? ".", FileName);

    // SHA-256, not string.GetHashCode: that one is randomized per process and
    // would churn the sheet every run.
    public static string Key(string recordTyped, int slotIndex)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"{recordTyped}{slotIndex}");
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    public static IReadOnlyList<TruthReviewRow> Read(string path) =>
        File.Exists(path) ? ParseLines(File.ReadLines(path)) : Array.Empty<TruthReviewRow>();

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

    public static IReadOnlyDictionary<string, string> ResolvedTruths(IEnumerable<TruthReviewRow> rows)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TruthReviewRow r in rows)
            if (r.Truth.Length > 0)
                map[r.Key] = r.Truth;
        return map;
    }

    // Fresh rows decide which keys still exist; a filled truth from the existing
    // sheet carries onto its key, so hand-resolved truths survive regeneration.
    // Duplicate keys (the same typed sentence twice) keep their first occurrence.
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
