---
description: Dated decisions and findings for the Anytype MCP server — founding grilling, API facts, tool surface.
type: module-journal
---

# Journal — MCP Anytype

Module-level dated notes. Most recent on top.

## 2026-06-16 — Management + lifecycle layer; schema resync

- Lifecycle split into two verbs, not one generic command. A naming pass found a
  single "set a lifecycle checkbox" tool (done + archived behind one param) carries
  an inherent collision with the « état » select for a small model — any lifecycle
  name slides toward "Terminé". Degrouped into `complete` (task `done`, set/clear)
  and `archive` (transversal `archive`, archive/restore; refused on rapport, which
  has no such checkbox). `task_done` removed — folded into `complete`. Base: 15 tools.
- `delete` → restorable bin, supervised + two-step, pinned by id. Lives in a separate
  `ManagementToolCatalog`, mounted only behind a launch flag (`--management` arg or
  `DECKLE_ANYTYPE_MANAGEMENT` env); a default consumer is served no destructive tool.
  Stateless two-step: first call previews the target (name/type/id), a second call
  with that id and `confirm:true` commits. No server token (reserved for the deferred
  batch). Added `DeleteObjectAsync` (DELETE /objects/{id}).
- Schema resync (DevSpace): `tag` unmapped from every type table (auto-transversal
  residue, unused); `Charge estimée/réelle` mapped onto Task; `État` removed from Idée.
  Consequence: `update` now refuses `tag`, and `LiveTagResolver` (free-vocabulary live
  resolution) is no longer reached by any mapped property — kept as dormant infra.

## 2026-06-15 — MCP host consumed via a `current` junction, off the build tree

The host AI clients spawn no longer points at the build output (`artifacts\bin\Deckle.Anytype.Mcp\debug\Deckle.Anytype.Mcp.exe`). A running .exe is locked on Windows, so a live client (Claude Code, Codex) held that file and any rebuild of the host failed with MSB3026 — the cause behind "can't rebuild/restart while a session is up". Chose the Scoop model under `%LOCALAPPDATA%\Deckle\mcp\anytype\` (sibling of `modules\anytype\`, the credentials home): each publish lands in `versions\<timestamp>\`, a `current` junction points at the active one, and `.claude.json` targets `current\Deckle.Anytype.Mcp.exe` once. An update republishes into a new dir and re-points the junction — it never overwrites a running exe, so clients stay open (live sessions keep their version until they respawn; old dirs prune once released). `scripts/lib/install-anytype-mcp.ps1` (Setup menu) owns publish + junction + the surgical, idempotent config repoint. The junction is deleted with `Directory.Delete(path,$false)` (reparse-point only, never its target).

## 2026-06-13 — Dialogue chats stay on REST, separate from reports

Found live against Dev: Anytype chats are objects of type `chat_derived`, layout `chat`, with transcript content served by `/chats/{chat_id}/messages` rather than object `markdown`. The `Test` chat carries the space-global `tache(s)_liee(s)` objects property and can link to a task without becoming a rapport. Chose the POC shape: dialogue gestures live inside `Deckle.Anytype` as a separate capability from project-management gestures, and the MCP host selects a `dialogues` profile instead of creating a new module.

## 2026-06-13 — `replace_section` and the body round-trip

Added `replace_section` (14th tool): replaces a section located by its heading, with read-after-write verification. Found: Anytype re-serializes the body on every PATCH export, so the read-back is normalized — heading text and level survive, but a literal underscore / asterisk / backtick / pipe comes back backslash-escaped and lines gain trailing spaces; a byte-equal read-after-write is impossible, so verification compares a normalized form.

## 2026-06-13 — gRPC re-investigated; REST decision held

Scoped feasibility pass on anytype-heart's private gRPC `ClientCommands` API — the block-level surface (stable block IDs, fine edits) that REST lacks. **Decision held (Louis): Deckle stays on REST, gRPC not pursued.** Deciding factor: port discovery is too complex and the whole path is heavy for the gain.

Facts re-verified (researched 2026-06-13: anytype-heart + anytype-ts source on GitHub, advisory GHSA-vv3h-7qwr-722v, developers.anytype.io changelog):

- **The gRPC port is not discoverable by a third party.** Anytype Desktop spawns heart with `127.0.0.1:0` (OS-chosen ephemeral port); the resolved address is printed only to heart's stdout, captured by the Electron parent — written to no file, named socket, or shared env. heart runs as Electron's child (killed on close), so its port and session key are private to that instance, not a daemon to attach to. The frontend speaks gRPC-Web, not native HTTP/2. The only clean gRPC path would be running our own headless heart (fixed ports 31007/31008, env `ANYTYPE_GRPC_ADDR`/`ANYTYPE_GRPCWEB_ADDR`) — owning a Go backend's lifecycle, not a graft.
- **CVE-2026-31863 is no longer an argument.** It is a brute-force of the 4-digit challenge (CWE-307), Low/Medium, localhost-only, fixed in heart v0.48.4 / Desktop v0.54.5 by server-side rate-limiting; it does not change third-party integration cost.
- **REST will not gain block-level editing.** The "patching the blocks" item (anyproto discussion #218) shipped as whole-body markdown replacement — the destructive read-modify-write already in use — and `getObject`'s `blocks` field was deprecated. Block-level with stable IDs stays gRPC-only at the visible horizon.
- The gRPC `.proto` are clean proto3 (no baked-in `gogoproto`) and would compile to C# via `Grpc.Tools` without exotic plugins — codegen is not the cost. A third-party Python client exists (`rakaarwaky/anytype-automator`) and finds the port by probing 17 candidate ports, confirming there is no clean discovery.

## 2026-06-13 — Link model inverted: rapport → task, project derived

The `session` type was deleted — it used to sit between task and rapport, and the link properties had been shaped for it. Louis re-pointed the live space: a rapport now carries « Tâche(s) liée(s) » (`tache(s)_liee(s)`, objects, several allowed), the task drops « Rapport(s) lié(s) », the rapport drops « Projet(s) lié(s) ». The chain is a cascade — rapport → task(s) → project — so a report's project is *derived* through its tasks, never stored. `session(s)_liee(s)` is gone from the space.

Code resynced: `DevSpace` Rapport/Task tables + the new `tache(s)_liee(s)` key; the `link` matrix (now task→project, rapport→task, project→project); `SessionGestures` anchors on the report side (the report is born with the anchor task in « Tâche(s) liée(s) », `session_touch` appends further tasks to the report, a task's reports read by inverse search); `ProjectGestures` joins a project's reports through its tasks.

Caveat — existing rapports were NOT re-linked: they keep their old `relation_projet` and have no `tache(s)_liee(s)`, so they stay out of the inverse views until a separate data migration re-links them. (Properties are space-global; `GET /v1/.../types` lists only a type's *featured* properties — the task still carries `etat`/`done` as object values though unfeatured — so the resync stayed surgical on the three reconfigured links.)

## 2026-06-13 — Templates are not applied by the API; the PM model pivots

Found while rebuilding the space: POST /objects does not apply the type's default template — the optional `template_id` field does, and without it objects are born bare (no template blocks, no inline views). Every object of the first migration was bare. `create_project` and `create_task` now pass the space's default-template ids, frozen in `Schema/DevSpace.cs`. `template_id` composes with `body`: the template blocks come first, the body follows.

Decided (Louis): Deckle is a *project*, not an epic — one project per app/repo, the modules become tasks under it, their detail lives as inline `- [ ]` subtasks in the task body. The space was restructured live: 11 module tasks created from the task template under the project « Deckle » (born from the project template, inline task views intact); the old 63 tasks, 11 module projects and the epic archived. The epic level drops out of use for Deckle; revisiting the tool surface (create_project's epic param, link, the host copy) is logged as a task in the space.

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

**PM types found in Dev:** `epic` (layout collection — projects are members of the collection, no link property on either side), `project`, `task`, `rapport` (note layout), `idee`, `document`.

**Key traps found** (display name ≠ key; the core library owns this map, frozen as code):
- `charge_estimee_(jours)` is the key for **Charge réelle**; `charge_estimee` is Charge estimée; `budget_reel_(` is the truncated key for Budget réel.
- Priorité lives under the opaque property id `67c6d714341c1628147d7b1d`; its options `0` and `4` are opaque tag ids too (`67cc1782…`, `67c6d722…`); options 1/2/3/5 have literal keys.
- Tag keys diverge from display names: `production`→Produire, `recherche`→Chercher, `gestion`→Gérer (type_de_tache); `rapport`→Recherche (type_de_document); `document_de_cadrage`→Texte (livrable(s)).

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

**Session reports are phase 1, not phase 2.** Because body PATCH is full-replacement, journaling into the task body would mean re-reading and rewriting an ever-growing markdown. Instead each work session creates a small **report** object (Louis's existing report type: note layout, journal date, linked to its task(s)) and journal lines go there — read-modify-write stays cheap on a fresh small object. 3–5 lines per session, each recording the *why* of a significant action (the *what* lives in git).

**Report lifecycle.** One report per work session, anchored on the task the session started on, and linked to every task it ended up touching — multi-task links, flat, not parent-child (Louis reads the graph in Anytype). No status property: a report is closed by construction because each session creates its own and never reopens previous ones. The handoff gesture is a final journal line by convention ("état au handoff : …"), not a field.

**Subtasks = inline `- [ ]` checklist in the task body**; the task body stays nearly empty otherwise. A subtask that would deserve its own journal or properties is not a subtask — it's another task in the project.

**Work outside any task is a missing task.** When session work attaches to no existing task, the agent proposes creating one and Louis validates in conversation. No server-side validation machinery — MCP elicitation was considered and rejected (uneven client support, wrong layer; the future Deckle UI will add its own confirm step).

**Tool surface v1 — validated by Louis, 13 tools.** Name→object resolution (returning candidates on ambiguity) is built into every tool, not a separate `resolve`. `session_start` returns a digest of the task and its recent reports; `log` takes an optional report id, defaulting to the last one opened by this server process.

Priority 3 — `session_start(task)` (create report + return task + digest of previous reports), `log(line, report?)`, `get(name_or_id, type?)` (full read of any object), `project_overview(project)`, `create_task(project, name, type, priority?, body?)`, `task_done(task)`.

Priority 2 — `link(object, targets)`, `list_projects(state?)`, `search(text, types?)`, `subtask(task, label, done?)`, `create_project(name, epic?, state?)`, `create_idea(content)`, `update(object, properties)`.

Deferred — `create_doc`, epic management, `archive_done` (priority 1, Louis by hand for now); yearly purge routines (priority 0, future scheduled agents *using* the MCP, not tools).

Open for later: a second, smaller MCP profile tailored for the in-app small model ("on verra"); exact type/property keys of the real space — first act of the build session is the interactive bootstrap (Anytype Desktop open, auth challenge, `getTypes` discovery).
