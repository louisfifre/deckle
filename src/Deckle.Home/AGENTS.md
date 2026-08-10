---
description: Home inventory domain — public nomenclature, live Anytype room registry, guarded inventory operations.
type: agent-instructions
---

# AGENTS.md — Deckle.Home

`Deckle.Home` is the custom Home MCP. It owns the generic home-inventory domain:
its public code grammar, category families, closed vocabularies, schema contract,
guarded operations, and their bounded model-facing MCP surface. It uses
`Deckle.Anytype` as its storage provider and plugs into the resident transport
through `Deckle.Anytype.Mcp`.

## Privacy boundary

The room registry is personal data. It MUST be read from the configured Home
space at runtime and MUST NOT appear as a compiled list, fixture copied from a
real home, documentation example, log payload, or committed identifier.

Space ids, object ids, account identities, tokens, and inventory values are
runtime coordinates only. Public examples and tests use deliberately fictional
codes and data.

## Domain boundary

The code grammar and the frozen category vocabulary are the public norm. Room
membership, object values, and relation targets are live data. A code is valid
only when its room prefix exists in that live registry.

All writes pass through the domain guards before reaching Anytype. The MCP
catalog may validate argument shape, but it MUST NOT reimplement nomenclature,
immutability, deletion, vocabulary, or relation rules. HTTP transport, bearer
authentication and MCP sessions remain in `Deckle.Anytype.Mcp`.

## Schema

Home runs only against the public managed schema contract. Type and property
keys are normative; Anytype ids and tag keys are resolved from the live space.
An absent or incompatible required shape fails closed before a write.

Titles are human names, written for a shared household — the space is meant to
be read by more than its author, so no bare acronyms. The immutable identity code of
`room`, `point`, `circuit`, and `panel` lives in the `code` property, not the
title (inverting the 2026-07-21 doctrine, see JOURNAL.md 2026-08-10).
Resolution accepts a code, a name, or an id — callers never need to know
which one they hold.

The ten former wall-point types (`prise`, `eclairage`, `commande`, `ouvrant`,
`appareil`, `reseau`, `capteur`, `relais`, `panneau`, `noeud`) are merged into
one `point` type. A point's nature is the frozen `category` select, derived
from its code, not a type discriminator; its room (`installed_in`) and its
`category` are code-derived and not directly writable — they follow the code,
the code does not follow them.

**Equipment triad.** `system` aggregates; `device` stands alone and may
optionally join a system through `part_of`; `component` only exists inside
its system — the surface refuses creation without one. In doubt, create a
device: retyping is cheap, an orphan component is not a component.

Life types (`plant`, `idea`, `errand`) carry no code at all: a plant or errand
is titled by its free name, an idea by the first line of its body — an idea is
therefore never renamed directly. Files properties are validated in the schema
but written only in the app.

Work types (`worksite`, `todo`) are free-titled like life types and carry the
house's pilotage without the dev-space PM discipline: creation is loose (a
name suffices), tasks may live orphan, and there is no intervention journal —
done tasks are the record. Prefer the dedicated verbs (`worksite_create`,
`todo_create`, `complete`, `worksite_overview`, plus `component_create`,
`plant_create`, `plant_water`) over the generic gestures; completion is the
native `done` checkbox on action layouts and `state = Terminé` on a worksite.

French labels — type names, property names, closed-vocabulary options — live
in `Terms/terms.fr.json`, loaded by `HomeTerms` at runtime (the pattern
shipped by `Deckle.Travel`). They are never hard-coded in C#: the schema names
the structure with stable English keys, the terms file names the words.

The `floor` type is labeled « Zone » in French: an assemblage of rooms —
Rez-de-chaussée, Étage, Extérieur — not a storey; the English key predates
the rename and stays as the technical coordinate.
The `floor` type is app-created: the Anytype API refuses to create a type of
collection layout, so `floor` is born in the app, not through this surface —
whether collection objects of an existing such type can be API-created is
still an open question tracked by the chantier's research task.
Its real key is discovered at runtime from the live schema snapshot rather
than compiled as a constant (see `HomeSchema.FloorTypeKey`). The `floor`
relation (carried by `room`) may only target collection objects of that
runtime-discovered type.
