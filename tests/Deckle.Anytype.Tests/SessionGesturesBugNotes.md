# Session gesture bug notes

## The first journal append replaced the report title with the logged line

- **Trigger:** `log` was called on a report freshly created by `session_start` (creation body `# Journal <date>`, rapport type, note layout).
- **Observed symptom:** The report's display name changed from « Journal <date> » to the full text of the first logged line, and the second logged line rendered as a bullet nested under that first line instead of a sibling entry.
- **Cause:** Anytype consumes the creation body's first line as the note's title: a GET right after creation returns an EMPTY `markdown` (the heading is not part of it — measured live 2026-08-10 on the PM space). `session_log` read that empty body and PATCHed the entry alone as the full body; a note's display name is its first body line, so the entry became the title. The next append then landed under a plain paragraph rather than a heading.
- **Violated invariant:** A journal append must leave the report's title line intact; entries are sibling bullets under the « # Journal <date> » heading.
- **Recurrence cue:** A journal report whose display name is a logged sentence instead of « Journal <date> », or an append path that writes a body whose first line is an entry. Watch any change to the creation body shape or to how the note title is derived — the empty-markdown-after-creation behavior is Anytype's, not ours, and may shift with backend versions.

Not tied to the stateless-mode rebuild: reports corrupted this way exist from
2026-07-03 onward, well before the rebuild — every first append into a fresh
report hit it; intact « Journal <date> » reports are the ones never logged into.

Regression coverage: `SessionGesturesTests.LogOnAFreshReportKeepsTheJournalHeadingAsTheFirstLine`.

## A failed digest read left an orphan report that a retry duplicated

- **Trigger:** `session_start` created the report, then a read of the digest (the task's project, the search of its past reports, a past report's body) failed.
- **Observed symptom:** the caller got an error and never received the report id, yet the report existed on the task. Retrying the gesture created a second one. Established by reproduction with a refused search; a failing digest read on the live backend was not observed.
- **Cause:** the report was created before the digest was built, so every digest read was a way to fail after the write. The id the caller needs travels in the digest's first line, so it was lost with the failure.
- **Violated invariant:** a gesture that can be retried makes its write its last remote call, so a failure leaves nothing to duplicate.
- **Recurrence cue:** a create followed by reads whose failure the caller sees as "nothing happened".

The digest is now built first, so it cannot contain the new report; the filter that skipped it, and the extra search slot it needed, are gone. Reports another session opened on the same task in between are history and are shown.

Regression: `SessionGesturesTests.StartCreatesNoReportWhenTheDigestCannotBeRead`.