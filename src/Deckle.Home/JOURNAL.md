---
description: Durable findings and frozen decisions for the Home inventory domain.
type: journal
---

# Deckle.Home — Journal

## 2026-07-21

- **Room registry is live data.** Room codes are read from `Pièce` objects in the configured Home space; no room list belongs in source or local configuration.
- **Applied schema keys are the contract.** Electrical circuits and distribution boards use `circuit_elec` and `tableau_elec`. Inventory codes live in object titles; there is no `code` property.
- **Category option keys are provider-local.** Anytype derives live tag keys from their labels, so the domain resolves the normative category code through the full applied label and never assumes the manifest key survived creation.
- **Select inputs are keys; Anytype writes use live tag ids.** The Home surface accepts the normative tag key or label, resolves it against the configured space, and sends the matching tag id in object properties.
- **Collection membership is not an object relation.** Home create/update exposes collection membership separately from `properties` and writes it through Anytype's list-member endpoints.
