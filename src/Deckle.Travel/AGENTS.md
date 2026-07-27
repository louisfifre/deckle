---
description: Trip-preparation domain — seven-type model, closed vocabularies, guarded operations, externalized terms.
type: agent-instructions
---

# AGENTS.md — Deckle.Travel

`Deckle.Travel` is the custom Travel MCP. It owns the trip-preparation domain:
a seven-type model, its closed vocabularies, guarded operations, and their
bounded model-facing MCP surface. It uses `Deckle.Anytype` as its storage
provider and plugs into the resident transport through `Deckle.Anytype.Mcp`.
The space alias `travel` resolves to the personal Travel space at runtime.
Space and code are English; every user-visible label is localized through the
terms file.

## Privacy boundary

Trip data is personal. Space ids, object ids, tokens, and stored values are
runtime coordinates only; public examples and tests use fictional trips.

Trip data MUST NOT be sent to any remote model or service, by any gesture or
future pipeline — receipt capture (photos, dictation) included, whenever it
exists. Local inference and the Anytype backend's own sync are the only
processing this domain admits.

## Domain model

Seven types. The Activity is the central object of a stay.

The Stay is the membership root: activities, transfers, and expenses link to
it. The Stage carries the where-over-time — « where I am from the 12th to the
15th » — and is mandatory so the stay's timeline is always covered; a
single-city stay gets one degenerate stage, created in the same gesture as the
stay. A dated object is situated in its stage by its Date — an explicit stage
link would duplicate the date, so none exists. The Lodging is the one
deliberate exception: it links its Stage because its where and the stage's
where are the same fact, and its arrival/departure span straddles stage
boundaries where date resolution is ambiguous. A Transfer moves between
stages and belongs to neither.

- **Stay** — a trip already validated elsewhere; the space prepares it, never
  justifies it — no estimation properties. Its budget is the live aggregation
  of its linked expenses, computed on read, never stored.
- **Stage** — stay link, start/end dates. Its body hosts the day-to-day
  account when the user wants one.
- **Place** — the durable « where » registry (museums included); enters only
  when worth keeping. Stable, filterable properties: category, accessibility,
  visit duration, address, official site. Perishable facts (closing days,
  prices, booking requirements) go to the body, dated at collection.
- **Activity** — the planned « doing »: walk, visit, evening, sport, meal,
  other. No status property: the day-level Date is the state (unset = pool,
  set = fixed, past = done); RDV carries the time when it binds. Optional
  duration; linked Place, files (GPX), Expense, and Stay.
- **Transfer** — a booked movement: Date + RDV (departure time), closed mode
  vocabulary (plane, train, bus, ferry, car), confirmation reference, ticket
  files, linked Expense and Stay. No structured origin/destination — the name
  carries the route. Accepted cost: transfers are few per stay and read
  chronologically, so search offers no place filter over them.
- **Lodging** — stage link, arrival/departure (check-in time matters),
  address, confirmation reference, files, linked Expense.
- **Expense** — the receipt-level cost register: amount, date, category,
  stay. Nothing else; the amount is written only once certain (paid, or
  booked at a known price). Rich objects link to their expense; orphan
  receipts stay unlinked.

## Schema

Type and property keys are the normative English contract; an applied key
survives creation as sent, under its localized label. Tag-option keys do not:
Anytype derives them from labels at creation, so they are provider-local —
never assume a manifest option key survived; resolve options by key or
applied label, as the validation and the gestures already do.

Provisioning is lazy, as in Home — initialize and tools/list answer before
any schema work — and validation fails closed before any write, pointing at
schema-admin.

The `files` properties are declared in the contract, but the upload path
(`POST /v1/spaces/:id/files`) is not wired in `Deckle.Anytype` yet, and the
payload key attaching a file at object creation is unverified; until then,
files reach objects through the Anytype app.

## Guarded operations

All writes pass through the domain guards before reaching Anytype. The MCP
catalog may validate argument shape, but it MUST NOT reimplement vocabulary,
stay-resolution, or relation rules. Transport, bearers, and MCP sessions stay
in `Deckle.Anytype.Mcp`.

Expense category is mandatory and must match the closed vocabulary; a missing
fit means the vocabulary lacks an option, and options are added by the user in
Anytype, never by the surface. The expense's stay resolves from its date when
exactly one stay covers it; otherwise the gesture requires the stay explicitly
and fails clearly rather than guessing.

The surface builds, plans, records, and reads. It exposes no deletion and
MUST NOT grow one; the user deletes in the app.

There is no code grammar: trips identify by destination and dates, objects by
name and links. Do not introduce one.

## Wording

Code and docs are English. Every label visible inside Anytype — type names,
property names, closed-vocabulary options — lives in the module's terms file,
French today; adding a language must remain a file addition, deferred to the
app-wide language pass (tracked in Anytype: « Revue multilingue »).
