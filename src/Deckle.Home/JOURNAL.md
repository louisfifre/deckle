---
description: Durable findings and frozen decisions for the Home inventory domain.
type: journal
---

# Deckle.Home — Journal

## 2026-08-05

- **Life types join the surface without the code grammar.** `idee`, `course`, and `outil` are creatable through the same five gestures: no code, a free title for course/outil, a body-derived title for idee (the dev-space capture shape — single-line text of ≤80 chars becomes the whole title, longer text keeps its head as title and the full text as body). The PIÈCE-CAT grammar and element invariants stay strictly inventory-side.
- **Files properties refuse MCP writes.** `facture` and `documents` belong to the validated contract, but their values are deposited in the app; the property writer fails them with guidance instead of silently writing text.
- **The `etage` relation write fails on a key collision, not on its target.** Live discriminating test: PATCHing the `etage` objects-property fails with Anytype's "The node already has a parent" for both collection and basic targets, while identical `circuit` relation writes succeed — the property key colliding with the `etage` type key is the prime suspect. Clearing the relation to null works. A fix needs id-addressed property entries tried against the real backend; parked with the chantier/tâche lot.

## 2026-07-21

- **Room registry is live data.** Room codes are read from `Pièce` objects in the configured Home space; no room list belongs in source or local configuration.
- **Applied schema keys are the contract.** Electrical circuits and distribution boards use `circuit_elec` and `tableau_elec`. Inventory codes live in object titles; there is no `code` property.
- **Category option keys are provider-local.** Anytype derives live tag keys from their labels, so the domain resolves the normative category code through the full applied label and never assumes the manifest key survived creation.
- **Select inputs are keys; Anytype writes use live tag ids.** The Home surface accepts the normative tag key or label, resolves it against the configured space, and sends the matching tag id in object properties.
- **Collection membership is not an object relation.** Home create/update exposes collection membership separately from `properties` and writes it through Anytype's list-member endpoints.
