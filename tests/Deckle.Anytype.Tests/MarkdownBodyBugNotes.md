# Markdown body engine bug notes

## The section engine read code fences as document structure

- **Trigger:** a task, report or document body holding a fenced code block whose lines start with `#` (a shell comment) or with `- [ ]`.
- **Observed symptom:** `document_outline` listed the shell comment as a heading, `replace_section` on the enclosing section stopped at it and left the rest of the fence behind, and the comment or the `- [ ]` line counted as a subtask. Established by reading the code and by the regression tests failing on the old engine; no real body was measured.
- **Cause:** `TryHeading` and `TryChecklistLine` tested each raw line with no memory of an open fence.
- **Violated invariant:** a line inside a fenced code block is code; it is never a heading, a section boundary or a checklist item.
- **Recurrence cue:** a new reader of body lines (a section, an outline, a checklist, a notes view) that splits the body on `\n` and tests each line itself instead of going through `ScanHeadings` or `FenceMask`.

Regression: `MarkdownBodyTests.AHashLineInsideACodeFenceIsNotAHeading`, `ReplaceSectionCarriesAFenceAndItsHashLinesAlongWithTheSection`, `AChecklistLineInsideACodeFenceIsNotASubtask`.

## A splice mixed line endings and a closing hash broke the heading match

- **Trigger:** `replace_section` on a body whose lines end in CRLF, or on a heading written `## Title ##`.
- **Observed symptom:** the rewritten body held CRLF lines from the original and LF lines from the new content; the heading `## Title ##` could not be found by `Title`.
- **Cause:** the body was split on `\n` (keeping the `\r`) and the replacement was split on `\n` too, then everything joined with `\n`; `TryHeading` kept the closing run of `#` in the text.
- **Violated invariant:** a splice leaves the document with the line ending it had, and a heading's text excludes its optional closing hashes.
- **Recurrence cue:** a body-rewriting path that builds lines without looking at the ending of the lines it keeps. Uncertain: nothing shows that Anytype returns CRLF or closing-hash headings today; this is hardening, not a symptom seen in the space.

Regression: `MarkdownBodyTests.ReplaceSectionOfACrlfBodyKeepsEveryLineEndingCrlf`, `AHeadingWithClosingHashesIsFoundByItsText`.

## Journal entries opening with a hash disappeared on read

- **Trigger:** a `session_log` line starting with `# ` (a quoted shell comment, a markdown title), or such a line as a continuation of an entry.
- **Observed symptom:** the entry came back from `report_journal`, the `session_start` digest and the project overview without that line.
- **Cause:** `JournalEntries` skipped every line starting with `# ` to drop the `# Journal <date>` title, and Anytype renders the entry's first block back as a heading line.
- **Violated invariant:** only the report's title line is dropped from a journal read; the text of an entry is never filtered by what it starts with.
- **Recurrence cue:** a journal read that filters lines by a markdown prefix instead of by position (the title precedes the first stamp).

Regression: `MarkdownBodyTests.JournalEntriesKeepAnEntryWhoseTextOpensWithAHash`.

## Written markdown came back with shifted list items, stray emphasis and a failed check

- **Trigger:** `replace_section` (and the notes of `create_task`, a `log` line) given nested list items indented past their parent's content column, typically four spaces under `- ` as `document_section` renders them; or a key whose underscore sits at a word edge (`_slot`, `slot_`, `__init__`).
- **Observed symptom:** measured on 2026-11-09 against the live backend, on a scratch document: under `- un`, `····- un.a` then `····- un.b` came back with un.b a child of un.a; `_slot et slot_` came back in italics and `__init__` in bold, while `due_date` inside a word stayed literal; and every write holding a nested list or a `_` emphasis reported « vérification en échec » even when the write was right.
- **Cause:** the content went to the server's markdown parser as written. That parser shifts an item indented past its parent's content column (inferred from the measure: two spaces under `- ` and three under `1. ` nested correctly); an edge underscore is emphasis by CommonMark. The check compared the lines as spaced, and the render re-indents a child by four spaces under a numbered item and by two under a checkbox, whatever was sent.
- **Violated invariant:** what a caller writes naturally lands with the nesting it meant and with its underscores literal, and the check compares nesting by structure. The traps are commented at `MarkdownBody.PrepareForWrite` and `SectionContentMatches`.
- **Recurrence cue:** a new path that sends markdown to `insert_blocks` without `MarkdownBody.PrepareForWrite`, or a read-back check that compares list lines by their spaces.

- **Second pass (review of 2026-11-09):** escaping every underscore put a backslash into web addresses and autolinks, and the list rewrite took `* * *` for an item and a one-space indent for a child. Now only an underscore at a word edge is escaped, addresses are skipped, and nesting follows CommonMark's content-column rule. Only the list shift is a server defect (CommonMark keeps un.a and un.b siblings); the underscore emphasis is CommonMark itself.

Regression: `MarkdownBodyTests.ReindentListsPutsEachItemAtItsParentsContentColumn`, `EscapeUnderscoresKeepsEveryBareUnderscoreLiteral`, `SectionContentMatchesReadsListNestingByStructureNotBySpaces`, `QueryGesturesTests.ReplaceSectionKeepsNestedItemsAtTheirLevelAndUnderscoreKeysLiteral`. Not covered: an item followed by an indented continuation paragraph, which is left as written; an indented code block inside a list; a Windows path whose separator precedes an underscore (`\_` reads as an escape).

## A long idea showed its title twice

- **Trigger:** `create_idea` with a text longer than 80 characters.
- **Observed symptom:** the idea carried a name (its first words, cut with « … ») and a body holding the whole text, so its first words appeared once as the title and again at the head of the body. Reported in the review of 2026-11-09; established by reading the code, not re-measured.
- **Cause:** the gesture sent both a `name` and a `body`, while an idea is a note whose title is its first line.
- **Violated invariant:** an idea, like a report, is created with a body and no name.
- **Recurrence cue:** a creation of a note-layout type (idee, rapport) that sends a `name`. Uncertain: creating an idea on v2 with markdown and no name is measured on reports only.

Regression: `QueryGesturesTests.CreateIdeaSendsABodyAndNoName` pins the payload; whether the server then titles the idea by its first line is not measured.