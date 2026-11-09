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

Free-titled types pass the near-name guard. A name that folds like an existing
object of the same type, or like an earlier item of the batch, is refused with
the candidates by id. Folding sets aside case, accents, ligatures,
punctuation, spacing and a plural `s` or `x` at the end of a word; an Idea is
compared by its first line. Update the object that exists, or pass
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

`point_dictate` writes a room as the person describes it: the room once, then
each point with its category and its fields. The codes are minted as above
and the points are written at once; the answer lists each code with its name
and id. There is no proposal round: a mistake is deleted in two steps.

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

The surface counts 38 tools: the 28 typed commands, `get`, `search`, `delete`,
`complete`, `worksite_overview`, `archive`, `point_dictate`, `product_need`,
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
- Experience export/import, reference remapping, schema reconstruction and the
  live manual app pass remain separate work.

Local validation follows the repository's compile-only rule. Build the owning
test project in Debug x64 with `/m:4`; do not infer a live probe or an executed
test suite from successful compilation.
