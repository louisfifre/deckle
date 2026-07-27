---
description: Trip-preparation domain — seven-type model, closed vocabularies, guarded operations, externalized terms.
type: agent-instructions
---

# AGENTS.md — Deckle.Travel

`Deckle.Travel` is the custom Travel MCP. It owns the trip-preparation domain
and its bounded model-facing MCP surface. It uses `Deckle.Anytype` as its
storage provider and plugs into the resident transport through
`Deckle.Anytype.Mcp`. Code and docs are English; every label visible inside
Anytype lives in the module's terms file, so adding a language stays a file
addition.

The normative domain model — the seven types, their properties, the closed
vocabularies — lives in the module's schema contract and terms file. Read it
there; do not recopy it here.

## Privacy boundary

Trip data is personal. Space ids, object ids, tokens, and stored values are
runtime coordinates only; public examples and tests use fictional trips. Trip
data MUST NOT be sent to any remote model or service, by any gesture or future
pipeline. Local inference and the Anytype backend's own sync are the only
processing this domain admits.

## Guarded operations

All writes pass through the domain guards before reaching Anytype. The MCP
catalog may validate argument shape, but it MUST NOT reimplement vocabulary,
stay-resolution, or relation rules. Transport, bearers, and MCP sessions stay
in `Deckle.Anytype.Mcp`.

Closed-vocabulary options are added by the user in Anytype, never by the
surface. The surface builds, plans, records, and reads; it exposes no deletion
and MUST NOT grow one — the user deletes in the app. There is no code grammar:
trips identify by destination and dates, objects by name and links. Do not
introduce one.

## Schema pitfalls

Type and property keys survive creation as sent, under their localized labels.
Tag-option keys do not: Anytype derives them from labels at creation, so they
are provider-local — never assume a manifest option key survived; resolve
options by key or applied label, as the validation and gestures already do.

`Deckle.Anytype` owns file upload; the domain owns the attaching. A `files`
PATCH replaces the whole list, so attaching reads the current one and writes
it back extended, inside the write scope. Only the booking-bearing types
accept files, and a refusal lands before any byte is sent.
