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
