---
description: Home inventory domain — nomenclature, live room registry, guards.
type: agent-instructions
---

# AGENTS.md — Deckle.Home

## Privacy boundary

The room registry is personal data. It MUST be read from the configured Home space at runtime. It MUST NOT appear as a compiled list, a fixture copied from a real home, a documentation example, a log payload or a committed identifier.

Space ids, object ids, account identities, tokens and inventory values are runtime coordinates only. Public examples and tests use fictional codes and data.

## Domain boundary

- The code grammar and the category vocabulary are the public norm. Room membership, values and relation targets are live data.
- A code is valid only when its room prefix exists in the live registry.
- All writes, batch writes included, pass through the domain guards. The catalog MUST NOT reimplement nomenclature, immutability, deletion, vocabulary or relation rules.
- Transport stays in `Deckle.Anytype.Mcp`.

## Schema

- Type and property keys are normative. Anytype ids and tag keys resolve from the live space.
- An absent or incompatible required shape fails closed before any write.
- See `Schema/schema-manifest.json`, `Schema/README.md` and `OPERATIONS.md`.

## Types and names

- Titles are human names for a shared household, no bare acronyms. The identity code of `room`, `point`, `circuit` and `panel` lives in the `code` property (see JOURNAL.md).
- Resolution accepts a code, a name or an id.
- Every wall point is one `point` type. Its `category` and room (`installed_in`) derive from the code and are not writable.
- Equipment triad: `system` aggregates; `device` stands alone and may join a system through `part_of`; `component` exists only inside a system. In doubt, create a device.
- Life types (`plant`, `idea`, `errand`) carry no code. An idea is titled by its body's first line, so it is never renamed directly.
- Files properties are written only in the app.
- Work types (`worksite`, `todo`) are loose: a name suffices, orphans allowed, no journal. A todo completes with native `done`, a worksite with `state = done`.
- French labels live only in `Terms/terms.fr.json`, never in C#.

## Zone

- The Zone type is created in the app: the Anytype API refuses to create a collection-layout type. Zone objects are ordinary API objects.
- Its key is app-derived, discovered at runtime (`HomeSchema.FloorTypeKey`), never compiled.
- The `floor` relation on `room` may only target objects of that type.
