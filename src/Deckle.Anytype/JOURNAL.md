---
description: Dated decisions and findings for the Anytype MCP server — founding grilling, API facts, tool surface.
type: module-journal
---

# Journal — MCP Anytype

Module-level dated notes. Most recent on top.

## 2026-06-12 — First live migration: list-add answers a bare string

Found while migrating the roadmap into the space: `POST /lists/{id}/objects` answers 200 with the bare JSON string `"Objects added successfully"` — not an object, not an empty body as the client comment assumed. `ParseRoot` threw on it *after* the membership was posted, so every `create_project(epic:…)` failed its report while having fully succeeded. Fixed by skipping body parsing on that endpoint (`parseBody: false`); pinned in `ProjectGesturesTests`.

Also surfaced by live use: an *existing* project cannot join an epic through the MCP — `create_project` only attaches at creation and `link` has no project→epic edge (collection membership, not a property). Logged as a task in the PM space; the two pre-existing projects were attached by direct API call.

## 2026-06-12 — First build: API facts measured, module placed

Module placed per repo shape: `src/Deckle.Anytype` (core, domain) + `src/Deckle.Anytype.Mcp` (stdio host); this journal moved here from `mcp/`. The space schema is frozen as code in `Schema/DevSpace.cs` — single source for keys; the live API is the re-verification path, no doc duplicate.

API facts measured during the build:
- The done state of an action-layout task is the built-in checkbox property `done` (read live on a completed task; the vendor JS does not model it).
- Asymmetry: object **creation** carries the markdown under `body` (POST), **update** under `markdown` (PATCH) — vendor-confirmed, the PATCH side exercised against the live host in the smoke test.
- Collection add: `POST /v1/spaces/{space}/lists/{collectionId}/objects` body `{"objects":[ids]}` — how a project joins an epic.

MCP host implemented against spec revision **2025-11-25** (verified at the source before writing): newline-delimited JSON-RPC 2.0, no batches, mandatory `ping`, version negotiation echoes any of 2025-03-26 / 2025-06-18 / 2025-11-25 and answers 2025-11-25 otherwise. Execution failures (including name-resolution ambiguity, which lists candidates) return `isError:true` results — the channel the model can self-correct on; JSON-RPC errors stay protocol-only.

## 2026-06-12 — Bootstrap: auth and real-space discovery

Interactive bootstrap done against the live local API (Anytype Desktop, challenge auth). The anyproto vendor reference actually lives at `D:\skills\global\anytype-agents-skill-main\` (the old `D:\projects\ai\anytype` path is gone). API version header `2025-11-08` works.

**Credentials home (decided):** `%LOCALAPPDATA%\Deckle\modules\anytype\credentials.json` — `api_url` + `api_version` + `api_key` (long-lived bearer). Follows the AppPaths module-data convention.

**The PM space is `Dev`** (`bafyreibaltekf6yw32suoj3g57ot7gxgmpjwi37k7mx5y6mdd4f3i7p4fa.54yhp4w3lgp`). `Perso` holds unrelated personal types; `Test MCP` is an empty sandbox without the PM types.

**PM types found in Dev:** `epic` (layout collection — projects are members of the collection, no link property on either side), `project`, `task`, `rapport` (note layout), `idee`, `document` — plus a dormant `session` type with zero objects (links tasks + rapports; left untouched).

**Key traps found** (display name ≠ key; the core library owns this map, frozen as code):
- `charge_estimee_(jours)` is the key for **Charge réelle**; `charge_estimee` is Charge estimée; `budget_reel_(` is the truncated key for Budget réel.
- Priorité lives under the opaque property id `67c6d714341c1628147d7b1d`; its options `0` and `4` are opaque tag ids too (`67cc1782…`, `67c6d722…`); options 1/2/3/5 have literal keys.
- Task's « Rapport(s) lié(s) » key is misspelled: `rpport(s)_lie(s)`.
- Tag keys diverge from display names: `production`→Produire, `recherche`→Chercher, `gestion`→Gérer (type_de_tache); `rapport`→Recherche (type_de_document); `document_de_cadrage`→Texte (livrable(s)).

**`rapport` has no task-link property.** It carries `date_du_journal`, `session(s)_liee(s)`, `relation_projet`, `contact_lie`, `fichier(s)_lie(s)`. The task→rapport link lives on the task side. The 47 existing rapports link only to the project in practice.

**Decision (Louis): reports anchor to tasks from the task side, schema intact.** `session_start` creates the rapport (journal date + project link); the server writes the report into each touched task's « Rapport(s) lié(s) ». This amends the founding-grilling line "linked to every task it ended up touching" — the link is written on the tasks, not on the report.

## 2026-06-12 — Founding grilling (grill-with-docs session)

Module founded: a custom MCP server over the local Anytype REST API, tailored to Louis's project-management space (epics → projects → tasks → session reports, plus ideas and documentation). Consumers: Claude Desktop / Claude Code now; later a Deckle in-app assistant running a small local instruct model (Ministral 3B class) — that future path is why the architecture below matters. The old connector plan at `D:\projects\ai\anytype` is superseded by this journal.

**Stack — C# homegrown, no SDK.** Two layers: a core library (HTTP client to the Anytype API + the PM gestures) and a thin stdio MCP host speaking JSON-RPC 2.0 (`initialize`, `tools/list`, `tools/call`). Chose homegrown over the official TypeScript or C# SDKs because: understanding what we build, zero structural dependency, and the future in-app assistant calls the core directly as a library — the MCP host is just one of two doors onto the same logic. Hygiene rule: stdout is reserved for JSON-RPC messages; logs go to stderr. No credentials hardcoded — the bearer key lives outside the repo (exact home to be decided at bootstrap).

**API facts** (researched 2026-06-12, developers.anytype.io + OpenAPI spec):
- Current API version `2025-11-08`; required `Anytype-Version` header; base `http://localhost:31009/v1` — the port is opened by Anytype Desktop, which must be running. Challenge auth (4-digit code in the app) yields a long-lived bearer key.
- Body (`markdown`) PATCH is a **full replacement** — no append. This single fact reshaped the design (see session reports below).
- `DELETE` object = restorable archive (native bin), not permanent deletion.
- Rate limit 1 rps sustained, burst 60. Acceptable; robustness over speed.
- The official anytype-mcp is a thin auto-generated OpenAPI wrapper — no domain logic, no token economy. That gap is this module's reason to exist.
- The block-level surface (AnyBlock) only exists on the private gRPC API used by the desktop app; not supported, not versioned — we stay on REST.

**Session reports are phase 1, not phase 2.** Because body PATCH is full-replacement, journaling into the task body would mean re-reading and rewriting an ever-growing markdown. Instead each work session creates a small **report** object (Louis's existing report type: note layout, journal date, linked task/project) and journal lines go there — read-modify-write stays cheap on a fresh small object. 3–5 lines per session, each recording the *why* of a significant action (the *what* lives in git).

**Report lifecycle.** One report per work session, anchored on the task the session started on, and linked to every task it ended up touching — multi-task links, flat, not parent-child (Louis reads the graph in Anytype). No status property: a report is closed by construction because each session creates its own and never reopens previous ones. The handoff gesture is a final journal line by convention ("état au handoff : …"), not a field.

**Subtasks = inline `- [ ]` checklist in the task body**; the task body stays nearly empty otherwise. A subtask that would deserve its own journal or properties is not a subtask — it's another task in the project.

**Work outside any task is a missing task.** When session work attaches to no existing task, the agent proposes creating one and Louis validates in conversation. No server-side validation machinery — MCP elicitation was considered and rejected (uneven client support, wrong layer; the future Deckle UI will add its own confirm step).

**Tool surface v1 — validated by Louis, 13 tools.** Name→object resolution (returning candidates on ambiguity) is built into every tool, not a separate `resolve`. `session_start` returns the report id; `log` takes an optional report id, defaulting to the last one opened by this server process.

Priority 3 — `session_start(task)` (create report + return task + digest of previous reports), `log(line, report?)`, `get(name_or_id, type?)` (full read of any object), `project_overview(project)`, `create_task(project, name, type, priority?, body?)`, `task_done(task)`.

Priority 2 — `link(object, targets)`, `list_projects(state?)`, `search(text, types?)`, `subtask(task, label, done?)`, `create_project(name, epic?, state?)`, `create_idea(content)`, `update(object, properties)`.

Deferred — `create_doc`, epic management, `archive_done` (priority 1, Louis by hand for now); yearly purge routines (priority 0, future scheduled agents *using* the MCP, not tools).

Open for later: a second, smaller MCP profile tailored for the in-app small model ("on verra"); exact type/property keys of the real space — first act of the build session is the interactive bootstrap (Anytype Desktop open, auth challenge, `getTypes` discovery).
