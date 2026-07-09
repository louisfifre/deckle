---
description: Anytype runs as a Deckle-orchestrated headless backend behind one HTTP MCP host that serializes writes.
type: adr
---

# ADR-0001 — Anytype via a Deckle-orchestrated headless backend behind a single HTTP MCP host

**Status** — accepted, amended 2026-07-09 after the 2026-07-02 lifecycle proof

## Context

Deckle exposes Louis's Anytype space to external AI clients (Claude Code, Codex) and, later, to its own internal tools. The starting shape had each client spawn its own stdio MCP host pointing at Anytype Desktop's REST API (`31009`), with a brittle install script publishing the host exe into the user-data folder and editing only `.claude.json`. Four forces collided: Desktop must stop being a runtime dependency; clients lock the spawned exe and break rebuilds; writes must be attributable to a bot, not Louis's personal account; and several sessions may write at once against a backend whose REST API has **no optimistic concurrency** and replaces an object's whole body on every PATCH (a verified lost-update hazard).

The first lifecycle design targeted a Windows user service or triggerless scheduled task so the backend would not die with Deckle. Live Windows proof changed that trade-off: a service loses the interactive user's keyring/DPAPI context, and the scheduled-task console path added its own shutdown failure mode. The resident app can instead launch/adopt `anytype-cli serve` windowless, supervise its process handle, and stop supervising without killing the already-warm backend.

## Decision

We run the Anytype backend as a **headless `anytype-cli` instance** (embedding `heart`, REST on the fixed `127.0.0.1:31012`), installed by Deckle and supervised from Deckle's resident app process. Deckle launches or adopts a windowless `serve` process in the interactive user context, watches it while the app lives, and does not terminate it on app shutdown; a rebuild or crash stops supervision, not the backend. Deckle orchestrates lifecycle and access provisioning but never owns or reimplements the data.

External clients reach Anytype through a **single HTTP MCP host living in Deckle's resident core**, connecting by URL with a per-client bearer token; internal Deckle tools bypass the MCP and call the `Deckle.Anytype` library in-process. The host MUST **serialize writes** through a single-consumer gate (with the existing cross-process `SpaceWriteLock` kept as a backstop), since transport choice does not reduce backend contention — it only decides whether a coordination point exists.

## Consequences

Easier: the exe-lock and the `current`-junction install machinery disappear (no client-spawned binary); multi-client config becomes a URL; the backend survives Deckle restarts; writes carry a bot identity and cannot interleave into a lost update; the keyring stays in the interactive user's context. Harder: Deckle now owns backend install/health/provisioning and a supervisor loop in the app process, and a stdio-only client (e.g. the Claude Desktop chat) needs a deferred thin stdio→HTTP gateway. Deferred, not closed: multiple bot accounts for per-author attribution in the Anytype graph (each would be its own headless instance with an independent sync — rejected for now on that cost).

## Options considered

- **stdio host per client (status quo)** — universal client support, but N blind processes lock the exe and share no in-process point to serialize the non-concurrent backend; coordination must live in an external lock or not at all.
- **Anytype backend as a Windows service or scheduled task** — more detached on paper, but the service path loses the interactive keyring/DPAPI context and the scheduled-task path proved more fragile than a windowless process supervised by the resident app.
- **MCP host as a separate Windows service** — always up without any Deckle process, but duplicates the library host and adds a second service to supervise, for a resident core that already exists.
- **Multiple bot accounts (one per client)** — true per-author attribution in Anytype's graph, but each is a full `heart` node re-syncing every shared space independently; the duplicated Dev-space sync is not worth it. Per-client *access* differences are kept via host-side token scoping instead.
