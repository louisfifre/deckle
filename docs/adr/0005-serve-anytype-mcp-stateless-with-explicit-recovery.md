---
description: The resident Anytype gateway uses stateless MCP requests and advertises an explicit ambiguous-outcome policy for every tool.
type: adr
---

# ADR-0005 — Serve Anytype MCP stateless with explicit recovery contracts

**Status** — accepted 2026-08-10, extends [ADR-0002](0002-anytype-app-supervised-headless-backend.md)

## Context

The handwritten HTTP MCP transport kept a session id, a current report and schema previews in process memory. A Deckle restart discarded that state even though external clients returned to the same gateway URL. Client reconnection and retry behavior also differs, so server correctness cannot depend on a particular client recreating a session or replaying a request.

Tool calls have different failure semantics. Reads and explicit sets can be repeated, while a report, object, chat, message or upload may have committed before its response was lost. Anytype exposes no universal idempotency key or lookup that can reconcile every such write.

## Decision

The resident gateway MUST use the official C# MCP SDK v2 stateless Streamable HTTP transport on the fixed loopback URL. It MUST authenticate every request by bearer and rebuild that bearer's capability surface for the request. The same endpoint SHOULD accept down-level clients supported by the SDK. Deckle promises that a fresh request works at the same URL and credential after restart; it does not promise that every client retries automatically.

No tool contract MAY rely on hidden transport-session state. A created report id MUST be returned and passed explicitly to later journal writes. A schema preview MUST be a deterministic, self-verifying handle that can be recomputed from the repeated manifest and live plan after a restart.

Every tool descriptor MUST declare whether its effect is read-only, mutating or destructive and whether an ambiguous outcome is safe to retry, must be verified, requires durable deduplication, or remains uncertain. The gateway MUST project the generic contract into standard MCP annotations and Deckle metadata without branching on tool names.

The REST client MUST replay a `5xx` response only for an endpoint whose exact wire effect is known to be safe. It MUST NOT blindly replay creators, uploads, messages, collection mutations or deletes. A JSON-RPC request id is transport correlation, not a durable operation receipt. Operations without a provider reconciliation seam MUST remain explicitly uncertain until such a receipt can be implemented honestly.

## Consequences

The gateway carries no transport session to recover, and its capability boundary remains stable across Deckle restarts. `log` callers must carry a report id, and schema apply callers must repeat the reviewed manifest. The official SDK owns protocol evolution, negotiation and framing instead of Deckle's handwritten dispatcher.

Some mutations remain unable to promise exactly-once execution. Their metadata tells a client whether to retry, verify or stop, but durable deduplication still requires operation-specific receipts and provider reconciliation. Kestrel and the SDK also increase the managed dependency and deployment surface.

## Options considered

- **Keep the handwritten sessionful transport** — preserves implicit state, but makes restart recovery and protocol evolution Deckle's responsibility.
- **Persist sessions keyed by bearer** — hides the restart, but becomes ambiguous when concurrent clients share a bearer and turns transport state into domain state.
- **Treat the JSON-RPC id as an idempotency key** — available on every request, but not durable, not defined as business identity and insufficient to reconcile an unknown provider commit.
