---
description: Trip-preparation domain — closed vocabularies, guards, terms.
type: agent-instructions
---

# AGENTS.md — Deckle.Travel

The domain model is in `TravelSchema` and `Terms/terms.fr.json`.

## Privacy boundary

Trip data is personal. Space ids, object ids, tokens and stored values are runtime coordinates only; examples and tests use fictional trips. Trip data MUST NOT be sent to any remote model or service, by any gesture or future pipeline. Only local inference and the Anytype backend's own sync may process it.

## Guarded operations

- All writes pass through the domain guards. The catalog MUST NOT reimplement vocabulary, stay-resolution or relation rules.
- The user adds closed-vocabulary options in Anytype, never the surface.
- The surface exposes no deletion and MUST NOT grow one.
- There is no code grammar: objects identify by name and links. Do not introduce one.

## Schema traps

- Tag-option keys are provider-local: Anytype derives them from labels. Resolve options by key or label.
- A `files` PATCH replaces the whole list. Read, extend and write it back inside the write scope. Only booking types accept files.
