---
description: "Home inventory vocabulary — the public norm, personal room registry, element codes, and managed Home schema."
type: agent-instructions
---

# Deckle.Home — Context

This context names the boundary between Deckle's shareable home-inventory
mechanism and the personal data held by each user's Anytype space.

## Norm and data

**Home norm**:
The shareable vocabulary and constraints carried by `Deckle.Home`: element-code
grammar, category families, closed lifecycle vocabularies, and invariants. It is
the mechanism that may be published.
_Avoid_: room registry (personal data), inventory (the user's recorded objects)

**Room registry**:
The live set of room codes and room objects in the configured Home space. It is
personal data and the authority used to decide whether an element-code prefix is
known; it never becomes a compiled constant.
_Avoid_: nomenclature (the registry instantiates the norm but is not the norm), configuration list (a second truth)

**Home inventory**:
The rooms, elements, circuits, panels, and their relations stored in the Home
space. The inventory belongs to the user and is never part of Deckle's source.
_Avoid_: Home norm (shareable mechanism), schema (storage contract)

## Codes and schema

**Element code**:
The immutable identifier of one physical or planned element, composed of a room
prefix, a category code, and a two-digit sequence. Its room prefix is accepted
only when the room registry contains it.
_Avoid_: object id (Anytype runtime coordinate), room code (only the location prefix)

**Home schema**:
The public managed Anytype contract required by the Home domain. Its type and
property keys are stable; ids and tag keys are local realizations resolved from
the live space.
_Avoid_: Home inventory (values stored under the schema), space id (local coordinate)

**Inventory code**:
The immutable code carried by an inventory object's Anytype title. Element
titles contain only the code; rooms, circuits, and distribution boards may add
a human suffix after an em dash. There is deliberately no duplicate `code`
property in the managed schema.
_Avoid_: object id (provider coordinate), label (human wording)

**Home surface**:
The MCP capability exposing the guarded Home operations through Deckle's single
resident MCP host. It is an adapter over `Deckle.Home`, not the owner of its rules.
_Avoid_: Home space (data store), Home module (domain implementation)
