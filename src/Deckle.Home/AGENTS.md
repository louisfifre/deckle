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

The code grammar and category-to-type mapping are the public norm. Room
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

Inventory codes live in object titles. Element titles are exactly their code;
other inventory titles may use `CODE — label`. Do not introduce a parallel
`code` property: it creates two truths and does not match the applied schema.
