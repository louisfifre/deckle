# Home gesture bug notes

## Idea creation wrote short note text into a discarded name

- **Trigger:** Home created an `idea` whose text fit on one line within 80 characters.
- **Observed symptom:** a short idea kept only its properties and no text; a longer one duplicated a truncated pseudo-name before the complete body, while Home `get` hid that body by rendering only the list summary.
- **Cause:** Deckle sent short idea text only as `name` and long text as both `name` and `body`, although the managed `idea` type uses Anytype's note layout and notes have no name. Its `get` gesture also skipped the provider's detailed-object endpoint.
- **Violated invariant:** an idea is its body; every idea creation must write the complete text to `body`, while any Home display label is only an excerpt derived from the returned note snippet or markdown.
- **Recurrence cue:** idea creation branches on text length, sends a note `name`, idea resolution reads only the object's name, or `get` renders a list summary instead of the detailed object.

Regression: `HomeGesturesTests.CreatedIdeaCanBeFoundAndReadFromItsBodyExcerpt`.
