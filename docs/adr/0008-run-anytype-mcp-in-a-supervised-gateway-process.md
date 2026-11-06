---
description: The Anytype MCP host runs in its own gateway process that Deckle stages by content fingerprint and supervises without owning its life.
---

# ADR-0008 — Run the Anytype MCP host in a supervised gateway process

**Status** — accepted 2026-11-06, supersedes the MCP host placement of [ADR-0002](0002-anytype-app-supervised-headless-backend.md)

## Context

[ADR-0002](0002-anytype-app-supervised-headless-backend.md) kept the single HTTP MCP host inside Deckle's resident app. The backend survived Deckle restarts, but the host did not: every relaunch and every rebuild closed the port, and every assistant session lost its Anytype access until it reconnected. The maintainer's requirement is that an MCP be available at all times, for every session and every harness, with no reconnection or manual step.

[ADR-0001](0001-anytype-headless-service-single-http-mcp-host.md) rejected a separate host because writes had to be serialized at one in-process point. The code does not rely on that point: every mutating gesture already holds `SpaceWriteLock`, an OS file lock shared across processes, for its whole read-modify-write. What ties the host to the app is the delete's second validation, a toast only the app can show.

A process locks its own image and DLLs. A host run from the build output would make every rebuild of a shared library fail.

## Decision

The MCP host, its client tokens and its client profiles (Home included) MUST run in `Deckle.Anytype.Gateway`, a windowless executable separate from Deckle.App. Deckle.App MUST NOT reference the MCP host.

Deckle.App ships the gateway's build output beside Deckle.exe and MUST run it only from a copy staged under `Deckle Providers\Anytype Gateway\versions\<fingerprint>`, where the fingerprint hashes the payload's files and content. At start, Deckle adopts the gateway listening on the port when its image is the staged executable of this build. It asks a gateway of another build to drain and exit, then starts its own. It refuses to touch a port held by any other program. It restarts an exited gateway on a ladder and MUST NOT stop it on shutdown. The gateway is therefore down only when its code, or a library it carries, changed.

Libraries MUST leave the commit id out of their informational version, so that a build's bytes follow its code and an unrelated commit does not read as a new gateway.

The resident app keeps the backend supervision of ADR-0002 and answers the gateway's delete approvals over a named pipe restricted to the current user. With no app listening, a delete fails closed as `Unavailable`. Writes stay serialized by `SpaceWriteLock`. The bearer, the stateless transport and the request headers of [ADR-0005](0005-serve-anytype-mcp-stateless-with-explicit-recovery.md) are unchanged.

## Consequences

- **Easier.** A Deckle relaunch or rebuild no longer cuts assistants off. Anytype code builds into a small graph that Deckle.App no longer links, and the app sheds ASP.NET Core and the MCP SDK.
- **Harder.** Deckle supervises a second resident process. The gateway keeps an Anytype key, token or code change until it is restarted: changing its build restarts it, a key change needs its process ended.
- **Two log files.** The gateway writes its own `gateway.jsonl`, and Deckle's log tells only the supervision side.
- **Bins and Deckle.** The delete cannot bin anything while Deckle is closed.
- **Publish.** The publish script must ship a self-contained gateway in `Gateway\`, because the development payload is framework-dependent.
- **Re-evaluate** when Deckle supervises more out-of-process modules. A generic module host would then replace this gateway-specific supervisor.

## Options considered

- **Keep the host in Deckle.** No new process, but every relaunch and rebuild cuts every session.
- **A stdio relay per client.** It hides a short gap by retrying, but the host still goes down. Each session spawns and locks an executable, and a minutes-long rebuild still breaks calls.
- **Run the gateway from the build output.** Simpler staging, but a running gateway locks the DLLs a rebuild must overwrite.
