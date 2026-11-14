---
description: Space plans carry templates, views, collections, queries and pages, and write their display names in one language, until a space must exist in two.
---

# ADR-0010 — Put templates, views and one-language names in space plans

**Status** — accepted 2026-11-14, supersedes two clauses of [ADR-0003](0003-anytype-specialized-spaces-versioned-plans.md): native templates and views left out of the plan, and user-facing names taken from localized resources

## Context

[ADR-0003](0003-anytype-specialized-spaces-versioned-plans.md) kept templates and views out of space plans because the local API was believed unable to write them. A trial on a throwaway space on 2026-11-14 showed otherwise. `POST /objects` and `POST /templates` write a dataview block whole, with its source, views, columns, sorts and filters. An object born from such a template receives the identical block. The block reads back the same 12 minutes later and after a backend restart. Type views are written through the view operations (`insert_view`, `update_view`, `move_view`, `delete_view`), not as blocks. Without templates in the plan, a rebuilt space lacks the part that shapes every object, because an object copies its template's body once, at birth, and never follows it afterwards.

ADR-0003 also required display names to come from localized resources. Every space Deckle provisions today belongs to one person and is written in French. No space needs a second language yet.

## Decision

A space plan MUST be able to carry templates with their bodies and in-body views, default templates, type views, collections, queries, pages and their views. The provisioner MUST create templates before any object, so that objects are born with their template's body.

A plan MUST write its display names in one language, inline. Properties and types keep stable English keys. Templates, collections, queries and pages are named by their display name inside the plan. Localized resources come back as a new decision the day one space must exist in two languages.

Steps the bot's key cannot write stay outside the provisioner and are named as a manual finishing step for the owner: the sidebar and the views of native types.

## Consequences

- **Easier.** One plan rebuilds a whole space from zero, templates and views included. The same plan checks a live space and reads one back.
- **Harder.** Renaming a template, collection, query or page means renaming every reference to it in the plan. A plan serves one language only.
- **Still manual.** The sidebar, native-type views and the export of an Experience remain the owner's acts in the app.
- **Re-evaluate** when a space must be installed in a second language, or when the API lets the bot write the sidebar.

## Options considered

- **Keep templates and views out of the plan.** Matches ADR-0003, but every rebuild needs a long manual pass and objects are born without their template.
- **Stable keys everywhere and localized resources now.** Faithful to ADR-0003 and ready for a second language, but a second build of the format before any space can be rebuilt, for a need nobody has yet.
