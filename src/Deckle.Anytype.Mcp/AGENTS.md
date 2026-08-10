---
description: Resident MCP host and reusable Anytype surfaces, extensible by domain MCP adapters.
type: agent-instructions
---

# AGENTS.md — Deckle.Anytype.Mcp

Protocol adapter over `Deckle.Anytype`. This module exposes gestures to external clients through one resident streamable-HTTP MCP host. It does not talk to the Anytype REST API directly and must not grow domain rules that belong in the core module.

## Boundaries

The HTTP host owns transport concerns: loopback listener, bearer authentication, stateless MCP requests, request size limits, JSON-RPC framing, and per-client tool surfaces. The official C# SDK owns protocol negotiation and framing; Deckle does not implement a parallel dispatcher or retain transport sessions.

The tool catalogs own model-facing command shape: tool names, descriptions,
input and output JSON schemas, argument validation, text fallbacks, structured
results, and dispatch into gesture methods. A catalog may explain how to use a
capability, but the actual Anytype payload belongs in `Deckle.Anytype`.

Reusable, bounded Anytype mutations live in `AnytypeUtilityToolCatalog`, separate
from schema provisioning and from every custom MCP catalog. The schema-admin
surface mounts these utilities for now; this separation is the seam a future
installer can use to select utilities independently.

`McpSurface` is the extension seam: a client points to one surface that builds a
fresh tool graph for each request. Reusable Anytype utilities stay here; each
bounded use owns its domain, catalog, descriptor and client profile in one
sibling module, and the application composition root plugs it into the host.

The current project-management and dialogue catalogs predate this boundary and
still live here — migration debt, not a precedent: they must move to sibling
custom MCP modules like `Deckle.Home`. Schema administration is a transverse
Anytype utility and remains here. Until those legacy surfaces move, their
membership stays deliberate; destructive tools stay in the management catalog
only.

## Adding A Tool

Add reusable Anytype behavior in `Deckle.Anytype` first, then expose it here.
Domain behavior and its catalog belong in the domain's MCP adapter. Keep input
schemas strict object schemas with `additionalProperties:false`; a shape mistake
should return an `isError:true` tool result the model can correct.

## Tests

Catalog tests pin the advertised tool names and schema discipline. Toolset tests pin which client/profile sees which surface. HTTP host tests pin transport behavior; do not use them for domain payload checks.
