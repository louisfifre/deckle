# Task gesture bug notes

## An omitted subtask state changed meaning on replay

- **Trigger:** `subtask` was called without `done` for a label that was not yet present, then the same call was replayed after its first response became ambiguous.
- **Observed symptom:** The first call appended an unchecked item, while the replay matched that new item and checked it. One logical request therefore produced two different states depending on whether the response was received.
- **Cause:** The tool advertised an optional toggle, but `TaskGestures` implemented two implicit defaults: a missing item was created unchecked and a matching item was forced checked. The request did not carry the state needed to make both paths equivalent.
- **Violated invariant:** A checklist mutation must state the exact target state. Replaying the same request after an ambiguous response must leave one matching item in that state.
- **Recurrence cue:** A `subtask` schema makes `done` optional, uses toggle wording, or an absent-item path chooses a different checkbox state from the matching-item path.

Regression coverage: `ToolCatalogTests.SubtaskWithoutDoneIsRejectedBeforeAnytypeIo`.
`TaskGesturesTests.SubtaskReplayKeepsOneItemInTheRequestedState` separately covers
the resulting exact-state behavior but does not reproduce the old omitted-argument trigger.

## A partial or empty checklist label selected the wrong item

- **Trigger:** `subtask` received an empty label, or a partial label shared by more than one checklist item.
- **Observed symptom:** An empty label matched the first checklist item because every string contains the empty string; multiple partial matches silently changed the first one.
- **Cause:** The gesture accepted every string and returned on the first case-insensitive contains-match without checking that the target was unique.
- **Violated invariant:** A body mutation must identify one intended checklist item before writing; invalid or ambiguous labels must leave the body untouched.
- **Recurrence cue:** Checklist matching accepts whitespace-only text or stops at the first match without counting the candidates.

Regression coverage: `TaskGesturesTests.SubtaskRejectsABlankLabelBeforeReadingOrWriting`
and `TaskGesturesTests.SubtaskRefusesAnAmbiguousPartialLabelWithoutWriting`.

## A body passed to create_task landed under the template's last section

- **Trigger:** `create_task` was called with a `body` holding `- [ ]` subtasks, on a type whose template body already carries `## Sous-tâches` and `## Details`.
- **Observed symptom:** The subtasks appeared below Details, after the template's own empty checklist slot, so `task_checklist` and `subtask` saw an empty checklist while the notes section carried the items.
- **Cause:** Anytype's POST appends the `body` field after the template body instead of merging it. The gesture passed the caller's markdown straight through and never addressed the template's sections.
- **Violated invariant:** A task born from the template keeps its sections in the template's order, subtasks under the checklist heading and notes under the notes heading.
- **Recurrence cue:** A create gesture sends a `body` on POST for a type that has a template, or a subtask gesture appends at the end of the body instead of inside the checklist section.

Regression coverage: `TaskGesturesTests.CreateWritesChecklistAndNotesIntoTheTemplateSections`,
`TaskGesturesTests.SubtaskFillsTheTemplatesEmptySlotBeforeAddingALine` and
`TaskGesturesTests.SubtaskWithNoMatchFollowsTheLastItemNotTheNotes`.

## A checklist label with an underscore never matched

- **Trigger:** `subtask` was called with a label typed naturally (`get_task_properties`) for an item Anytype serves escaped in the body (`get\_task\_properties`).
- **Observed symptom:** The item was never found; each call added a ticked duplicate, and the deployed build put it at the end of the body under the notes section. Seen on 2026-10-20 while ticking the subtasks of the typed-reads task: three labels, three duplicates each after retries.
- **Cause:** `ApplyChecklist` compared the raw body label with the caller's label. Anytype escapes markdown punctuation on export, so the two spellings of one label differ by backslashes.
- **Violated invariant:** A label matches the item a person reads, whatever escaping the transport adds.
- **Recurrence cue:** Any comparison of body text with caller text that skips `MarkdownBody.Unescape` on the body side, or a heading match that reads the raw line.

Regression coverage: `TaskGesturesTests.SubtaskMatchesAnEscapedLabelTypedNaturally`.

Observed alongside, cause not established: every PATCH of a body through the
deployed build multiplied the backslashes already present in untouched
sections (`\_` grew to twelve backslashes over seven writes) and turned a `~`
into a `~~` strikethrough. Whether the export or the import side adds the
escaping is not known; no test covers it yet.
