using System.Text;

namespace Deckle.Anytype.Gestures;

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
// Round-trip facts this is built against (measured live, see JOURNAL): Anytype
// destroys-and-recreates blocks on every PATCH then re-exports markdown, so the
// read-back is a NORMALIZED render, not the bytes sent. Heading text and level
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

        // Splice: head lines (through the heading) + new content + tail (the kept
        // separator and everything from the next same-or-higher heading on). Head
        // and tail are copied verbatim, so every other section returns to Anytype
        // byte-for-byte as it arrived.
        var rebuilt = new List<string>(lines.Length);
        rebuilt.AddRange(lines[..(matchLine + 1)]);
        if (content.Length > 0) rebuilt.AddRange(content.Split('\n'));
        rebuilt.AddRange(lines[contentEnd..]);

        return new(EditStatus.Replaced, string.Join('\n', rebuilt), 1);
    }

    // Read-after-write "intent" check: the re-read body still carries the section
    // and its normalized content equals the normalized intent. Absent heading or
    // any content drift → false.
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

    // ── internals ─────────────────────────────────────────────────────────────

    // Content under the first heading matching `heading`, or false when absent.
    static bool TryReadSection(string body, string heading, out string content)
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

    // First line after `headingLine` that is a heading of level ≤ `level`
    // (exclusive end of the section), or the end of the document.
    static int SectionEnd(string[] lines, int headingLine, int level)
    {
        for (int i = headingLine + 1; i < lines.Length; i++)
            if (TryHeading(lines[i], out int l, out _) && l <= level) return i;
        return lines.Length;
    }

    // Recognizes an ATX heading: up to 3 leading spaces, 1-6 '#', then a space or
    // end of line. level = '#' count, text = the trimmed remainder. "#text" (no
    // space) is not a heading.
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

    // Heading-text normalization for matching: unescape the four escaped literals
    // and trim. A heading carries no blank-line structure, so this is per-string.
    static string NormalizeHeading(string s) => Unescape(s).Trim();

    // Block normalization for content comparison: unescape, drop each line's
    // trailing whitespace, and drop blank lines — so Anytype's hard-break and
    // blank-line reflow does not read as content drift. Order and the non-empty
    // text are what the intent check is about.
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

    // Undo Anytype's export escaping of the literal _ * ` | (and the backslash
    // itself). A backslash before one of those drops; every other backslash stays.
    static string Unescape(string s)
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
