---
description: Deckle provisions specialized Anytype spaces from versioned composed plans and resolves live identities during installation.
type: adr
---

# ADR-0003 — Provision specialized Anytype spaces from versioned plans

**Status** — accepted 2026-07-28

## Context

Deckle needs one-click setup for several Anytype spaces whose domains, tool surfaces and user-facing vocabulary differ. Keeping those domains in one universal space would make every installation carry irrelevant types and would widen each capability's effective data boundary. Defining every specialized space independently would preserve that boundary but duplicate transverse concepts and let them drift.

Anytype assigns opaque identities to spaces and schema elements in each installation. Those identities cannot serve as portable authored keys. The local API can create spaces, types, properties, options and objects, but it cannot yet write native templates or views. Localized labels must also remain independent from the identifiers consumed by code.

## Decision

Deckle MUST treat versioned space plans as the authored source of truth for provisionable Anytype spaces. Each specialized space owns one plan. Plans compose transverse plan fragments; definitions that carry space-specific meaning remain local to their plan. Composition, not a base-plan inheritance hierarchy, carries reuse.

Plan keys and code identifiers MUST be stable English names. User-facing names and descriptions MUST come from localized resources. Plans MUST NOT contain opaque Anytype identities: the provisioner creates or discovers live elements during installation and records the resulting key-to-id bindings in installation state.

The provisioner's contract includes only resources the local API can write. Native templates and views remain outside the plan until the API exposes supported write operations for them. The setup provisioner remains distinct from the interactive schema-admin surface.

## Consequences

Deckle can install separate domain spaces while sharing genuinely transverse concepts without copying complete schemas. A plan remains portable across accounts, installations and locales because its authored identity does not depend on live ids or display text. Plan versions give later schema evolution an explicit input, although the migration and reconciliation policy remains a separate decision.

The provisioner must validate composed plans, detect key collisions, resolve dependencies and persist live identity bindings. A one-click installation cannot reproduce native templates or views yet, so the first delivered spaces will still require either API evolution or a bounded manual finishing step for those features.

## Options considered

- **One universal Deckle space** — one schema to install, but unrelated domains, permissions and tool capabilities become coupled.
- **One complete independent definition per space** — preserves isolation, but transverse concepts are copied and drift independently.
- **Base plan with per-space inheritance** — centralizes reuse, but makes local meaning depend on an implicit parent and encourages override chains.
- **Clone a reference space or freeze its live ids** — mirrors today's installation quickly, but is not portable across accounts, locales or fresh installs and makes mutable live state the source of truth.
