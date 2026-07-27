---
description: Trip-preparation domain — seven-type model, closed vocabularies, guarded operations, externalized terms.
type: agent-instructions
---

# AGENTS.md — Deckle.Travel

`Deckle.Travel` is the custom Travel MCP. It owns the trip-preparation domain:
a seven-type model, its closed vocabularies, guarded operations, and their
bounded model-facing MCP surface. It uses `Deckle.Anytype` as its storage
provider and plugs into the resident transport through `Deckle.Anytype.Mcp`.
The space alias `travel` resolves to the personal « Vacances » space at runtime.

## Privacy boundary

Trip data is personal. Space ids, object ids, tokens, and stored values are
runtime coordinates only; public examples and tests use fictional trips.

Receipt capture (photos, dictation) is processed locally, never sent to any
cloud model. The pipeline does not exist yet; the expense gesture is its
future entry point and must stay callable by a local client.

## Domain model

Seven types. The Activity is the central object of a stay.

- **Stay** — a trip already validated elsewhere; the space prepares it, never
  justifies it. No budget, no estimation properties.
- **Stage** — mandatory, carries « where I am from the 12th to the 15th »; a
  single-city stay has one degenerate stage, created in the same gesture as
  the stay. Its body hosts the day-to-day account when the user wants one.
- **Place** — the durable « where » registry (museums included); enters only
  when worth keeping. Stable, filterable properties: category, accessibility,
  visit duration, address, official site. Perishable facts (closing days,
  prices, booking requirements) go to the body, dated at collection.
- **Activity** — the planned « doing »: walk, visit, evening, sport, meal,
  other. No status property: the day-level Date is the state (unset = pool,
  set = fixed, past = done); RDV carries the time when it binds. Optional
  duration; linked Place; linked files (GPX); linked Expense.
- **Transfer** — a booked movement: Date + RDV (departure time), closed mode
  vocabulary (plane, train, bus, ferry, car), confirmation reference, ticket
  files, linked Expense. No structured origin/destination — the name says it.
- **Lodging** — arrival/departure (check-in time matters), address,
  confirmation reference, files, linked Expense, linked to its Stage.
- **Expense** — the receipt-level cost register: amount, date, category,
  stay. Nothing else; the amount is written only once certain (paid, or
  booked at a known price). Rich objects link to their expense; orphan
  receipts stay unlinked.

## Guarded operations

Expense category is mandatory and must match the closed vocabulary; a missing
fit means the vocabulary lacks an option, and options are added by the user in
Anytype, never by the surface. The expense's stay resolves from its date when
exactly one stay covers it; otherwise the gesture requires the stay explicitly
and fails clearly rather than guessing.

The surface builds, plans, records, and reads. It exposes no deletion; the
user deletes in the app.

There is no immutable code grammar: trips identify by destination and dates,
objects by name and links. Provisioning is lazy, as in Home — initialize and
tools/list answer before any schema work.

## Wording

Code and docs are English. Every label visible inside Anytype — type names,
property names, closed-vocabulary options — lives in the module's terms file,
French today; adding a language must remain a file addition, deferred to the
app-wide language pass (tracked in Anytype: « Revue multilingue »).
