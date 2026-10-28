# Typed read bug notes

## Bin flag read under a key a v2 read never serves

- **Trigger:** a typed read by id (`task_properties` and the other digests that end with the facts block) of an object that sits in Anytype's bin.
- **Observed symptom:** inferred, not seen through Deckle: the digest would carry no `Corbeille : oui` line, so a binned object reads like a live one. What was observed, on 2026-10-22, is the raw shape: a v2 GET of a binned task serves `"is_archived": true` among its properties and nothing at the root, while a v1 GET of the same task says `archived: true`.
- **Cause:** `AnytypeV2PropertyMap.Archived` looked for `archived`, in the properties and at the root, a guess written before the flag's place was measured. `archived` is accepted by the v2 search as a filter and field alias (alongside `is_archived` and `isArchived`), which made the guess look plausible; a read serves only `is_archived`.
- **Violated invariant:** a v2 read helper reads a property under the key a real GET serves, not under a key the search merely accepts.
- **Recurrence cue:** a flag that a search filter on the same key finds but that no read ever shows; a reader that checks two places for one value because neither was measured.

Regression: `ReadGesturesTests.TaskPropertiesFlagsAnObjectInAnytypesBinWithACorbeilleLine` and `AnytypeV2PropertyMapTests.TheBinIsReadFromIsArchivedOnly`.

## A nameless idea read its title three times and its opening twice

- **Trigger:** `idea_text` of an idea whose `name` is empty, its title being the first line of its body.
- **Observed symptom:** seen on 2026-10-28 through the live MCP: the digest repeated the title under the id, then a copy of the body's opening cut with « … », then the whole body again from its title.
- **Cause:** for a nameless idea, the v2 markdown render opens with the snippet as paragraphs, cut with « … », before the body's own blocks (measured the same day). `DropTitle` only drops a line that spells the object's name, and the name is empty, so the header stayed. The name taken from the first line then repeated the body's own first line.
- **Violated invariant:** a typed read shows each line of the object once: the title as identity, the body as the author wrote it.
- **Recurrence cue:** a digest whose opening lines end with « … » and come back in full just after; a nameless object type whose render was never measured. Whether a short, uncut snippet is rendered too is not established; a nameless report measured the same day opened on its own body.

Regression: `ReadGesturesTests.IdeaTextReadsANamelessIdeaTitleAndTextOnce`.
