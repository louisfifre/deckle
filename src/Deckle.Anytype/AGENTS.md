---
description: Anytype core module — headless backend supervision, REST transport, frozen Dev-space schema, and domain gestures.
type: agent-instructions
---

# AGENTS.md — Deckle.Anytype

Core domain layer over Louis's Anytype Dev space. It owns the headless backend lifecycle, credentials, the thin REST client, the frozen schema map, and the gestures that turn raw Anytype objects into Deckle concepts. It does **not** own the MCP protocol, client tokens, or any visible UI.

## Layer Shape

`Api` is transport only: build HTTP requests, parse JSON roots, serialize calls, retry transient failures. It does not know what a task, project, report, dialogue, or document means.

`Schema` freezes measured Anytype keys. Never "fix" malformed property keys in code; those keys are the wire contract of the space. A display label may be clean while the stored key stays malformed.

`Gestures` carry project-management intent: tasks, projects, session reports, generic query/update/link/body edits, management actions, and documents. A gesture returns a terse French digest for model use and builds the exact REST payload itself.

`Dialogues` are chat-surface gestures, not reports. They may link to tasks as metadata, but they do not journal work sessions.

`Backend` starts, adopts, and supervises the headless Anytype runtime. Deckle orchestrates this process; it does not reimplement or own the data store.

## Body Writes

Anytype REST has no block-level edit surface. A markdown body PATCH replaces the whole body. Mutating gestures that touch a body must hold the write scope over read-modify-write and should prefer section-targeted edits through `MarkdownBody` instead of raw body replacement.

Creation may send an initial `body`. Later edits should go through the generic section-edit gesture unless a new, stricter body operation has a concrete reason to exist.

## Documents

Documents are stable reference material: architecture, instructions, nomenclature, specifications, research, tips. They are not tasks, not session reports, and not a substitute for journal lines.

`DocumentGestures` owns document creation only. Reading, search, title/property updates, archive, and section replacement stay on the generic query surface so the behavior is shared with every object type.

## Tests

Gesture tests assert behavior at the Anytype wire boundary with the loopback fake server. Prefer checking the JSON payload the live API would receive over matching the human digest text.
