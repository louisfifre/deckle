# CLAUDE.md — Deckle.Anytype

Core library over Louis's Anytype project-management space: a thin HTTP client to the local Anytype REST API plus the PM gestures (sessions, tasks, projects, queries). It is the single home of that logic, with two doors on top: the stdio MCP host (`Deckle.Anytype.Mcp`, thin by design) today, the in-app assistant later. New behavior goes here, never in a door.

`Schema/DevSpace.cs` freezes the live space's type/property/tag keys as code — the single source of truth, including the trap keys that must never be "fixed". When in doubt, re-verify against the live API; do not mirror the map into docs.

Credentials (`api_url`, `api_version`, `api_key`, `space_id`) live in `credentials.json` under `AppPaths.GetModuleDirectory("anytype")` — never in the repo, never in a log line.

Anytype API constraints that shape the code: body PATCH is a full replacement (hence small fresh report objects per session); DELETE archives, nothing is permanently destroyed; rate limit 1 rps sustained — the client serializes calls and retries on 429. Gesture digests are French and token-sober: the consumer is an LLM, every line must earn its tokens.

In the MCP host, stdout belongs to JSON-RPC alone; all observation flows through the `Deckle-Anytype` EventSource into a stderr listener. Dated decisions and measured API facts live in `JOURNAL.md`.
