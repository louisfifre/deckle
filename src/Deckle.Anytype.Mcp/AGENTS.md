---
description: Resident MCP host and reusable Anytype tool surfaces.
type: agent-instructions
---

# AGENTS.md — Deckle.Anytype.Mcp

## Boundaries

- No direct Anytype REST call and no domain rules here.
- The host owns transport; the official C# SDK owns the protocol. No parallel dispatcher, no retained transport sessions.
- Catalogs own the command shape. The Anytype payload belongs in `Deckle.Anytype`.
- Reusable mutations live in `AnytypeUtilityToolCatalog`, apart from schema provisioning and custom catalogs, so a future installer can select utilities on their own.
- `McpSurface` is the extension seam. Each bounded use is one sibling module owning its domain, catalog, descriptor and client profile, like `Deckle.Home`.
- The project-management and dialogue catalogs are migration debt that must move out, not a precedent. Schema administration stays.
- Destructive tools live in the management catalog only.

## Adding a tool

- Add reusable behaviour in `Deckle.Anytype` first, then expose it here.
- Input schemas are strict, with `additionalProperties:false`. A shape mistake returns an `isError:true` result.

## Tests

Catalog tests pin tool names and schema discipline. Toolset tests pin which client sees which surface. Host tests pin transport only.
