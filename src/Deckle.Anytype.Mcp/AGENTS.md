---
description: Anytype MCP adapter — streamable-HTTP host, client surfaces, and JSON-RPC tool catalog over Deckle.Anytype gestures.
type: agent-instructions
---

# AGENTS.md — Deckle.Anytype.Mcp

Protocol adapter over `Deckle.Anytype`. This module exposes gestures to external clients through one resident streamable-HTTP MCP host. It does not talk to the Anytype REST API directly and must not grow domain rules that belong in the core module.

## Boundaries

The HTTP host owns transport concerns: loopback listener, bearer authentication, MCP sessions, request size limits, JSON-RPC framing, and per-client tool surfaces.

The tool catalogs own model-facing command shape: tool names, descriptions, JSON schemas, argument validation, and dispatch into gesture methods. A catalog may explain how to use a capability, but the actual Anytype payload belongs in `Deckle.Anytype`.

`McpToolset` is the composition seam. It builds a fresh gesture graph per MCP session because session gestures carry current-report state.

## Adding A Tool

Add the behavior in `Deckle.Anytype` first, then expose it here. Keep input schemas strict object schemas with `additionalProperties:false`; a shape mistake should return an `isError:true` tool result the model can correct.

Surface membership is deliberate. Project-management tools belong to the project-management and all profiles; dialogue tools belong to dialogue and all profiles; destructive tools stay in the management catalog only.

## Tests

Catalog tests pin the advertised tool names and schema discipline. Toolset tests pin which client/profile sees which surface. HTTP host tests pin transport behavior; do not use them for domain payload checks.
