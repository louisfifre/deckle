using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Finds a section in a /v2 block document, pure and I/O-free: the block-level
// counterpart of MarkdownBody's line-level section search, so a section edit can
// be written as block ops instead of a whole-body rewrite.
//
// The document is flat and in reading order, each block with an integer indent
// (a column or a row nests its children at a greater indent; indent 2 is
// measured on the task template). A "section" is the heading block plus what
// follows it up to a boundary:
//
//   • the next heading block at the SAME indent whose level is the same or
//     higher (a smaller number), or
//   • the end of the container: the first block at a SMALLER indent, or the end
//     of the document.
//
// Headings at a greater indent do not end the section; they belong to it, as a
// deeper "###" belonged to a "##" section in markdown. Heading levels come from
// the block type heading_1..heading_N, and header_4 (the schema enum carries
// both spellings of the fourth level; which one the server emits is not
// measured, so both are read).
//
// Divider blocks sitting immediately before the boundary heading are layout
// BETWEEN sections, not content of the one above: they are kept out of the
// section. The markdown flow deleted them with the section (the task template's
// dividers were lost on the first replace). Dividers elsewhere in the section
// are content and go with it. At the end of a container there is no next
// section to separate, so a trailing divider there stays inside the section.
//
// What is returned as the section's roots are the blocks to DELETE: the blocks
// of the section whose parent is the heading or something outside the section.
// Deleting a root with recursive:true takes its descendants (the blocks that
// follow it at a greater indent), so a nested block is never named twice.
//
// Not measured: that a full read serves an indent on every block (the outline
// read does). A block without one is read as indent 0, which only blurs the
// nesting, never the heading search.
internal static class BlockSections
{
    internal enum Outcome { Found, NotFound, Ambiguous }

    // Headings: every heading of the document, unescaped and trimmed, for the
    // "not found" answer. HeadingId, RootIds and KeptHeadings are only
    // meaningful on Found; KeptHeadings are the headings that stay after the
    // replace (the matched one included), the reference for the guard.
    internal sealed record Located(
        Outcome Outcome,
        int MatchCount,
        IReadOnlyList<string> Headings,
        string HeadingId,
        IReadOnlyList<string> RootIds,
        IReadOnlyList<string> KeptHeadings);

    private readonly record struct Entry(string Id, string Type, string Text, int Indent, int Level);

    internal static Located Locate(JsonArray? blocks, string heading)
    {
        List<Entry> entries = Parse(blocks);
        string target = MarkdownBody.Unescape(heading).Trim();

        var headings = new List<string>();
        var matches = new List<int>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Level == 0) continue;
            headings.Add(entries[i].Text);
            if (entries[i].Text.Equals(target, StringComparison.OrdinalIgnoreCase)) matches.Add(i);
        }

        if (matches.Count == 0) return new(Outcome.NotFound, 0, headings, "", [], []);
        if (matches.Count > 1) return new(Outcome.Ambiguous, matches.Count, headings, "", [], []);

        int h = matches[0];
        Entry head = entries[h];

        int end = entries.Count;
        bool endsAtHeading = false;
        for (int j = h + 1; j < entries.Count; j++)
        {
            Entry e = entries[j];
            if (e.Indent < head.Indent) { end = j; break; }
            if (e.Indent == head.Indent && e.Level is > 0 && e.Level <= head.Level)
            {
                end = j;
                endsAtHeading = true;
                break;
            }
        }

        // Dividers right before the next heading stay where they are.
        int keepFrom = end;
        if (endsAtHeading)
        {
            while (keepFrom - 1 > h
                && entries[keepFrom - 1].Type == "divider"
                && entries[keepFrom - 1].Indent == head.Indent)
                keepFrom--;
        }

        // A root is a block whose nearest earlier block of smaller indent is the
        // heading itself (or, for a block at the heading's indent, lies before it).
        var roots = new List<string>();
        var ancestors = new Stack<int>();
        ancestors.Push(h);
        for (int j = h + 1; j < keepFrom; j++)
        {
            while (ancestors.Count > 0 && entries[ancestors.Peek()].Indent >= entries[j].Indent)
                ancestors.Pop();
            int parent = ancestors.Count > 0 ? ancestors.Peek() : -1;
            ancestors.Push(j);
            if (parent <= h) roots.Add(entries[j].Id);
        }

        var kept = new List<string>();
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Level > 0 && (i <= h || i >= keepFrom))
                kept.Add(entries[i].Text);

        return new(Outcome.Found, 1, headings, head.Id, roots, kept);
    }

    private static List<Entry> Parse(JsonArray? blocks)
    {
        var entries = new List<Entry>();
        foreach (JsonNode? node in blocks ?? [])
        {
            if (node is not JsonObject block || StringOf(block, "id") is not { Length: > 0 } id)
                continue;

            string type = StringOf(block, "type") ?? "";
            string text = MarkdownBody.Unescape(StringOf(block, "text") ?? "").Trim();
            int indent = block["indent"] is JsonValue v && v.TryGetValue(out int n) ? n : 0;
            entries.Add(new Entry(id, type, text, indent, HeadingLevel(type)));
        }
        return entries;
    }

    private static int HeadingLevel(string type)
    {
        if (type == "header_4") return 4;
        return type.StartsWith("heading_", StringComparison.Ordinal)
            && int.TryParse(type.AsSpan("heading_".Length), out int level)
            && level > 0
                ? level
                : 0;
    }

    private static string? StringOf(JsonObject block, string name)
        => block[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
