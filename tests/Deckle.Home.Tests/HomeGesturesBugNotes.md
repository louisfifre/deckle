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
