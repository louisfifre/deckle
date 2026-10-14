---
name: glossary-deckle-anytype
description: "Anytype integration vocabulary, Deckle.Anytype.Mcp included. It separates the backend, the MCP host and the resident core, and defines the surfaces, the bot and the client token, and the Home space types. Read before touching the integration or its authorship model."
---

# Deckle.Anytype, Glossary

Vocabulary of the Anytype and MCP integration, shared with `Deckle.Anytype.Mcp`. Three layers are constantly conflated, the data runtime, the protocol adapter and the Deckle process that hosts it. The words "bot" and "token" carry the question of authorship.

## Runtime, host, surfaces

**Anytype backend** :
The live, headless Anytype runtime that holds the data and serves the local REST API. Deckle orchestrates its lifecycle and access but never owns or reimplements it.
_Avoid_ : Anytype Desktop (the GUI, no longer a runtime dependency), MCP server (a different layer).

**Anytype provider** :
The installed, versioned distribution of Anytype executables from which Deckle may launch an Anytype backend. Its versions are immutable and live outside Deckle's replaceable payload. One atomic activation selects the next version to launch without interrupting a healthy backend.
_Avoid_ : backend (the live runtime), application payload (replaced during a Deckle update).

**Backend reconciliation** :
The single decision, shared across processes, that inspects the Anytype endpoint and then adopts a trusted backend that is warming or serving, or launches the activated provider. It succeeds only when it can positively tell which process owns the listener.
_Avoid_ : health check (one observation inside reconciliation), supervision (the longer-lived watch and restart loop).

**MCP host** :
The single stateless adapter that exposes the `Deckle.Anytype` gestures to external clients over HTTP, from Deckle's resident core. Each request is authenticated by its client token and opens the matching capability surface. No transport session carries domain state.
_Avoid_ : backend (the data runtime, a different layer), Anytype server (could mean the backend or the host).

**Deckle resident core** :
The Deckle process that is always on and starts at login, holding the global hotkeys and the orchestration. It hosts the MCP host and the library. It differs from the visible windows that come and go, such as the HUD or Settings.

**MCP surface** :
A capability graph that the host opens for one authenticated request, such as PM, Dialogue or Home. The unit of separation is the *capability*, never a transport session.
_Avoid_ : profile (the earlier name), server (there is only one).

**Operation recovery policy** :
The contract each tool states for an ambiguous response. It says whether the call is safe to retry, must be verified before a retry, needs durable deduplication, or stays uncertain. It describes what a caller may conclude about the effect on the domain. A JSON-RPC request id is never a receipt for an operation.
_Avoid_ : HTTP retry policy (the lower transport decision), idempotency hint alone (only one projection of the fuller contract).

**Schema admin surface** :
The MCP surface dedicated to schema administration. It inspects spaces, plans changes to types, properties, tags and templates, previews the diff against a live space, applies confirmed changes, and freezes the measured result into code. Its changes to a space are guarded. Its preview names every change before it applies.
_Avoid_ : putting type/property creation inside Home tools (schema changes stay in this surface), generic Anytype MCP (too unconstrained for Deckle's guarded workflow).

**Anytype Desktop** :
The graphical Anytype application. Deckle no longer depends on it at runtime, and the runtime it uses is the Anytype backend.

**MCP server** :
In ordinary MCP use, any program that serves tools to clients. In Deckle, the layer that exposes Anytype operations is the MCP host. The data runtime is the Anytype backend.

**Application payload** :
The files of Deckle itself, replaced when Deckle updates. The Anytype provider lives outside it, so an update leaves the provider in place.

**Health check** :
A single probe that asks whether a service answers. It is one observation inside backend reconciliation, not the whole decision.

**Supervision** :
The long-lived watch that restarts a process when it fails. Backend reconciliation is one decision, not that loop.

**Anytype server** :
A loose name that could mean the Anytype backend or the MCP host. Deckle names each layer separately.

**Profile** :
The earlier name for an MCP surface, now retired. The word keeps another meaning in the rewrite service's profile store.

**Server** :
In ordinary use, a program that answers requests. Deckle has only one MCP host, so a capability graph opened for a request is a surface.

**HTTP retry policy** :
The transport's rule for resending a failed HTTP request. It sits below the operation recovery policy and says nothing about the effect on the domain.

**Idempotency hint** :
A flag that says whether repeating a call is harmless. Alone, it is only one projection of the operation recovery policy.

**Putting type/property creation inside Home tools** :
A design in which Home operations would also create Anytype types and properties. Deckle keeps schema changes in the schema admin surface.

**Generic Anytype MCP** :
An MCP server that exposes every Anytype operation without limits. Deckle's guarded workflow needs the narrower schema admin surface.

## Space provisioning

**Space plan** :
The versioned, declarative source of truth for one specialized Anytype space. It names the intended domain vocabulary through stable plan keys. It composes shared plan fragments with definitions that only this space owns. It holds no opaque id discovered from a live installation, and no native Anytype template or view definition.
_Avoid_ : template (an Anytype-native concept the local API cannot write), schema snapshot (describes what was measured, not what Deckle intends to install), space manifest (too generic to carry the source-of-truth meaning).

**Plan fragment** :
A unit of domain vocabulary that more than one space plan shares and composes. It represents one transverse concept, without owning a space or absorbing definitions that only look alike.
_Avoid_ : base plan (implies inheritance and one privileged parent), shared schema (suggests one live schema rather than reuse at definition time).

**Space-local definition** :
A unit of domain vocabulary, such as a type, a property or an option, that exactly one specialized space owns. It stays beside that space plan. A second definition that looks similar does not make it transverse until the two meanings are truly the same.
_Avoid_ : special case (it is the normal home of domain-specific vocabulary), fragment (reserved for concepts intentionally shared across plans).

**Plan key** :
A stable English identifier written in a space plan and used by code and localization resources. It identifies an intended concept across languages. It is neither the label users see nor an opaque Anytype id.
_Avoid_ : Anytype key (may mean the live API key), label (localized presentation, see `src/Deckle.Home/GLOSSARY.md`), id (resolved from the installed space).

**Space provisioner** :
The setup capability that turns a selected space plan into an installed specialized Anytype space and records the live identities it discovers. It is a deterministic installation path. It differs from the schema admin surface, which serves interactive inspection and guarded maintenance.
_Avoid_ : schema admin (different workflow), space generator (suggests throwaway output rather than a versioned installation contract).

**Template** :
An Anytype-native object that pre-fills new objects of a type. The local API cannot write one, so a space plan never contains it.

**Schema snapshot** :
A record of the schema measured in a live space. It describes what exists, while the space plan describes what Deckle intends to install.

**Space manifest** :
A generic name for a file that lists the contents of a space. It is too generic to carry the meaning of source of truth that the space plan holds.

**Base plan** :
A parent plan that other plans inherit from. Plan fragments compose without any privileged parent.

**Shared schema** :
One live schema used by several spaces at once. Plan fragments are reused when plans are defined, not shared live.

**Special case** :
An exception to a general rule. A space-local definition is the normal home of domain-specific vocabulary.

**Anytype key** :
A key in the Anytype sense, which may mean the API key of the live backend. It is too ambiguous to name the stable identifier of a plan, which is a plan key.

**Id** :
An identifier that Anytype resolves in one installed space. It changes from one installation to another, while a plan key stays the same.

**Space generator** :
A tool that produces a space once, as throwaway output. The space provisioner follows a versioned installation contract.

## Home space types

**Étage** :
The first spatial level of the Home space. An étage contains one or more pièces.

**Pièce** :
The second spatial level of the Home space. A pièce belongs to one étage and contains one or more emplacements.

**Emplacement** :
The third spatial level of the Home space. It is a precise physical place where equipment can be installed, observed, fixed, embedded or linked. An emplacement belongs to one pièce.
_Avoid_ : Endroit (too vague), Zone (rejected as a catch-all spatial type).

**Home object type** :
An Anytype type of the Home space that represents a stable spatial or material family, never a catch-all inventory bucket. Variants inside a family stay properties or categories. Families with distinct properties and links become distinct types.
_Avoid_ : Element (rejected as too generic for the Home inventory), one type per tiny variant (variants stay properties or categories).

**Endroit** :
A French word for any place. It is too vague for the third spatial level, which is the emplacement.

**Zone** :
A word for an area of any size. It was rejected because it turns into a catch-all spatial type.

**Element** :
A generic word for any part of a whole. It was rejected as a Home type because it would hold everything.

**One type per tiny variant** :
A schema that creates a new type for every small difference. Deckle keeps such variants as properties or categories of one Home object type.

## Authorship

**Bot** :
An Anytype account distinct from Louis, under which the headless backend writes. It is invited into each space. One headless backend means one account and one author. There is one bot to start.
_Avoid_ : user (the person who uses Deckle, a separate account), API key (an access credential, not the identity).

**Client token** :
The bearer credential each client presents to the MCP host. It carries *access*, meaning which surfaces and spaces are allowed. It never carries *authorship*, since the author is always the backend's bot.
_Avoid_ : identity (the bot signs writes, not the token), account key (tied to an account, while the token is tied to access).

**User** :
The person who uses Deckle, here Louis. The bot is a separate account, so it is never called a user.

**API key** :
A credential that grants access to an API. It carries access, not identity, so it never names the bot.

**Identity** :
In ordinary use, the attribute that tells a system who someone is. A client token carries access only, and the identity that signs writes is the bot.

**Account key** :
A key tied to an account and its identity. A client token is tied to access, not to an account.

### Example conversation

> - Le « MCP Anytype », c'est l'exe qu'on lançait ?
> - Plus maintenant. L'**hôte MCP** est un seul serveur HTTP dans le **noyau résident**. Les clients s'y connectent par URL. L'exe spawné, c'était le monde stdio.
> - Et quand Codex crée une tâche, c'est qui l'auteur ?
> - Le **bot** unique du **backend**. Le **jeton** de Codex ne dit que ce qu'il a le droit de toucher, pas qui signe.
