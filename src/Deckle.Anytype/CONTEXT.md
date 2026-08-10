---
name: context-deckle-anytype
description: "Anytype integration vocabulary (covers Deckle.Anytype.Mcp) — backend vs MCP host vs resident core, surfaces, bot vs client token, and the Home space types. Read before touching the integration or its authorship model."
type: agent-instructions
---

# Deckle.Anytype — Context

Vocabulary of the Anytype/MCP integration, shared with `Deckle.Anytype.Mcp`. Three layers are constantly conflated — the data runtime, the protocol adapter, and the Deckle process that hosts it — and "bot" versus "token" carries the authorship question.

## Runtime, host, surfaces

**Anytype backend** :
The live headless `anytype-cli` runtime (embedding `heart`) that holds the data and serves the local REST API on `127.0.0.1:31012`. Spawned and supervised by Deckle's resident core, then adopted only when the listener-owning PID belongs to a trusted provider image; Deckle orchestrates its lifecycle and access but never owns or reimplements it.
_Avoid_ : Anytype Desktop (the GUI, no longer a runtime dependency), MCP server (a different layer).

**Anytype provider** :
The installed, versioned executable distribution from which Deckle may launch an Anytype backend. Versions are immutable and live outside Deckle's replaceable payload; one atomic activation selects the next version to launch without interrupting a healthy backend.
_Avoid_ : backend (the live runtime), application payload (replaced during a Deckle update).

**Backend reconciliation** :
The single cross-process decision that inspects the Anytype endpoint, adopts a trusted warming or serving backend, or launches the activated provider. It succeeds only with positive listener-owner attribution and runs under the named reconciliation lease.
_Avoid_ : health check (one observation inside reconciliation), supervision (the longer-lived watch and restart loop).

**MCP host** :
The single stateless adapter that exposes the `Deckle.Anytype` gestures to external clients over HTTP, from Deckle's resident core. Every request authenticates its client token and opens the corresponding capability surface; no transport session carries domain state.
_Avoid_ : backend, Anytype server.

**Deckle resident core** :
The always-on Deckle process (global hotkeys, orchestration) that hosts the MCP host and the lib and starts at login — distinct from the visible windows (HUD, Settings) that come and go.

**MCP surface** :
A capability graph opened by the host for one authenticated request — PM, Dialogue, Home. The unit of separation is the *capability*, never a transport session.
_Avoid_ : profile (the earlier name), server (there is only one).

**Operation recovery policy** :
The tool-level contract for an ambiguous response: safe to retry, verify before retry, requires durable deduplication, or uncertain. It describes what a caller may conclude about the domain effect; a JSON-RPC request id is never an operation receipt.
_Avoid_ : HTTP retry policy (the lower transport decision), idempotency hint alone (only one projection of the fuller contract).

**Home surface** :
The Anytype MCP surface dedicated to structured home knowledge: rooms, infrastructure, equipment, observations, links, and later home-management functions. It writes to a dedicated home Anytype space rather than the Dev project-management space; clients do not choose a space per call.
_Avoid_ : Cartographie (too narrow for the intended long-term scope), Électrique (too narrow), Maison surface (French label rejected for the surface name), Maison générique (too broad).

**Schema admin surface** :
The Anytype MCP surface dedicated to schema administration: inspecting spaces, planning type/property/tag/template changes, previewing the diff against a live space, applying confirmed schema changes, and freezing the measured result into code. Its first scope is additive only: create or attach types, properties, tags and templates; no delete, key rename, property format change, or property removal. Cross-space work uses Deckle-configured aliases such as `dev` and `home`, never a free `space_id` argument.
_Avoid_ : putting type/property creation inside Home tools, generic Anytype MCP (too unconstrained for Deckle's guarded workflow).

## Space provisioning

**Space plan** :
The versioned declarative source of truth for one specialized Anytype space. It names the intended domain vocabulary through stable plan keys and composes transverse plan fragments with definitions owned only by that space. It contains no opaque ids discovered from a live installation and no native Anytype template or view definition.
_Avoid_ : template (an Anytype-native concept the local API cannot write), schema snapshot (describes what was measured, not what Deckle intends to install), space manifest (too generic to carry the source-of-truth meaning).

**Plan fragment** :
A composable unit of domain vocabulary shared by more than one space plan. It represents one transverse concept without owning a space or absorbing definitions that only resemble each other.
_Avoid_ : base plan (implies inheritance and one privileged parent), shared schema (suggests one live schema rather than reuse at definition time).

**Space-local definition** :
A type, property, option, or other planned concept owned by exactly one specialized space. It stays beside that space plan; a second superficially similar definition does not make it transverse until the two meanings are genuinely the same.
_Avoid_ : special case (it is the normal home of domain-specific vocabulary), fragment (reserved for concepts intentionally shared across plans).

**Plan key** :
A stable English identifier authored in a space plan and consumed by code and localization resources. It identifies an intended concept across locales; it is neither the user-facing label nor an opaque Anytype id.
_Avoid_ : Anytype key (may mean the live API key), label (localized presentation), id (resolved from the installed space).

**Space provisioner** :
The setup capability that turns a selected space plan into an installed specialized Anytype space and records the live identities it discovers. It is a deterministic installation path, distinct from the schema admin surface used for interactive inspection and guarded maintenance.
_Avoid_ : schema admin (different workflow), space generator (suggests throwaway output rather than a versioned installation contract).

## Home space types

**Étage** :
The first spatial level in the Home space. An étage contains one or more pièces.

**Pièce** :
The second spatial level in the Home space. A pièce belongs to one étage and contains one or more emplacements.

**Emplacement** :
The third spatial level in the Home space: a precise physical place where equipment can be installed, observed, fixed, embedded, or linked. An emplacement belongs to one pièce.
_Avoid_ : Endroit (too vague), Zone (rejected as a catch-all spatial type).

**Home object type** :
An Anytype type in the Home space that represents a stable spatial or material family, not a catch-all inventory bucket. Variants inside a family stay properties or categories; families with distinct properties and links become distinct types.
_Avoid_ : Element (rejected as too generic for the Home inventory), one type per tiny variant.

## Authorship

**Bot** :
An Anytype account distinct from Louis, under which the headless writes, invited per space. One headless = one account = one author; one bot to start.
_Avoid_ : user, API key (an access credential, not the identity).

**Client token** :
The bearer each client presents to the host; it carries *access* (which surfaces and spaces are allowed), not *authorship* (the author is always the backend's bot).
_Avoid_ : identity, account key.

### Example conversation

> — Le « MCP Anytype », c'est l'exe qu'on lançait ?
> — Plus maintenant. L'**hôte MCP** est un seul serveur HTTP dans le **noyau résident** ; les clients s'y connectent par URL. L'exe spawné, c'était le monde stdio.
> — Et quand Codex crée une tâche, c'est qui l'auteur ?
> — Le **bot** unique du **backend**. Le **jeton** de Codex ne dit que ce qu'il a le droit de toucher, pas qui signe.
