---
description: Durable findings and frozen decisions for the Home inventory domain.
type: journal
---

# Deckle.Home — Journal

## 2026-08-05

- **Life types join the surface without the code grammar.** `idee`, `course`, and `outil` are creatable through the same five gestures: no code, a free title for course/outil, a body-derived title for idee (the dev-space capture shape — single-line text of ≤80 chars becomes the whole title, longer text keeps its head as title and the full text as body). The PIÈCE-CAT grammar and element invariants stay strictly inventory-side.
- **Files properties refuse MCP writes.** `facture` and `documents` belong to the validated contract, but their values are deposited in the app; the property writer fails them with guidance instead of silently writing text.
- **"The node already has a parent" was ours, not Anytype's.** The `etage` relation failure was a client-side System.Text.Json exception: in the property writer, `value is JsonArray array ? array : [value]` target-types the collection expression as a **JsonArray**, and building it around a scalar node that already belongs to the caller's properties object re-parents it and throws. Scalar selectors hit the bug, array selectors dodge it — which is why `circuit` writes (arrays) passed while `etage` (scalar) failed, and why the key-collision hypothesis was wrong. Fixed by wrapping scalars in a plain array; verified live by setting AT's Étage to Extérieur through the patched path. The same pattern remains in `TravelPropertyWriter` (flagged as a separate task).
- **Work types are pilotage, not the dev-space PM model (decisions Louis, 2026-08-05).** `chantier` and `tache` join the surface with dedicated verbs (`chantier_create`, `tache_create`, `complete`, `chantier_overview`) because typed commands spend fewer tokens than generic gestures — the five gestures stay as the fallback. No intervention journal: done tasks are the record. Creation stays loose everywhere: a name suffices, properties land when known. Orphan tasks are allowed; the chantier is for real works.
- **Anytype's link graph is not a domain reference.** The live smoke test showed objects carry system objects-properties (`links`, `backlinks`, `creator`, `last_modified_by`); counting them in the delete guard made a tâche and its chantier hold each other undeletable. The guard now ignores them — only domain relations refuse a deletion.
- **`complete` is polymorphic on the completion signal.** A `tache` or `course` gets the native action-layout `done` checkbox (outside the property contract, entry built directly); a `chantier` gets `statut = Terminé` (its basic layout has no done box) and the reply counts still-open tasks.

## 2026-07-21

- **Room registry is live data.** Room codes are read from `Pièce` objects in the configured Home space; no room list belongs in source or local configuration.
- **Applied schema keys are the contract.** Electrical circuits and distribution boards use `circuit_elec` and `tableau_elec`. Inventory codes live in object titles; there is no `code` property.
- **Category option keys are provider-local.** Anytype derives live tag keys from their labels, so the domain resolves the normative category code through the full applied label and never assumes the manifest key survived creation.
- **Select inputs are keys; Anytype writes use live tag ids.** The Home surface accepts the normative tag key or label, resolves it against the configured space, and sends the matching tag id in object properties.
- **Collection membership is not an object relation.** Home create/update exposes collection membership separately from `properties` and writes it through Anytype's list-member endpoints.
