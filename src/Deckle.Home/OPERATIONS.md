# Home MCP operations

This reference describes the target command surface and its current limits.
The runtime schema contract is documented in [Schema/README.md](Schema/README.md).

## Individual records

Each canonical type has a `<type>_create` and `<type>_update` command:
zone, room, point, circuit, panel, system, device, component, utensil, plant,
idea, product, worksite and todo. These 28 commands expose explicit fields from
the accepted manifest. Number/checkbox/text/relation shapes are checked before
dispatch; closed options have finite input choices and all options resolve
against the configured Home space. A missing option is reported, not created.

Creation accepts a human name, known properties, optional initial `text`, a live
template name and collection memberships. Coded types also require `code`;
Circuit alone may use its code until its human title is known. An Idea requires
`text` and has no separate name. A Component still requires `system`. A Zone
object can be created only after its collection-layout type exists.

An update resolves only the named type. Omitted fields remain unchanged. A
relation selector or array replaces its list. Use a relation delta to retain
other links, for example:

```json
{"object":"Fictional lamp","connected_to":{"add":["ZZ-LC01"]}}
```

Both `add` and `remove` accept arrays. The operation reads the current object,
resolves all targets under the shared write scope, and preserves unrelated
members. Adding and removing the same resolved target refuses. Component's
mandatory System and immutable Point identity apply to every path.

`append_text` adds dictation after the current body. `section` takes an existing
unique `heading` and replacement `text` (empty text clears that section).
These body edits read, prepare, write and verify within the Home write scope;
they refuse missing/ambiguous headings and preserve surrounding text. Anytype
still rewrites Markdown as a whole, so app block layout is not guaranteed.
The local scope coordinates Deckle writers, not simultaneous app edits.

The four older dedicated creates retain their known `properties` map and
`component.system` / `plant.room` arguments for compatibility. New individual
calls should use flat fields. Generic `create` and `update` retain homogeneous
creation batches and multi-object updates, limited to 100 items. Batch validation
precedes sequential writes; provider failures can leave a partially applied
batch. There is no durable per-item replay ledger yet.

## Retrieval and related work

`get` reads an object and its body. `search` combines known-context filters:
type, text, room, circuit, Point category, worksite, state, system, equipment
domain/category, connected Point, panel, concerned object, task completion and
`needed`. Legacy `condition` is read-only compatibility. Structured results carry
`matches` (id, provider type key, name, code), `total`, `offset`, and `next_offset`.
Default page size is 50; maximum is 100. A text fallback remains available.
Pagination bounds the returned result, while the provider index is still fully
enumerated. Text matches names, codes and returned properties, not full bodies.

The LLM starts from known context, reads existing tasks/worksites before writing,
and asks a focused question when target identity or action remains ambiguous.
Use a few focused searches, active work first and completed work when relevant.
Respect an explicit worksite. A failed lookup must never be interpreted as a
successful search with zero matches. These are interaction instructions, not a
semantic duplicate detector inside the server.

`worksite_overview` follows direct task attachments. `complete` marks a task done
or closes a worksite and reports remaining tasks. Products use `needed` (Prendre)
instead. `delete` keeps its recoverable-bin preview/confirmation and reference
guards. These seven shared operations plus the 28 typed commands make 35 tools.

## Remaining boundaries

- Explicit archive retrieval and typed multi-hop paths are not implemented.
- Files, retyping and select-option creation are still outside these commands.
- A select/number/date cannot yet be cleared via null; omit unknown facts.
- There is no permanent preview-card/elicitation workflow. Search candidates
  provide one structured building block for future mobile interaction.
- Task multi-attachment cardinality and removed Component membership have not
  acquired new rules. Relation edits preserve the existing permitted model.
- Experience export/import, reference remapping, schema reconstruction, typed
  batch replay and the live manual app pass remain separate work.

Local validation follows the repository's compile-only rule. Build the owning
test project in Debug x64 with `/m:1`; do not infer a live probe or an executed
test suite from successful compilation.
