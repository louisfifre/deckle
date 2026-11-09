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

When nothing contains the selector, near names are looked for with the same
comparison as the near-name guard (a typo, another word order). A write lists
the near objects by id and never picks one; a read takes the single near
object, or lists several.

## Remaining boundaries

- Typed multi-hop paths are not implemented.
- Files, retyping and select-option creation are still outside these commands.
- A select/number/date cannot yet be cleared via null; omit unknown facts.
- Types, views and spaces are administration gestures: they belong to a
  separate guichet, not to this surface.
- There is no permanent preview-card/elicitation workflow. Search candidates
  provide one structured building block for future mobile interaction.
- The recreation of a space is rehearsed headless on a test space (see
  Recreation rehearsal). Type settings, type views, section-collection
  views, templates and widgets are carried by the rehearsal through the v2
  API (see the boundary below). What the API does not show (how any of it
  renders, what an inline query lists), and the personal sidebar, the
  person checks in the app.

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
   module folder (`"home-test": "<id>"`). The public API has no invite and
   the bot is an Anytype account of its own, so nobody can bring the person
   into a space the bot created: use this step only for a space no person
   needs to see. For a test space the person looks at, the person creates it
   in the app and shares an invite link, the bot joins it
   (`anytype space join <link>`), and its id goes into `spaces.json`.
2. Rehearse. With `DECKLE_HOME_REHEARSAL_SOURCE` (an alias, `home`) and
   `DECKLE_HOME_REHEARSAL_TARGET` (the test space's alias) set,
   `RecreateOnTestSpace` runs `HomeRecreationRehearsal`: export (objects and
   structure), schema, replay, compare, structure write and read-back,
   counts.
3. Or split the run, when the bot that reads the source cannot reach the
   target (a cut-over from one Anytype network to another). `ExportSheet`
   with `DECKLE_HOME_EXPORT_SOURCE` (an alias) writes the sheet and prints its
   path; nothing else is written. Later, `ReplaySheetOnTestSpace` with
   `DECKLE_HOME_REPLAY_SHEET` (that path) and `DECKLE_HOME_REHEARSAL_TARGET`
   runs schema, replay, compare, the structure and counts from the file. The usual
   refusals hold, plus one: the target alias is never the alias the sheet
   was exported from.

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
- Export, structure. `HomeStructureExporter` then reads, with v2 GETs only,
  what the source holds beyond its objects, as the source speaks it (old
  keys, old ids): for each Home type its settings (default view, default
  template, the featured or hidden section of each property) and its own
  views; for each section collection (a collection that is not a Home
  object) its views; each template whole, as its AnyBlock document; the
  widgets of the space sidebar in order. It goes into the sheet under
  `structure`, and the sheet is then version 2: a sheet without it loads as
  version 1 and carries no structure, and the replay says so. Anytype's
  built-in types are not carried (their views are the defaults every space
  has and nothing of Home hangs on them), nor are templates of a type Home
  does not own, nor widgets of the personal sidebar; each is counted as
  skipped. A refused read costs that item and is counted `unread_*`.
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
- Structure, remap and write (after the objects, on the target only). The
  remap is pure (`HomeStructureRemap`, unit-tested). A type key goes through
  the exporter's bridge (errand to product, floor to zone), a property key
  through the same bridge as the objects. A property the new manifest does
  not hold, or does not attach to the type, is dropped and never guessed:
  from the columns, sorts, group and filters of a view, from a template's
  values and property blocks, from a type's sections; Anytype's built-in keys
  pass. Ids come from one old-to-new map: the replay's created ids, the
  target type of each source type, the new template of each old one, the
  target collection of the same name; an id that looks like an object id
  and is in no map is dropped with the member that carried it. `_filter_template_1_`
  (« this object ») and other literals pass untouched. A select option in a
  filter or in a template is read through the manifest (its key, French
  label or name gives the manifest option; a name unknown to a closed
  vocabulary is dropped). Output-only members of the schema (block `fields`
  and `source`, view `groups`, filter and sort `id`…) are stripped, and the
  block ids with them. Then `HomeStructureWriter` writes, in this order:
  templates (one POST each, `create_missing_options` on, so an open
  vocabulary grows by the names a template uses); per type its default view
  and mapped default template, then its sections (the target type's whole
  property list is read and sent back with only the sections changed,
  because the patch replaces the list), then its views (the first source
  view updates the target's default view, the others are inserted, each as
  its own request so a refused view costs that view; a view always sends its
  sorts and filters, empty when the source had none, because `set` merges
  and the target's default view has sorts of its own; the columns the patch
  does not name are then read back and hidden, so the visible set matches);
  the section collections' views; the widgets in source order, each target
  mapped (a type to the target type, a collection to its namesake, another
  object through the id map) and each view mapped to the view written for it
  (a view that was not carried is dropped from the widget, the widget
  stays). Every refusal is caught, counted by stage and written to a file;
  one failure never stops the rest.
- Structure, read back. `HomeStructureComparer` reads the target and holds
  it against what the write settled on: per type default view, default
  template and sections; per view (type or collection) the count, then name,
  type, group, filters, sorts and visible columns (as a set: a column patch
  cannot reorder); per template the count per type, its type, its property
  values and the histogram of its block kinds; per widget the count, the
  order, the target, the layout and the view. A widget the target already
  had on another target is not held against the plan.

Files. The sheet and its reports hold the person's data and live outside
the repository, under `%LOCALAPPDATA%\Deckle\modules\home\rehearsal\`, or at
the path in `DECKLE_HOME_REPLAY_SHEET`:

- `<yyyyMMdd-HHmm>-<alias>.json`: the replay sheet, with export counts.
- `<stem>.replay.json`: each refusal with its reason and the gesture's
  message, dropped relations and memberships.
- `<stem>.compare.json`: each difference, field, expected and actual.
- `<stem>.structure.json`: each refusal of the structure write (stage,
  what, message) and each drop of the remap (kind, where, what).
- `<stem>.structure-compare.json`: each difference of the structure
  read-back, field, expected and actual.
- `<stem>.structure-error.json`: only when the structure step met an answer
  nobody planned for; the objects' counts are still written.
- `<stem>.rehearsal.json`: the counts of every step, as printed, the
  structure's included (exported, written, refused, missing, dropped,
  compared, different by field).

Nothing that names an object is logged or printed: the test output holds
counts and the sheet path only.

What the comparison proves: the objects the typed verbs can hold, with
their mapped values, relations, bodies, task completion and collection
memberships, survive an export and a recreation through the guichet alone,
and the refusals are counted by cause. What it does not prove: anything
under `unmapped`, which the new model drops on purpose; files (manuals,
receipts, photos), which the verbs do not write; creation and modification
dates and authors, which a recreation resets; and everything REST cannot
write. The structure's comparison proves that what the v2 API serves back
equals what was meant; it does not prove how the app renders it. Text inside
a template that links to an object by id (`anytype://object?objectId=…`) is
not rewritten.

The boundary as proved on 2026-11-09, evening, on a throwaway bot-created
space of the self-hosted network, fictional data only, every write read back
through the API. The v2 API (the PC bot's anytype-cli serves the same
schemas, `GET /v2/schemas`) writes:

- on a type (`PATCH /v2/spaces/{space}/types/{key}`): `default_view`,
  `default_template` (an object created without a template then starts from
  it), the featured or hidden section of each property, and the type's own
  views through an `ops` envelope (`insert_view`, `update_view`);
- a set: `POST /v2/spaces/{space}/queries` with its views whole (table,
  kanban grouped by a property, gallery; filters, sorts, columns with an
  aggregation);
- a collection: `POST /v2/spaces/{space}/collections` with its items, then
  its views through `PATCH /v2/spaces/{space}/objects/{id}` ops;
- a template: `POST /v2/spaces/{space}/templates` with property values and a
  body, including an inline dataview block with its views and filters; an
  object created from it gets the values and the body. A filter on « this
  object » is spelled `_filter_template_1_`, the same token the app stores in
  the live space's templates and in the objects made from them, resolved
  when the app renders;
- widgets: `POST /v2/spaces/{space}/widgets` on a query view or a
  collection, space scope.

Not shown by the API: what an inline query lists inside an object (no
endpoint answered), and how any of this renders, which only the app shows.
A widget in personal scope belongs to the member who writes it, so the
bot cannot set the person's own sidebar (inference from the scope field).
Deckle now carries, through that API and on the new keys, the type
settings, type views, section-collection views, templates and widgets (see
Recreation rehearsal, structure steps); sets are not carried, the source
holds none. The reads and writes go through one public pass-through on the
v2 client, `SendJsonAsync`, that adds no behaviour of its own.

The earlier boundary, recorded on 2026-11-09 before that proof, read the
2025-11-08 v1 OpenAPI: REST could not write templates, views, placement or
widgets. The official export (doc.anytype.io) offers Markdown,
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
and templates. Both are leads, not proof, and the person ruled the export
and import out on 2026-11-09: it would carry the old keys. The reborn space
recreates its views and templates on the new keys instead.

## Local validation

Local validation runs the owning suite through
`scripts/build-locked.ps1 -Test -Project tests/Deckle.Home.Tests/Deckle.Home.Tests.csproj`
(Debug x64, the machine-wide build lock). A green suite proves the contract
against the fake server, never a live space: the live proof is the recreation
rehearsal above, run on a test space.
