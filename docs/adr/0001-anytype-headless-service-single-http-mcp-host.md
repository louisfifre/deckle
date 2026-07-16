---
description: Anytype runs as a Deckle-orchestrated headless service behind one HTTP MCP host that serializes writes.
type: adr
---

# ADR-0001 — Anytype via a Deckle-orchestrated headless service behind a single HTTP MCP host

**Status** — superseded by [ADR-0002](0002-anytype-app-supervised-headless-backend.md)

## Context

Deckle exposes Louis's Anytype space to external AI clients (Claude Code, Codex) and, later, to its own internal tools. The starting shape had each client spawn its own stdio MCP host pointing at Anytype Desktop's REST API (`31009`), with a brittle install script publishing the host exe into the user-data folder and editing only `.claude.json`. Four forces collided: Desktop must stop being a runtime dependency; clients lock the spawned exe and break rebuilds; writes must be attributable to a bot, not Louis's personal account; and several sessions may write at once against a backend whose REST API has **no optimistic concurrency** and replaces an object's whole body on every PATCH (a verified lost-update hazard).

## Decision

We will run the Anytype backend as a **headless `anytype-cli` instance** (embedding `heart`, REST on the fixed `127.0.0.1:31012`), installed and supervised by Deckle as a **Windows user service** — not Anytype Desktop, and not a child process of the Deckle app (so a Deckle rebuild or crash never tears the backend down). Deckle orchestrates the service's lifecycle and access provisioning but never owns or reimplements the data.

External clients reach Anytype through a **single HTTP MCP host living in Deckle's resident core**, connecting by URL with a per-client bearer token; internal Deckle tools bypass the MCP and call the `Deckle.Anytype` library in-process. The host MUST **serialize writes** through a single-consumer gate (with the existing cross-process `SpaceWriteLock` kept as a backstop), since transport choice does not reduce backend contention — it only decides whether a coordination point exists.

## Consequences

Easier: the exe-lock and the `current`-junction install machinery disappear (no client-spawned binary); multi-client config becomes a URL; the backend survives Deckle restarts; writes carry a bot identity and cannot interleave into a lost update. Harder: Deckle now owns a service's install/health/provisioning, and a stdio-only client (e.g. the Claude Desktop chat) needs a deferred thin stdio→HTTP gateway. Deferred, not closed: multiple bot accounts for per-author attribution in the Anytype graph (each would be its own headless instance with an independent sync — rejected for now on that cost).

## Options considered

- **stdio host per client (status quo)** — universal client support, but N blind processes lock the exe and share no in-process point to serialize the non-concurrent backend; coordination must live in an external lock or not at all.
- **MCP host as a separate Windows service** — always up without any Deckle process, but duplicates the library host and adds a second service to supervise, for a resident-core that already exists.
- **Multiple bot accounts (one per client)** — true per-author attribution in Anytype's graph, but each is a full `heart` node re-syncing every shared space independently; the duplicated Dev-space sync is not worth it. Per-client *access* differences are kept via host-side token scoping instead.
