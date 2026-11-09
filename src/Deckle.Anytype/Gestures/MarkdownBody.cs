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
        var headings = ScanHeadings(lines);

        int matchLine = -1, matchLevel = 0, matchCount = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (headings[i] is not (int level, string text)) continue;
            if (!NormalizeHeading(text).Equals(target, StringComparison.OrdinalIgnoreCase)) continue;

            matchCount++;
            if (matchLine < 0) { matchLine = i; matchLevel = level; }
        }

        if (matchCount == 0) return new(EditStatus.NotFound, body, 0);
        if (matchCount > 1) return new(EditStatus.Ambiguous, body, matchCount);

        int end = SectionEnd(headings, matchLine, matchLevel);

        // The blank line(s) before the next heading separate the two sections — they
        // belong to the layout, not to this section's content. Back the replaced
        // span off them so a replace preserves the separator instead of swallowing
        // it; they ride along in the verbatim tail.
        int contentEnd = end;
        while (contentEnd - 1 > matchLine && lines[contentEnd - 1].Trim().Length == 0)
            contentEnd--;

        var rebuilt = new List<string>(lines.Length);
        rebuilt.AddRange(lines[..(matchLine + 1)]);
        if (content.Length > 0)
        {
            // The body keeps its own line ending: a CRLF body gets CRLF content,
            // so a splice never leaves a document with both. Only the lines that
            // a later line follows carry the CR; a last line with nothing after
            // it ends the document and gets none.
            string[] added = content.Replace("\r\n", "\n").Split('\n');
            bool crlf = body.Contains("\r\n", StringComparison.Ordinal);
            for (int i = 0; i < added.Length; i++)
            {
                bool followed = i < added.Length - 1 || contentEnd < lines.Length;
                rebuilt.Add(crlf && followed ? added[i] + "\r" : added[i]);
            }
        }
        rebuilt.AddRange(lines[contentEnd..]);

        return new(EditStatus.Replaced, string.Join('\n', rebuilt), 1);
    }

    // Read-after-write "intent" check against the re-read body. List
    // indentation is compared by structure, not by spaces: the render indents
    // a child by four spaces under a numbered item and two under a checkbox
    // (measured 2026-11-09), whatever was sent.
    public static bool SectionContentMatches(string body, string heading, string intendedContent)
    {
        if (!TryReadSection(body, heading, out string actual)) return false;
        return NormalizeBlock(ReindentLists(actual))
            .Equals(NormalizeBlock(ReindentLists(intendedContent)), StringComparison.Ordinal);
    }

    // ── writes ────────────────────────────────────────────────────────────────
    //
    // Markdown a caller wrote naturally, made safe for the server's parser
    // before an insert. Two faults, both measured on 2026-11-09 against the
    // backend that serves Deckle:
    //
    //   • A nested list item indented past its parent's content column shifts
    //     one level down: under « - un », « ····- un.a » then « ····- un.b »
    //     made un.b a child of un.a. Two spaces, the content column of « - »,
    //     kept them siblings. A caller copying a read gets four spaces, since
    //     the render indents that way, so the items are re-indented to their
    //     parent's content column.
    //   • An underscore at the edge of a word opens or closes emphasis: « _slot
    //     et slot_ » came back in italics and « __init__ » in bold, while
    //     « due_date » inside a word stayed. Keys are what an agent writes with
    //     underscores, so an underscore at a word edge is escaped; « \_ » is
    //     rendered as a literal underscore (measured). Emphasis is written with
    //     « * », which is left alone.
    //
    // The list shift is the server's: CommonMark makes un.a and un.b siblings
    // there (both sit past un's content column, neither past un.a's), so it is
    // a defect of the backend's markdown import, worth reporting upstream. The
    // underscore emphasis is CommonMark itself, not a defect: the escape is ours
    // to write.
    public static string PrepareForWrite(string markdown) =>
        EscapeUnderscores(ReindentLists(markdown.Replace("\r\n", "\n")));

    // Each list item gets the indentation its nesting calls for: none at the
    // top, its parent's content column below (the marker's width plus one
    // space: 2 under « - », 3 under « 1. »). The nesting is read the CommonMark
    // way from the original indentation: an item that reaches its parent's
    // content column is its child, one that falls short goes back up a level.
    // A line that is not an item and starts at the margin ends the list, and so
    // does a thematic break (« * * * », « --- »). Left as written: code fences,
    // and an item indented four columns or more outside any list, which
    // CommonMark reads as an indented code block.
    public static string ReindentLists(string markdown)
    {
        string[] lines = markdown.Split('\n');
        bool[] fenced = FenceMask(lines);
        // Per open item: its content column as written, and as rewritten.
        var stack = new List<(int Written, int Rewritten)>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (fenced[i]) continue;
            string line = lines[i];
            if (line.Trim().Length == 0) continue;

            int indent = IndentWidth(line, out int textStart);
            int marker = IsThematicBreak(line) ? 0 : ListMarkerLength(line, textStart);
            if (marker == 0)
            {
                if (indent == 0) stack.Clear();
                continue;
            }

            while (stack.Count > 0 && indent < stack[^1].Written) stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0 && indent >= 4) continue;
            int column = stack.Count == 0 ? 0 : stack[^1].Rewritten;
            stack.Add((indent + marker + 1, column + marker + 1));
            lines[i] = new string(' ', column) + line[textStart..];
        }
        return string.Join('\n', lines);
    }

    // Three or more of the same « - », « * » or « _ », spaces allowed between.
    static bool IsThematicBreak(string line)
    {
        string bare = line.Replace(" ", "").Replace("\t", "");
        return bare.Length >= 3 && bare[0] is '-' or '*' or '_' && bare.All(c => c == bare[0]);
    }

    // Escapes each underscore at a word edge, the only place it can open or
    // close emphasis: inside a word (« due_date ») it is literal already, and
    // left alone so a read gives back what was written. Never touched: code
    // (fences and inline spans), an underscore escaped already, an autolink
    // « <…> », a bare web address and a link's destination « ](…) », where a
    // backslash would land in the address.
    public static string EscapeUnderscores(string markdown)
    {
        string[] lines = markdown.Split('\n');
        bool[] fenced = FenceMask(lines);
        for (int i = 0; i < lines.Length; i++)
        {
            if (fenced[i] || lines[i].IndexOf('_') < 0) continue;

            string line = lines[i];
            var sb = new StringBuilder(line.Length + 8);
            int p = 0;
            while (p < line.Length)
            {
                char c = line[p];
                if (c == '\\' && p + 1 < line.Length)
                {
                    sb.Append(c).Append(line[p + 1]);
                    p += 2;
                    continue;
                }
                if (c == '`')
                {
                    // An inline code span runs to the next backtick run of the
                    // same length; without one, the backticks are literal.
                    int run = 1;
                    while (p + run < line.Length && line[p + run] == '`') run++;
                    int close = line.IndexOf(new string('`', run), p + run, StringComparison.Ordinal);
                    int end = close < 0 ? p + run : close + run;
                    sb.Append(line, p, end - p);
                    p = end;
                    continue;
                }
                if (AddressEnd(line, p) is int addressEnd)
                {
                    sb.Append(line, p, addressEnd - p);
                    p = addressEnd;
                    continue;
                }
                if (c == '_' && !InsideWord(line, p)) sb.Append('\\');
                sb.Append(c);
                p++;
            }
            lines[i] = sb.ToString();
        }
        return string.Join('\n', lines);
    }

    static bool InsideWord(string line, int p) =>
        p > 0 && p + 1 < line.Length && char.IsLetterOrDigit(line[p - 1]) && char.IsLetterOrDigit(line[p + 1]);

    // Where an address starting at `p` ends, or null when none starts there:
    // an autolink « <scheme:…> », a link destination « ](…) » (its « ]( »
    // included), or a bare web address « http://… », « https://… », « www.… »
    // that runs to the next space.
    static int? AddressEnd(string line, int p)
    {
        char c = line[p];
        if (c == '<')
        {
            int close = line.IndexOf('>', p + 1);
            if (close < 0) return null;
            ReadOnlySpan<char> inner = line.AsSpan(p + 1, close - p - 1);
            int colon = inner.IndexOf(':');
            bool scheme = colon >= 2 && char.IsAsciiLetter(inner[0])
                && inner[..colon].IndexOfAnyExcept(SchemeChars) < 0;
            return scheme && inner.IndexOfAny(' ', '<') < 0 ? close + 1 : null;
        }
        if (c == ']' && p + 1 < line.Length && line[p + 1] == '(')
        {
            int depth = 0;
            for (int q = p + 1; q < line.Length; q++)
            {
                if (line[q] == '(') depth++;
                else if (line[q] == ')' && --depth == 0) return q + 1;
            }
            return line.Length;
        }
        if ((p == 0 || !char.IsLetterOrDigit(line[p - 1]))
            && (StartsAt(line, p, "http://") || StartsAt(line, p, "https://") || StartsAt(line, p, "www.")))
        {
            int end = p;
            while (end < line.Length && !char.IsWhiteSpace(line[end])) end++;
            return end;
        }
        return null;
    }

    static readonly System.Buffers.SearchValues<char> SchemeChars =
        System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+.-");

    static bool StartsAt(string line, int p, string prefix) =>
        string.Compare(line, p, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0;

    // Leading width, a tab counting four columns, and where the text starts.
    static int IndentWidth(string line, out int textStart)
    {
        int width = 0;
        textStart = 0;
        while (textStart < line.Length && line[textStart] is ' ' or '\t')
        {
            width += line[textStart] == '\t' ? 4 : 1;
            textStart++;
        }
        return width;
    }

    // The width of a list marker at `start` (« - », « * », « + », « 12. »,
    // « 3) ») when a space follows it, else 0.
    static int ListMarkerLength(string line, int start)
    {
        if (start >= line.Length) return 0;
        int end = start;
        if (line[end] is '-' or '*' or '+') end++;
        else
        {
            while (end < line.Length && end - start < 9 && char.IsAsciiDigit(line[end])) end++;
            if (end == start || end >= line.Length || line[end] is not ('.' or ')')) return 0;
            end++;
        }
        return end < line.Length && line[end] == ' ' ? end - start : 0;
    }

    // Normalized heading texts in document order, for the section-set guard: an
    // edit must not drop any heading that existed before it.
    public static IReadOnlyList<string> HeadingTexts(string body)
    {
        var list = new List<string>();
        foreach (var heading in ScanHeadings(body.Split('\n')))
            if (heading is (_, string text))
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
        var headings = ScanHeadings(lines);

        int start = -1, level = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (headings[i] is (int l, string text)
                && NormalizeHeading(text).Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                start = i; level = l; break;
            }
        }
        if (start < 0) return false;

        int end = SectionEnd(headings, start, level);
        content = string.Join('\n', lines[(start + 1)..end]);
        return true;
    }

    // The lines before the first heading (the whole body when it has none).
    public static string Preamble(string body)
    {
        string[] lines = body.Split('\n');
        var headings = ScanHeadings(lines);
        for (int i = 0; i < lines.Length; i++)
            if (headings[i] is not null)
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
        var headings = ScanHeadings(lines);
        for (int i = 0; i < lines.Length; i++)
        {
            if (headings[i] is not (int level, string text)) continue;
            int end = SectionEnd(headings, i, level);
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
        string[] lines = body.Split('\n');
        bool[] fenced = FenceMask(lines);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (fenced[i] || !TryChecklistLine(line, out int box, out string label) || label.Length == 0) continue;
            items.Add(new ChecklistItem(line[box] != ' ', Unescape(label)));
        }
        return items;
    }

    // The text without its checklist lines, for a reader that has the checklist
    // from elsewhere. A `- [ ]` line inside a code fence is code, so it stays.
    public static string WithoutChecklistLines(string text)
    {
        string[] lines = text.Split('\n');
        bool[] fenced = FenceMask(lines);
        var kept = new List<string>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
            if (fenced[i] || !TryChecklistLine(lines[i], out _, out _))
                kept.Add(lines[i]);
        return string.Join('\n', kept);
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

    // The first line a reader would see, cleaned, with a leading heading marker
    // stripped. Empty when the body has no text. Names an object that has none
    // (an idea or a report is titled by what it says first).
    public static string FirstLine(string text)
    {
        string clean = Clean(text);
        if (clean.Length == 0) return "";
        string line = clean.Split('\n')[0].Trim();
        return line.TrimStart('#').Trim();
    }

    // The v2 markdown render opens with the object's name as a `# ` line
    // (measured 2026-10-22 on a task; a report has no name and opens on its own
    // first heading block, which stays). That line is the title, not body: it
    // is dropped when it spells `name`, leaving the body as the author wrote it.
    // An idea opens instead on a plain line holding its name, which Anytype
    // derives from that first block (measured 2026-10-22): dropped the same way.
    // Whether v1 ever emitted either is not established; dropping is harmless.
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
            if (!TryHeading(lines[i], out _, out _)
                && NormalizeHeading(lines[i]).Equals(wanted, StringComparison.Ordinal))
                return string.Join('\n', lines[(i + 1)..]);
            break;
        }
        return body;
    }

    // A nameless idea's v2 render opens with its snippet before the body: the
    // body's first lines as paragraphs, the last cut with « … » (measured
    // 2026-10-28; a nameless report opened on its own body). That header only
    // repeats the body, so it is dropped, but only when the lines after it do
    // start with what it says: a body that merely ends a line with « … » stays
    // whole. Whether a short, uncut snippet is rendered too is not established.
    public static string DropSnippetHeader(string body)
    {
        string[] lines = body.Split('\n');
        var header = new List<string>();
        int end = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            header.Add(line);
            if (line.EndsWith('…')) { end = i; break; }
        }
        if (end < 0) return body;

        List<string> rest = lines[(end + 1)..]
            .Select(l => l.Trim()).Where(l => l.Length > 0).Take(header.Count).ToList();
        if (rest.Count < header.Count) return body;
        for (int j = 0; j < header.Count - 1; j++)
            if (!rest[j].Equals(header[j], StringComparison.Ordinal)) return body;
        if (!rest[^1].StartsWith(header[^1].TrimEnd('…'), StringComparison.Ordinal)) return body;

        return string.Join('\n', lines[(end + 1)..]);
    }

    // session_log writes each journal entry as two paragraphs: this stamp, then
    // the text. JournalEntries finds the entries by it.
    public const string JournalStampFormat = "yyyy-MM-dd HH:mm";

    // The journal entries of a report body, oldest first, so a reader keeps the
    // last N entries rather than the last N lines. An entry is a stamp and the
    // lines after it up to the next stamp, read back as "stamp · first line"
    // with any further lines below. Before the first stamp, each line is an
    // entry of its own: reports written before the stamped form carry one
    // bullet per entry. The « # Journal » title is not an entry, but only that
    // line: an entry whose text opens with « # » (a quoted shell comment, a
    // markdown title) reads back as a heading line and stays an entry. The
    // title is always written first, so it can only precede the first stamp.
    public static IReadOnlyList<string> JournalEntries(string body)
    {
        var entries = new List<string>();
        StringBuilder? stamped = null;
        bool hasText = false;
        foreach (string line in Clean(body).Split('\n'))
        {
            if (line.Length == 0 || (stamped is null && IsJournalTitle(line))) continue;
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


    static bool IsJournalTitle(string line) =>
        line.Equals("# Journal", StringComparison.Ordinal)
        || line.StartsWith("# Journal ", StringComparison.Ordinal);

    static bool IsJournalStamp(string line) =>
        line.Length == JournalStampFormat.Length
        && DateTime.TryParseExact(
            line, JournalStampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    static int SectionEnd((int Level, string Text)?[] headings, int headingLine, int level)
    {
        for (int i = headingLine + 1; i < headings.Length; i++)
            if (headings[i] is (int l, _) && l <= level) return i;
        return headings.Length;
    }

    // The heading of each line, or null when it is none. A line inside a code
    // fence is code, whatever it starts with: a `# comment` in a shell block is
    // not a heading, so it neither opens nor closes a section.
    static (int Level, string Text)?[] ScanHeadings(string[] lines)
    {
        bool[] fenced = FenceMask(lines);
        var headings = new (int Level, string Text)?[lines.Length];
        for (int i = 0; i < lines.Length; i++)
            if (!fenced[i] && TryHeading(lines[i], out int level, out string text))
                headings[i] = (level, text);
        return headings;
    }

    // Marks every line that belongs to a fenced code block, fences included. A
    // fence is three or more backticks or tildes after at most three spaces; it
    // closes on a run of the same character at least as long with nothing after
    // it, and an unclosed fence runs to the end of the document (CommonMark).
    // A backtick fence whose info string holds a backtick is inline code, not a
    // fence.
    static bool[] FenceMask(string[] lines)
    {
        var mask = new bool[lines.Length];
        char fence = '\0';
        int fenceLength = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            int p = 0;
            while (p < line.Length && p < 3 && line[p] == ' ') p++;
            int run = 0;
            char c = p < line.Length ? line[p] : '\0';
            if (c is '`' or '~')
                while (p + run < line.Length && line[p + run] == c) run++;

            if (fence == '\0')
            {
                if (run >= 3 && !(c == '`' && line.IndexOf('`', p + run) >= 0))
                {
                    fence = c;
                    fenceLength = run;
                    mask[i] = true;
                }
                continue;
            }

            mask[i] = true;
            if (c == fence && run >= fenceLength && line[(p + run)..].Trim().Length == 0)
                fence = '\0';
        }
        return mask;
    }

    // A CommonMark ATX heading; "#text" (no space) is not a heading. An optional
    // closing run of `#` (« ## Title ## ») is not part of the text, but a `#`
    // glued to the last word (« ## C# ») is.
    static bool TryHeading(string line, out int level, out string text)
    {
        level = 0; text = "";

        int p = 0;
        while (p < line.Length && p < 3 && line[p] == ' ') p++;

        int hashes = 0;
        while (p < line.Length && line[p] == '#') { hashes++; p++; }
        if (hashes is < 1 or > 6) return false;
        if (p < line.Length && line[p] != ' ' && line[p] != '\r') return false;

        level = hashes;
        text = StripClosingHashes(line[p..].Trim());
        return true;
    }

    static string StripClosingHashes(string text)
    {
        int end = text.Length;
        while (end > 0 && text[end - 1] == '#') end--;
        if (end == text.Length) return text;
        if (end == 0) return "";
        return text[end - 1] is ' ' or '\t' ? text[..end].TrimEnd() : text;
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
