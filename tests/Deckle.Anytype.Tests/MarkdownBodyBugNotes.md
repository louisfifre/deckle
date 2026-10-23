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