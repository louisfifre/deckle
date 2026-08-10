# Operation recovery bug notes

## Delete confirmation did not enforce its preview handle

- **Trigger:** `delete` was called with `confirm:true` and a display name instead of the id returned by its preview.
- **Observed symptom:** The gesture resolved the name and moved that object to the bin, bypassing the advertised pinned-id confirmation contract.
- **Cause:** Preview returned a stable provider id, but the confirmed path reused the general name-or-id resolver without checking the input coordinate.
- **Violated invariant:** A destructive confirmation must commit only against the exact provider id reviewed by the preceding preview.
- **Recurrence cue:** A confirmed destructive gesture accepts a name, fuzzy selector, or newly resolved target instead of its preview handle.

Regression coverage: `ManagementGesturesTests.ConfirmedCallRejectsANameInsteadOfThePreviewedIdWithoutDeleting`.

## Replayable mutations accepted mutable display names

- **Trigger:** `update` renamed an object, or `log` / `dialogue_post` appended through a display-name selector, and the response became ambiguous.
- **Observed symptom:** Replaying the same arguments could fail to find the renamed target or could resolve a different report or chat with the same display name.
- **Cause:** The gestures exposed name-or-id lookup even though their recovery contracts depend on provider identity returned by an earlier read or create.
- **Violated invariant:** A replayable mutation or non-idempotent append must address its target by a stable Anytype object id.
- **Recurrence cue:** A mutating gesture classified for stable-target recovery calls `NameResolver` on a display name.

Regression coverage: `QueryGesturesTests.UpdateRequiresAStableObjectIdBeforeReadingOrWriting`,
`SessionGesturesTests.LogRequiresTheReportIdBeforeReadingOrWriting`, and
`DialogueGesturesTests.PostRequiresTheChatIdBeforeAppendingAMessage`.
