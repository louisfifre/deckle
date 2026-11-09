# Home MCP operations

This reference describes the command surface and its current limits.
The runtime schema contract is documented in [Schema/README.md](Schema/README.md).

## Individual records

Each canonical type has a `<type>_create` and `<type>_update` command:
zone, room, point, circuit, panel, system, device, component, utensil, plant,
idea, product, worksite and todo. These 28 commands are the only writers of
records. They expose explicit fields from the accepted manifest, so a model
reads the fields and options of a type from its schema and needs no separate
vocabulary read. Number/checkbox/text/relation shapes are checked before
dispatch; closed options have finite input choices and all options resolve
against the configured Home space. A missing option is reported, not created.

A create takes a batch: `{"items": [ ... ]}`, 1 to 100 items of its type. Each
item takes a human name, known properties, optional initial `text`, a live
template name and collection memberships. Every item is checked for shape and
unknown fields before the Home space is read, and the whole batch (codes,
options, relations, templates, names) is prepared before the first write: one
refused item leaves the space untouched. Anytype has no transaction, so a
transport failure part way is reported item by item; create only what is
missing.

Coded types (room, point, circuit, panel) take a `code`; Circuit alone may use
its code until its human title is known. A Point may give `category` and
`room` instead of `code`: the lowest free number of that room and category is
minted, 01 to 99, gaps reused, counting the batch. An Idea requires `text` and
has no separate name. A Component requires `system`: it is born in a System.
A Zone is an ordinary object; a Room points at its Zone.

Free-titled types pass the near-name guard. A name that is near an existing
object of the same type, or an earlier item of the batch, is refused with the
candidates by id. Two names are near when their folded forms are equal (case,
accents, ligatures, punctuation, spacing and a plural `s` or `x` at the end of
a word are set aside), or they hold the same significant words in any order
(French stop words such as `de`, `la`, `et` dropped), or their sorted keys are
a typo apart (one for a key of 5 to 10 characters, two above 10, none at 4 or
fewer). Names whose numbers differ ("Radiateur 1", "Radiateur 2") are never
near. The guard accepts a distance because an LLM drives the surface: a false
refusal costs one retry, a missed duplicate splits the links between two
objects. An Idea is compared by its first line. Update the object that exists, or pass
`force: true` on the item for a true twin. Coded types keep their code as
identity and skip the guard.

An update takes one object and resolves only the named type. Omitted fields
remain unchanged. A relation selector or array replaces its list. Use a
relation delta to retain other links, for example:

```json
{"object":"Fictional lamp","connected_to":{"add":["ZZ-LC01"]}}
```

Both `add` and `remove` accept arrays. The operation reads the current object,
resolves all targets under the shared write scope, and preserves unrelated
members. Adding and removing the same resolved target refuses. A Point's code,
room and category are immutable. A Component may lose its System on update,
once dismantled; a Task may serve several Worksites.

`append_text` adds dictation after the current body. `section` takes an existing
unique `heading` and replacement `text` (empty text clears that section).
These body edits read, prepare, write and verify within the Home write scope;
they refuse missing/ambiguous headings and preserve surrounding text. Anytype
still rewrites Markdown as a whole, so app block layout is not guaranteed.
The local scope coordinates Deckle writers, not simultaneous app edits.

## Usages

A room described by the person is written through `point_create`: the LLM
extracts each point and gives `room` and `category` (the code is minted as
above) or an explicit `code`. The points are written at once and the answer
lists each created point with its code, name and id. There is no proposal
round: a mistake is deleted in two steps with `delete`, and codes are
immutable.

The errands are three verbs over the product type. `product_need` puts
products on the list and `product_taken` takes them off. A name equal to an
existing product once case and accents are set aside is that product; a name
only near one refuses the batch with the candidates (`product_need` takes
`force: true` to create the different product); an unknown name creates the
product, needed or already taken. `product_list` returns the needed products
that are not archived, grouped in the order of the category options, products
without a category last, as text and as structured groups.

`complete` marks a task done. On a worksite it requires `actual_budget` and
`actual_effort`, writes them with the done state in one patch, and answers
with each actual against its estimate and the gap (or "estimé absent"), then
the tasks still open. A task takes no actuals.

`archive` ticks the archived checkbox of a device, component, utensil, plant,
product or idea: the object had a life and should stop showing, its links
and history stay. `archived: false` brings it back. Other types refuse: a
point is deleted, work is finished, the structure stays.

## Retrieval and related work

`get` reads an object and its body. `search` combines known-context filters:
type, text, room, circuit, Point category, worksite, state, system, equipment
domain/category, connected Point, panel, concerned object, task completion,
`needed` and `archived`. Archived objects are left out unless `archived: true`
asks for them alone. Structured results carry `matches` (id, provider type
key, name, code), `total`, `offset`, and `next_offset`. Default page size is
50; maximum is 100. A text fallback remains available. Pagination bounds the
returned result, while the provider index is still fully enumerated. Text
matches names, codes and returned properties, not full bodies.

The LLM starts from known context, reads existing tasks/worksites before writing,
and asks a focused question when target identity or action remains ambiguous.
Use a few focused searches, active work first and completed work when relevant.
Respect an explicit worksite. A failed lookup must never be interpreted as a
successful search with zero matches.

`worksite_overview` follows direct task attachments. `delete` keeps its
recoverable-bin preview/confirmation by id and its reference guards, and also
asks the person: after the confirming call, Deckle shows the same Windows
notification as the Anytype delete and waits up to 60 seconds. Only a "move to
bin" answer lets the delete through; a refusal, no answer in time, or no way
to show the notification (notifications off, Deckle elevated, a host with
none) refuses it and nothing moves. The reference guard is checked again after
the answer, under the write scope, so a link added while the notification
waited still stops the delete. Each refusal and each completed move is told in
the log with its reason and the object's type key, never its name or id.

The surface counts 37 tools: the 28 typed commands, `get`, `search`, `delete`,
`complete`, `worksite_overview`, `archive`, `product_need`,
`product_taken` and `product_list`.

## Selecting objects

A read (`get`, `search` filters, `worksite_overview`) takes the one object a
selector partly matches. A write (`*_update`, `complete`, `archive`, `delete`,
relation targets, collection memberships) does not: it resolves an id, a code
or a name given in full, and a partial name is refused with the nearest
candidates by id, so the call is repeated with the id. A write never lands on
an object that was only guessed.

## Remaining boundaries

- Typed multi-hop paths are not implemented.
- Files, retyping and select-option creation are still outside these commands.
- A select/number/date cannot yet be cleared via null; omit unknown facts.
- Types, views and spaces are administration gestures: they belong to a
  separate guichet, not to this surface.
- There is no permanent preview-card/elicitation workflow. Search candidates
  provide one structured building block for future mobile interaction.
- The recreation of a space is rehearsed headless on a test space (see
  Recreation rehearsal); views, templates, placement and widgets stay the
  person's pass in the app.

## Recreation rehearsal

The rehearsal proves, on a TEST space and never on the live one, that a
space can be recreated on the manifest with every object's information
preserved, through the typed gestures only. It is a maintainer operation:
two explicit maintenance tests in `Deckle.Home.Tests` host it, and they skip
unless their variables are set. Running it is the maintainer's act.

1. Create the test space once. With `DECKLE_HOME_CREATE_TEST_SPACE` set to
   the exact name (for example `Home test`), `CreateTestSpace` creates it
   through `POST /v1/spaces`, refuses a name already taken, and prints the
   line to add to the `aliases` object of `spaces.json` in the anytype
   module folder (`"home-test": "<id>"`). The public API has no invite: the
   space is the bot's alone until the person joins it from the app.
2. Rehearse. With `DECKLE_HOME_REHEARSAL_SOURCE` (an alias, `home`) and
   `DECKLE_HOME_REHEARSAL_TARGET` (the test space's alias) set,
   `RecreateOnTestSpace` runs `HomeRecreationRehearsal`: export, schema,
   replay, compare, counts.

```powershell
$env:DECKLE_HOME_CREATE_TEST_SPACE = 'Home test'
pwsh -File scripts/build-locked.ps1 -Test -Project tests/Deckle.Home.Tests/Deckle.Home.Tests.csproj --filter "FullyQualifiedName~HomeRecreationRehearsalTests.CreateTestSpace" /p:VSTestCLIRunSettings=xUnit.Explicit=only --logger "console;verbosity=detailed"

$env:DECKLE_HOME_REHEARSAL_SOURCE = 'home'
$env:DECKLE_HOME_REHEARSAL_TARGET = 'home-test'
pwsh -File scripts/build-locked.ps1 -Test -Project tests/Deckle.Home.Tests/Deckle.Home.Tests.csproj --filter "FullyQualifiedName~HomeRecreationRehearsalTests.RecreateOnTestSpace" /p:VSTestCLIRunSettings=xUnit.Explicit=only --logger "console;verbosity=detailed"
```

`/p:VSTestCLIRunSettings=xUnit.Explicit=only` is how an explicit test runs
through `build-locked.ps1`: the script cannot forward `--` and inline run
settings, and the MSBuild property carries them. The detailed console
logger shows the test output, where the counts are printed.

The rehearsal refuses before any write when the target alias is `home` or
`dev`, when source and target resolve to the same space, or when the
target already holds Home objects (a second run would double them; start
again on a new test space).

Steps, in the order they run:

- Export. `HomeSpaceExporter` reads the source through the raw client
  (listing, then each object's body) into a replay sheet on the manifest
  keys. The old-key bridge applies there: errand becomes product, floor
  becomes zone and a room held by a floor collection gets its `zone`
  relation, `documents`, `errand_category`, `memory_size_gb`,
  `memory_frequency_mhz`, `switch_kind` and `floor` move to their new keys,
  old `notes` join the body after a blank line. Dead keys (`power_watts`,
  `condition`, `light_nature`, `existence`, `aisle`, `survey_date`), a point
  category outside the 22, files, and links to objects that do not travel
  stay raw under `unmapped` and are counted. Anytype's built-in types are
  skipped and counted. Options travel by name, relations by code (coded
  types) or name, with the source ids kept beside them.
- Schema. The manifest goes on the target through `SchemaAdminGestures`
  (preview, then apply). It is the compiled manifest plus the option names
  the sheet uses in open and seeded vocabularies; closed ones stay compiled.
- Replay. `HomeSpaceReplayer` creates the objects through `HomeGestures`
  only: per type in the design-note order, batches of at most 100, every
  item forced past the near-name guard, a type's single template when it
  has exactly one, memberships of collections the target holds. Relations
  to objects not yet created wait for one update pass; done tasks go
  through `complete`. A refused batch is retried item by item, and each
  refused item is counted under a reason.
- Compare. `HomeRecreationComparer` reads the target back, matches each
  sheet object within its type by code, else by name, and compares name,
  body (whitespace set aside), done box, memberships and every mapped
  property: options by the manifest option their name stands for,
  relations by the code or name they point at.

Files. The sheet and its reports hold the person's data and live outside
the repository, under `%LOCALAPPDATA%\Deckle\modules\home\rehearsal\`, or at
the path in `DECKLE_HOME_REPLAY_SHEET`:

- `<yyyyMMdd-HHmm>-<alias>.json`: the replay sheet, with export counts.
- `<stem>.replay.json`: each refusal with its reason and the gesture's
  message, dropped relations and memberships.
- `<stem>.compare.json`: each difference, field, expected and actual.
- `<stem>.rehearsal.json`: the counts of every step, as printed.

Nothing that names an object is logged or printed: the test output holds
counts and the sheet path only.

What the comparison proves: the objects the typed verbs can hold, with
their mapped values, relations, bodies, task completion and collection
memberships, survive an export and a recreation through the guichet alone,
and the refusals are counted by cause. What it does not prove: anything
under `unmapped`, which the new model drops on purpose; files (manuals,
receipts, photos), which the verbs do not write; creation and modification
dates and authors, which a recreation resets; and everything REST cannot
write.

The boundary recorded on 2026-11-09. REST cannot write templates, views
(beyond four v2 view operations Deckle has not modeled), featured or hidden
property placement, widgets and pinning, and it serves no template content
and no active view. The official export (doc.anytype.io) offers Markdown,
Any-Block (Protobuf or JSON, zip), PDF and HTML, for one object, a
selection, a view or the whole space, with linked objects, files and
archived objects as options; import is desktop-only, from Space Settings;
members and chats are not exported. The documentation does not say whether
types, properties, options, templates and views travel, nor whether ids and
keys survive. The Experience Gallery page says an experience, a space
exported as Any-Block JSON, includes only the types, properties, widgets,
templates and objects chosen: the one official statement that templates and
widgets travel, views not named. A reading of anytype-heart (2026-09, home
repository NOTES.md) found that the Any-Block export carries types, views
and templates. Both are leads, not proof. So the experience stays the
person's pass in the app: export the live space as Any-Block JSON, import
it into a test space, then compare views (`schema_inspect_space` on both
aliases) and templates (by eye) with the live space.

## Local validation

Local validation runs the owning suite through
`scripts/build-locked.ps1 -Test -Project tests/Deckle.Home.Tests/Deckle.Home.Tests.csproj`
(Debug x64, the machine-wide build lock). A green suite proves the contract
against the fake server, never a live space: the live proof is the recreation
rehearsal above, run on a test space.
