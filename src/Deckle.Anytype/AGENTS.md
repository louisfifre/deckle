---
description: Anytype core — backend supervision, REST transport, schema, gestures.
type: agent-instructions
---

# AGENTS.md — Deckle.Anytype

## Layers

- `Api` is transport only. It knows no task, report or document.
- `Schema` freezes measured keys. Never fix a malformed property key in code: it is the space's wire contract.
- A gesture builds the exact REST payload and returns a terse French digest.
- Dialogues are not reports. They may link to tasks; they never journal work sessions.
- `Backend` supervises the headless runtime. Deckle never owns the data store.

## Body writes

- A body PATCH replaces the whole body: REST has no block-level edit.
- A gesture that changes a body holds the write scope over read-modify-write.
- Prefer section edits through `MarkdownBody` over raw replacement.

## Documents

- Documents are reference material, not tasks or session reports.
- `DocumentGestures` only creates. Other operations stay on the generic query surface.

## Tests

Test gestures at the wire boundary with the loopback fake server. Prefer checking the JSON payload over the digest text.
