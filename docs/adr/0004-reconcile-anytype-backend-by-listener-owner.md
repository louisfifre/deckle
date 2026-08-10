---
description: Deckle reconciles one externally versioned Anytype provider by proving which trusted process owns the REST listener.
type: adr
---

# ADR-0004 — Reconcile the Anytype backend by listener owner

**Status** — accepted 2026-08-10, extends [ADR-0002](0002-anytype-app-supervised-headless-backend.md)

## Context

ADR-0002 lets the headless backend outlive the Deckle process that started it. An outgoing Deckle and its successor can therefore overlap while the backend is still warming. A healthy response from the fixed REST endpoint proves that something is serving, but not that the process Deckle just spawned owns the listener. Process-name or executable-path scans have the inverse weakness: they can identify a trusted image without proving that it serves the endpoint.

The provider also lived inside Deckle's replaceable application payload. A running image blocked updates, while a stopped image was deleted with the old payload. Preserving the backend across Deckle restarts and updates requires lifecycle identity and provider placement to be one contract.

## Decision

Deckle MUST install Anytype provider versions as immutable directories in a per-user executable root outside the replaceable Deckle payload. Provisioning MUST validate a complete staged version before publishing it and MUST activate it through one atomically replaced manifest. Activation selects the next process to launch; it MUST NOT interrupt a healthy older provider. The legacy payload path MAY remain trusted only for migration.

Every adopt-or-spawn decision MUST run under one named, current-user, cross-session Windows mutex. The mutex MUST cover inspection, a trusted process's warm-up, spawn, listener attribution and readiness. Its thread-affine ownership MUST remain on one dedicated worker, and an abandoned mutex MUST trigger full reinspection.

Deckle MUST accept readiness or adoption only when all of these are simultaneously true: the process handle is live, its executable belongs to the trusted provider set, the Windows TCP owner table attributes `127.0.0.1:31012` to that PID, the health endpoint answers, and a second TCP-table read still attributes the listener to the same PID. A trusted warming process blocks a second spawn. An unknown, ambiguous or unreadable listener owner blocks spawning and surfaces an endpoint conflict.

Before a credentialed request to the supervised headless endpoint, Deckle MUST re-prove that the current listener owner is a live process in the same trusted provider set. A failed proof MUST reject the request locally before the Anytype bearer is attached to the wire. This request boundary complements reconciliation because ownership can change after initial readiness.

The application MUST cancel and drain its complete Anytype runtime task before releasing resident ownership. Draining stops supervision and the MCP gateway; it never terminates the backend merely because Deckle exits.

## Consequences

Two Deckle process sessions can reconcile the same warm backend without sleeps or duplicate spawn. Deckle updates no longer replace or gate on the active provider image, and a failed provider activation leaves the previous version selected. Explicit uninstall now owns the separate provider root as well as the application payload. A lost or replaced listener cannot receive the headless bearer through a still-running MCP gateway; tool calls fail locally until trusted ownership is restored.

The lifecycle is intentionally Windows-specific: it depends on a named mutex, process handles and `GetExtendedTcpTable`. Positive identity costs extra inspection and can choose a visible safe outage when the listener owner cannot be proved. Old provider versions remain on disk until an explicit pruning policy is added.

## Options considered

- **Health plus executable scan** — portable and simple, but neither observation proves that the same PID is healthy.
- **File lock or lease file** — observable on disk, but adds stale-file recovery while Windows already supplies abandonment semantics and user/session scoping.
- **Kill and restart on every Deckle launch** — gives simple ownership, but violates the warm-backend requirement and turns updates into interruptions.
