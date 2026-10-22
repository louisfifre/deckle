using System.Globalization;
using System.Text;

namespace Deckle.Anytype;

// The body-edit engine, pure and I/O-free: locate a section by its heading and
// replace the content under it, leaving every other section exactly as Anytype
// returned it. A markdown body PATCH is a full replacement (Anytype exposes no
// block-level REST surface), so a section edit has to rewrite the whole document
// — but the rewrite SPLICES only the targeted span and copies the rest verbatim,
// never round-tripping the untouched part through a parser that would reflow it.
//
// A "section" is a heading line (`#`..`######`) plus every following line up to
// the next heading of level ≤ its own; deeper sub-headings belong to the section
// and are replaced with it. The heading line is kept — only the body under it is
// replaced.
//
// Round-trip facts this is built against: Anytype destroys-and-recreates
// blocks on every PATCH then re-exports markdown, so the read-back is a
// NORMALIZED render, not the bytes sent. Heading text and level
// survive intact (hence "find by title" is sound), but a literal _ * ` | comes
// back backslash-escaped and lines carry GFM hard-break trailing spaces. Matching
// and verification therefore compare a normalized form — unescape those four,
// drop trailing whitespace, ignore blank lines — never raw byte equality, which
// is structurally impossible.
public static class MarkdownBody
{
    public enum EditStatus { Replaced, NotFound, Ambiguous }

    // Outcome of a replace. Body is the rewritten document on Replaced, and the
    // input unchanged otherwise; MatchCount is meaningful on Ambiguous.
    public readonly record struct SectionEdit(EditStatus Status, string Body, int MatchCount);

    // Replaces the body under the section whose heading text equals `heading`
    // (normalized, case-insensitive). Strict by design: a missing heading is
    // NotFound and a heading carried by more than one line is Ambiguous — the
    // caller turns both into model-facing errors instead of guessing, so a
    // mistyped title never spawns a stray section.
    public static SectionEdit ReplaceSection(string body, string heading, string content)
    {
        string target = NormalizeHeading(heading);
        string[] lines = body.Split('\n');

        int matchLine = -1, matchLevel = 0, matchCount = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!TryHeading(lines[i], out int level, out string text)) continue;
            if (!NormalizeHeading(text).Equals(target, StringComparison.OrdinalIgnoreCase)) continue;

            matchCount++;
            if (matchLine < 0) { matchLine = i; matchLevel = level; }
        }

        if (matchCount == 0) return new(EditStatus.NotFound, body, 0);
        if (matchCount > 1) return new(EditStatus.Ambiguous, body, matchCount);

        int end = SectionEnd(lines, matchLine, matchLevel);

        // The blank line(s) before the next heading separate the two sections — they
        // belong to the layout, not to this section's content. Back the replaced
        // span off them so a replace preserves the separator instead of swallowing
        // it; they ride along in the verbatim tail.
        int contentEnd = end;
        while (contentEnd - 1 > matchLine && lines[contentEnd - 1].Trim().Length == 0)
            contentEnd--;

        var rebuilt = new List<string>(lines.Length);
        rebuilt.AddRange(lines[..(matchLine + 1)]);
        if (content.Length > 0) rebuilt.AddRange(content.Split('\n'));
        rebuilt.AddRange(lines[contentEnd..]);

        return new(EditStatus.Replaced, string.Join('\n', rebuilt), 1);
    }

    // Read-after-write "intent" check against the re-read body.
    public static bool SectionContentMatches(string body, string heading, string intendedContent)
    {
        if (!TryReadSection(body, heading, out string actual)) return false;
        return NormalizeBlock(actual).Equals(NormalizeBlock(intendedContent), StringComparison.Ordinal);
    }

    // Normalized heading texts in document order, for the section-set guard: an
    // edit must not drop any heading that existed before it.
    public static IReadOnlyList<string> HeadingTexts(string body)
    {
        var list = new List<string>();
        foreach (string line in body.Split('\n'))
            if (TryHeading(line, out _, out string text))
                list.Add(NormalizeHeading(text));
        return list;
    }

    // ── reads ─────────────────────────────────────────────────────────────────
    //
    // The typed reads project a body instead of returning it whole: a section
    // under a known heading, the checklist items, the outline. They share the
    // heading grammar above so a section a read finds is the one replace_section
    // will edit.

    // Content under the first heading whose text equals `heading` (normalized,
    // case-insensitive), raw as Anytype returned it. False when absent.
    public static bool TryReadSection(string body, string heading, out string content)
    {
        content = "";
        string target = NormalizeHeading(heading);
        string[] lines = body.Split('\n');

        int start = -1, level = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (TryHeading(lines[i], out int l, out string text)
                && NormalizeHeading(text).Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                start = i; level = l; break;
            }
        }
        if (start < 0) return false;

        int end = SectionEnd(lines, start, level);
        content = string.Join('\n', lines[(start + 1)..end]);
        return true;
    }

    // The lines before the first heading (the whole body when it has none).
    public static string Preamble(string body)
    {
        string[] lines = body.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (TryHeading(lines[i], out _, out _))
                return string.Join('\n', lines[..i]);
        return body;
    }

    public readonly record struct Section(int Level, string Heading, int ContentChars);

    // Every heading in document order with the size of the content under it, so
    // a reader can pick a section before paying for its text.
    public static IReadOnlyList<Section> Outline(string body)
    {
        var list = new List<Section>();
        string[] lines = body.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!TryHeading(lines[i], out int level, out string text)) continue;
            int end = SectionEnd(lines, i, level);
            int chars = Clean(string.Join('\n', lines[(i + 1)..end])).Length;
            list.Add(new Section(level, NormalizeHeading(text), chars));
        }
        return list;
    }

    public readonly record struct ChecklistItem(bool Done, string Label);

    // GFM checklist items in document order, labels unescaped. An item with an
    // empty label (the task template ships one) is skipped: it is a slot, not a
    // subtask.
    public static IReadOnlyList<ChecklistItem> ChecklistItems(string body)
    {
        var items = new List<ChecklistItem>();
        foreach (string line in body.Split('\n'))
        {
            if (!TryChecklistLine(line, out int box, out string label) || label.Length == 0) continue;
            items.Add(new ChecklistItem(line[box] != ' ', Unescape(label)));
        }
        return items;
    }

    // A GFM checklist line: optional indent, a list marker, `[ ]`/`[x]`/`[X]`,
    // then the label. `boxIndex` is the position of the box character so a
    // caller can rewrite it in place.
    public static bool TryChecklistLine(string line, out int boxIndex, out string label)
    {
        boxIndex = -1;
        label = "";

        int p = 0;
        while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;

        if (p >= line.Length || (line[p] != '-' && line[p] != '*' && line[p] != '+'))
            return false;
        p++;

        if (p >= line.Length || line[p] != ' ') return false;
        p++;

        if (p + 2 >= line.Length || line[p] != '[' || line[p + 2] != ']')
            return false;

        char box = line[p + 1];
        if (box != ' ' && box != 'x' && box != 'X') return false;

        boxIndex = p + 1;
        label = line[(p + 3)..].Trim();
        return true;
    }

    // Body text as a reader wants it: Anytype's escapes undone, trailing
    // hard-break spaces dropped, `---` dividers and blank runs removed. The
    // dividers are layout the template ships, not content.
    public static string Clean(string text)
    {
        var kept = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = Unescape(raw).TrimEnd();
            // The v2 render spells the divider « --- » with a space on each
            // side (measured 2026-10-22), v1 bare.
            if (line.Length == 0 || line.Trim() == "---") continue;
            kept.Add(line);
        }
        return string.Join('\n', kept);
    }

    // The v2 markdown render opens with the object's name as a `# ` line
    // (measured 2026-10-22 on a task; a report has no name and opens on its own
    // first heading block, which stays). That line is the title, not body: it
    // is dropped when it spells `name`, leaving the body as the author wrote it.
    // Whether v1 ever emitted it is not established; dropping it is harmless
    // either way.
    public static string DropTitle(string body, string name)
    {
        string wanted = NormalizeHeading(name);
        if (wanted.Length == 0) return body;

        string[] lines = body.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            if (TryHeading(lines[i], out int level, out string text)
                && level == 1
                && NormalizeHeading(text).Equals(wanted, StringComparison.Ordinal))
                return string.Join('\n', lines[(i + 1)..]);
            break;
        }
        return body;
    }

    // session_log writes each journal entry as two paragraphs: this stamp, then
    // the text. JournalEntries finds the entries by it.
    public const string JournalStampFormat = "yyyy-MM-dd HH:mm";

    // The journal entries of a report body, oldest first, so a reader keeps the
    // last N entries rather than the last N lines. An entry is a stamp and the
    // lines after it up to the next stamp, read back as "stamp · first line"
    // with any further lines below. Before the first stamp, each line is an
    // entry of its own: reports written before the stamped form carry one
    // bullet per entry. The « # Journal » title is not an entry.
    public static IReadOnlyList<string> JournalEntries(string body)
    {
        var entries = new List<string>();
        StringBuilder? stamped = null;
        bool hasText = false;
        foreach (string line in Clean(body).Split('\n'))
        {
            if (line.Length == 0 || line.StartsWith("# ", StringComparison.Ordinal)) continue;
            if (IsJournalStamp(line))
            {
                if (stamped is not null) entries.Add(stamped.ToString());
                stamped = new StringBuilder(line);
                hasText = false;
            }
            else if (stamped is null)
            {
                entries.Add(line);
            }
            else
            {
                stamped.Append(hasText ? "\n" : " · ").Append(line);
                hasText = true;
            }
        }
        if (stamped is not null) entries.Add(stamped.ToString());
        return entries;
    }

    // ── internals ─────────────────────────────────────────────────────────────

    static bool IsJournalStamp(string line) =>
        line.Length == JournalStampFormat.Length
        && DateTime.TryParseExact(
            line, JournalStampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    static int SectionEnd(string[] lines, int headingLine, int level)
    {
        for (int i = headingLine + 1; i < lines.Length; i++)
            if (TryHeading(lines[i], out int l, out _) && l <= level) return i;
        return lines.Length;
    }

    // A CommonMark ATX heading; "#text" (no space) is not a heading.
    static bool TryHeading(string line, out int level, out string text)
    {
        level = 0; text = "";

        int p = 0;
        while (p < line.Length && p < 3 && line[p] == ' ') p++;

        int hashes = 0;
        while (p < line.Length && line[p] == '#') { hashes++; p++; }
        if (hashes is < 1 or > 6) return false;
        if (p < line.Length && line[p] != ' ') return false;

        level = hashes;
        text = line[p..].Trim();
        return true;
    }

    static string NormalizeHeading(string s) => Unescape(s).Trim();

    static string NormalizeBlock(string s)
    {
        var kept = new List<string>();
        foreach (string raw in s.Split('\n'))
        {
            string line = Unescape(raw).TrimEnd();
            if (line.Length > 0) kept.Add(line);
        }
        return string.Join('\n', kept);
    }

    public static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] is '_' or '*' or '`' or '|' or '\\')
            {
                sb.Append(s[i + 1]);
                i++;
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }
}
