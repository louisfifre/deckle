# Home gesture bug notes

## Idea creation wrote short note text into a discarded name

- **Trigger:** Home created an `idea` whose text fit on one line within 80 characters.
- **Observed symptom:** a short idea kept only its properties and no text; a longer one duplicated a truncated pseudo-name before the complete body, while Home `get` hid that body by rendering only the list summary.
- **Cause:** Deckle sent short idea text only as `name` and long text as both `name` and `body`, although the managed `idea` type uses Anytype's note layout and notes have no name. Its `get` gesture also skipped the provider's detailed-object endpoint.
- **Violated invariant:** an idea is its body; every idea creation must write the complete text to `body`, while any Home display label is only an excerpt derived from the returned note snippet or markdown.
- **Recurrence cue:** idea creation branches on text length, sends a note `name`, idea resolution reads only the object's name, or `get` renders a list summary instead of the detailed object.

Regression: `HomeGesturesTests.CreatedIdeaCanBeFoundAndReadFromItsBodyExcerpt`.

## Home listings skipped rows when a page came back shorter than the limit

- **Trigger:** a Home gesture read the whole object inventory, a type's templates or a property's options while the server served fewer rows per page than the limit asked for. Established by reproduction against a fake server that caps its pages; whether the live API ever does is not measured, and the Dev-space search had the same shape (`fix(anytype): advance the search offset by the rows the server returned`).
- **Observed symptom:** `delete` removed an object that another object still referenced, and `create` accepted a code that already existed, because the referencing or duplicate object sat in rows the loop never read. A template or option past the first served page was reported unknown.
- **Cause:** each loop advanced its offset by the limit it had asked for (1000 for the inventory, 100 for templates and options). A page capped at 100 then left a hole of 900 rows before the next request. The templates loop also had no guard on an empty page, so a server that kept announcing `has_more` without serving rows kept it requesting forever.
- **Violated invariant:** a listing read is complete: the next offset is the number of rows already received, and an empty page ends the read whatever `has_more` claims.
- **Recurrence cue:** an `offset += <constant>` in a paging loop, or a loop that ends only on `has_more`.

Regression: `HomePagingTests.DeleteSeesAReferenceHeldBeyondTheFirstPageOfTheInventory`, `HomePagingTests.CreateRefusesACodeAlreadyHeldBeyondTheFirstPageOfTheInventory`, `HomePagingTests.TemplateLookupFindsATemplateBeyondTheFirstServedPage`, `HomePagingTests.TemplateLookupEndsWhenTheServerKeepsAnnouncingPagesItNeverServes` and `HomePagingTests.OptionLookupFindsALiveOptionBeyondTheFirstServedPage`.

## A Home batch that failed half way read as if nothing had been written

- **Trigger:** `create` or `update` with several items, or with collection membership, where one request fails after another went through: a refused create on the second item, a membership call refused after the objects were created, a patch refused on the second object.
- **Observed symptom:** the caller got only the raw API failure. The earlier items already existed or were already patched, and the membership of the created objects was never applied. A caller that repeated the batch duplicated the items that carry no code (errands, ideas, devices) and re-appended text that had already been appended. Established by reproduction with a fake server that refuses the Nth request; the live API failing mid-batch was not observed.
- **Cause:** Anytype offers no transaction. The gestures issue one request per item and per collection and let the first failure escape, so the exception said nothing of the writes before it. The collection memberships were also deferred to the very end of a create, so a failure on a later item skipped them for the objects that did exist.
- **Violated invariant:** a gesture that has written part of its batch says so: what was written, what was interrupted and what was never attempted, so the retry covers only the rest. A failure before any write still surfaces unchanged.
- **Recurrence cue:** a loop of independent writes with one `await` per item and no state kept about which ones went through; a "flush at the end" step after a loop that can throw.

Regression: `HomePartialWriteTests.CreateReportsWhatWasCreatedWhenALaterItemFails`, `HomePartialWriteTests.CreateStillAttachesTheObjectsItCreatedBeforeItFailed`, `HomePartialWriteTests.CreateTellsTheCallerWhenOnlyTheCollectionMembershipFailed`, `HomePartialWriteTests.UpdateReportsWhichItemsWereWrittenWhenALaterOneFails` and `HomePartialWriteTests.UpdateNamesTheInterruptedItemWhenItsCollectionStepFailsAfterItsWrite`. Nothing is rolled back: a delete would be a second write that can fail the same way.

## A Home write landed on an object that only resembled the name, and a Home delete never asked the person

- **Trigger:** a Home write (`update`, `complete`, `delete`, a relation target, a collection membership) received a part of a name that a single object contained; or `delete` was confirmed by id.
- **Observed symptom:** the write landed on the one object the part of the name pointed to, with no sign that it had been guessed. A confirmed `delete` moved the object to the bin on the model's say-so alone, where the Anytype delete also asks the person through a Windows notification. Established by reproduction against the fake server; no wrong write was seen on a real space.
- **Cause:** `HomeObjectIndex.Resolve` accepted a lone partial match for every caller, reads and writes alike. `HomeGestures.DeleteAsync` stopped at preview and confirm by id and did not use `IDeletionApproval`.
- **Violated invariant:** a write resolves a name only when it is given in full, as a code or as an id, and anything else is refused with the nearest objects by id; reads keep taking the one object a part of a name points to. A delete is let through only by the person's approval, and fails closed when it is refused, unanswered or impossible to ask. The reference fence runs again after the answer, because a link can appear while the notification waits.
- **Recurrence cue:** a Home write path that resolves with `NameMatch.Tolerant`; a delete that reaches `DeleteObjectAsync` without an `Approved` outcome; a fence checked only before the approval.

Regression: `HomeNameMatchTests` (the write refusals, the exact name among many that contain it, reads unchanged) and `HomeDeleteApprovalTests` (refused, unanswered, unavailable, timed out, the fence after the answer, the journal events without the object).