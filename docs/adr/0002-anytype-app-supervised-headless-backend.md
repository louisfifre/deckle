---
description: Deckle supervises Anytype's headless backend from the resident app, in the interactive user context.
type: adr
---

# ADR-0002 — Supervise the Anytype headless backend from Deckle's resident app

**Status** — accepted 2026-07-09, supersedes [ADR-0001](0001-anytype-headless-service-single-http-mcp-host.md)

## Context

[ADR-0001](0001-anytype-headless-service-single-http-mcp-host.md) put the headless `anytype-cli` backend behind one HTTP MCP host, serialized writes, and assigned backend lifecycle to a Windows user service. Live Windows proof invalidated the lifecycle mechanism: a service loses the interactive user's keyring/DPAPI context, while the triggerless scheduled-task alternative added its own console shutdown failure mode.

Deckle is already a resident process in the interactive user session. It can launch or adopt a windowless backend, retain the process handle for health supervision, and stop supervising without terminating the already-warm backend.

## Decision

Deckle installs the headless `anytype-cli` backend and supervises it from the resident app process. The app launches or adopts `anytype-cli serve` windowless in the interactive user context, watches it while Deckle lives, and does not terminate it on app shutdown. A rebuild or crash stops supervision, not the backend.

The rest of ADR-0001's architecture remains: external clients use the single HTTP MCP host in Deckle's resident core, internal tools call `Deckle.Anytype` in-process, and writes pass through one single-consumer gate with `SpaceWriteLock` as a cross-process backstop.

## Consequences

The backend keeps access to the interactive user's keyring and survives Deckle restarts without a Windows service or scheduled-task console. Deckle now owns install, adoption, health checks and a supervisor loop inside the resident app. A stdio-only client still needs a deferred thin stdio-to-HTTP gateway.

## Options considered

- **Windows user service** — detached from Deckle, but cannot use the interactive user's keyring/DPAPI context.
- **Triggerless scheduled task** — runs as the user, but the tested console path introduced a shutdown failure mode.
- **Child process killed with Deckle** — simple ownership, but rebuilds and crashes would repeatedly tear down the warm backend.
