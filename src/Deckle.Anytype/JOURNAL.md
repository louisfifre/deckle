---
description: Dated decisions and findings for the Anytype MCP server — founding grilling, API facts, tool surface.
type: module-journal
---

# Journal — MCP Anytype

Module-level dated notes. Most recent on top.

## 2026-08-10 — Restart race exposed missing lifecycle boundaries

Measured across two Deckle process sessions: the outgoing process emitted `ShutdownRequested`, then its still-running fire-and-forget `EnsureRunningAsync` spawned `anytype.exe`. The successor probed the still-warming endpoint, spawned a duplicate, then attributed the original process's `200` response to that duplicate. The duplicate exited with code 1 and became the process watched and restarted, while the original process kept serving on `127.0.0.1:31012`. The confirmed trigger, cause, violated invariant and recurrence cue are preserved in `tests/Deckle.Anytype.Tests/BackendSupervisionBugNotes.md`; no regression test exists yet.

Code matches the trace: `BackendSupervisor.Dispose` cancels and joins only the watch task, not an in-flight `EnsureRunningAsync`; readiness proves only that the fixed endpoint answers, not which PID owns it; the watch begins after the readiness wait; and logged uptime starts when the watch begins rather than when the process was spawned. The application discards the initialization task from `OnLaunched`, so shutdown has no task handle to cancel or await.

The provider runtime currently lives under the replaceable application payload at `%LOCALAPPDATA%\\Programs\\Deckle\\anytype`. The updater's running-process gate scans that payload recursively, while deployment deletes every nested entry except the uninstaller before copying the new payload. A running backend therefore blocks the update gate; a stopped backend is removed by deployment and must be provisioned again. This conflicts with the accepted requirement that the warm backend survive Deckle updates and restarts.

The existing observations were sufficient to reconstruct the race: application JSONL stamps a process-session id; backend events expose health status and duration, attached PID and mode, exit code and watched uptime; MCP events expose host start/stop, client plus session prefix, and rejected-request reasons; Anytype REST events expose request start/end/retry/failure. They do not currently expose an initialization/reconciliation id, attempt and scheduled backoff, listener-owner PID, cancellation and drain outcome, MCP request start/end, tool outcome, or session close/eviction.

The local HTTP host implements the sessionful MCP `2025-11-25` path (`initialize` plus `Mcp-Session-Id`) in handwritten transport code. The official `2026-07-28` revision removed both from the protocol core and made each request self-contained; the official C# SDK 2.0 supports that revision and down-level interoperability. Claude Code documents automatic HTTP reconnection with bounded exponential backoff. No equivalent Codex contract was found; an open Codex issue still requests automatic MCP reconnection. Server-side robustness must therefore not rely on a particular client retrying or recreating a transport session.

## 2026-07-29 — Native Experience import proven as an integration fixture

Measured with `anytype-cli` 0.3.6 (anytype-heart 0.50.10): private RPC `ObjectImportExperience` imported a cleaned native Desktop archive into one fresh existing space after bot membership was verified `Active`. The exact validated archive has SHA-256 `775ebd35ec185fde3fec0fa50723bab3d241deac02c85603dcb58401528019ae`. Its REST inventory moved from 14 types / 34 properties / 0 objects / 5 templates / 0 lists / 0 views to 22 / 64 / 6 / 15 / 5 / 5; Desktop then visually confirmed the types, per-type icons, linked properties, templates and views. The six retained objects are structural dashboards; Recent items is automatic and was deliberately absent from the archive.

The English fixture keeps Anytype's native `Status` separate from the custom `Lifecycle status`, and reuses native archiving instead of defining a second Archived property. Anyblock JSON field shape is significant: flattening a one-item array into a scalar makes import fail with `PB:GetSnapshot json: cannot unmarshal string into Go value of type []json.RawMessage`.

This proves portability for one clean import, not strict idempotence or a supported production installer. The RPC server has reflection disabled and requires the matching protobuf definition. `anytype-cli space join` 0.3.6 also ignores `RpcSpaceJoinResponse.Error`, so its success message is insufficient; membership must be checked in `space list` until it reports `Active`.

## 2026-07-27 — Epic / chantier / task model frozen

Decided: Deckle is the permanent Epic, a Project is one finite chantier, and a Task is one executable unit; smaller steps stay as inline checklist items. The built-in `done` checkbox is the canonical completion signal for Projects and Tasks. État remains planning state, while Archivé only removes an object from active views.

Found: Anytype API `2025-11-08` documents both object creation with `type_key` and retyping through a `type_key` PATCH. Chose a bounded `create_epic` gesture instead of exposing generic retyping because the API does not document how incompatible properties or collection state survive an arbitrary type change.

## 2026-07-16 — Retired the stale schema snapshot without losing its live residues

Removed `Schema/SCHEMA.md`: its type tables had become a second, drifting source beside `DevSpace.cs` and the live Anytype space. The last verified cleanup state remains here: `tag` is Anytype's auto-transversal residue and stays deliberately unmapped; `État` was removed from Idée; `rpport(s)_lie(s)` (the frozen misspelled key for “Rapport(s) lié(s)”) remains orphaned and should be deleted from the space if the live schema still exposes it. The Anytype MCP was offline during this audit, so that final live-space check is deferred rather than claimed.

## 2026-07-02 — MCP host HTTP en vif ; hébergement remplacé par une supervision in-process

- **Porte HTTP prouvée bout-en-bout.** Host résident sur `127.0.0.1:33255/mcp`, identité par bearer (`mcp-token-claude`/`-codex` scellés au vault, miroités en env vars `DECKLE_MCP_TOKEN_*`). `initialize`/`tools/list`/`tools/call` réels pour les deux clients, surfaces exactes du stdio (claude 16 outils `delete`/pas de dialogues, codex 18 l'inverse), `401` sans bearer, `403` en session croisée. `list_projects` → `isError:false` une fois le bot membre de Dev.
- **Cutover HTTP fusionné sur `main`** (`887c4aac`). La porte MCP n'existe que tant qu'un Deckle issu de ce `main` tourne ; un échec de `tools/list` par connexion refusée signifie d'abord que ce Deckle n'est pas lancé. Codex vise `http://127.0.0.1:33255/mcp` avec `DECKLE_MCP_TOKEN_CODEX` et garde sa surface stdio miroir moins les outils de management : 18 outils, dialogues oui, delete non.
- **`space join` fait.** Invite Desktop approuvée ; l'espace Deckle est `Active` côté bot.
- **`anytype service` disqualifié sur Windows.** Lecture du source (kardianos/service v1.2.4) : `UserService:true` est ignoré sur Windows, `service install` crée un vrai service SCM **LocalSystem/Session 0** — le keyring/DPAPI de l'utilisateur y est invisible, l'auto-login échoue. La tâche planifiée du 06-19 tenait pour cette raison.
- **Hébergement par tâche planifiée retiré au profit d'une supervision in-process.** Deckle spawne le serve en `CreateNoWindow` (console sans fenêtre du tout — plus de vecteur 0xC000013A), l'adopte au boot par chemin de binaire exact, attend son handle et le relance sur une échelle bornée (2/5/15/60 s, reset après 5 min stables). `Dispose` arrête la garde, jamais l'enfant → le serve survit aux rebuilds. Prouvé par kill : relance ~2 s, nouveau pid, sans fenêtre. `BackendScheduledTask`/`BackendTaskDocument` supprimés, tâche machine désinscrite.
- **Résidu de setup à traiter dans `Deckle.Setup`.** Après confirmation du cutover HTTP en vif, le wizard doit supprimer le junction stdio obsolète `%LOCALAPPDATA%\Deckle\mcp\anytype` et les sauvegardes `.claude.json.deckle-http-bak` / `.bak`. Reste à vérifier si Claude Code interpole `${VAR}` dans les headers au scope utilisateur ; fallback prévu : `headersHelper`.

## 2026-07-02 — Backend wired at boot; vault-first credentials proven

The lifecycle built on 06-19 went live: the real task is registered, the supervisor runs from `App.OnLaunched`, and the bearer moved to the vault. Chain proven on the machine through the real module code (scratch runner, never the app).

- **Both 06-19 empirical residuals closed on the real task.** `schtasks /Create` + `/Run` of the LeastPrivilege triggerless "Deckle Anytype Backend" task raise no UAC from a non-elevated process, and the spawned `serve` survives its caller (parented to the Task Scheduler service) — REST still answers after the launcher dies.
- **La console InteractiveToken était le tueur** (corrigé le même jour, voir entrée en tête). `MainWindowHandle 0` trompait : le binaire console garde une console *attachée mais fenêtrée*, dont la fermeture émet `CTRL_CLOSE_EVENT` → `STATUS_CONTROL_C_EXIT` (0xC000013A). Cinq morts du serve en session, toutes sous ce régime.
- **Credentials resolution is vault-first (frozen in code).** A vault `anytype-api-key` pins the fixed 31012 headless listener; the file `api_key`/`api_url` pair stays as the legacy Desktop fallback until the space cutover retires it. Non-secret coordinates (`api_version`, `space_id`) stay in the module file.
- **API key names are not unique.** A second `apikey create deckle-mcp` succeeds alongside the first; `revoke` targets the key id, not the name. The dead 07-01 key was revoked; the only live key is the vault's.
- **Guard until `space join`: do not republish the stdio MCP host.** A rebuilt host would resolve headless and talk to a backend whose bot is not yet a member of Dev. The published `current` junction host keeps speaking to the Desktop.

## 2026-07-01 — Bot account provisioned; REST auth proven end-to-end (supervised session)

The 06-19 residual closed: a real bot account now exists, and an authed `GET /v1/spaces` answers 200 on 31012. Measured against anytype-cli v0.3.6.

- **Bot account `Deckle` created** (`auth create "Deckle"`), Account Id `ABG37cUsbuNeymEqTYWdNFLCufdJBdgphyxSiaNWL6gC7hE6`. The account key printed once; the CLI stored it in the OS keychain via go-keyring. `cmdkey /list` does not surface it by name — but auto-login (below) reads it back, so it is stored.
- **`auth create` requires a running `serve`.** Order is counter-intuitive: `serve` first (gRPC only, no REST), then `auth create` talks to the live server, creates the account, and logs it in — which binds REST 31012 **hot**, no restart. Confirmed by a 401 on `/v1/spaces` immediately after.
- **Cold restart auto-logs-in.** A fresh `serve` finds the stored key and logs in on its own (log: "Found stored account key" → "Successfully logged in using stored account key"), rebinding REST unaided. So the backend scheduled task needs no auth gesture at start — a bare `serve` restores REST + auth. This is the lifecycle precondition, now proven.
- **Correction to the 06-19 note: `/docs/openapi.json` IS a local route** — 200 on 31012, unauthenticated. The 06-19 "not a local route" was written with no account, so REST never bound and the path was never testable. Since REST binds only after login, a 200 there proves both "REST up" and "account logged in" without a bearer. `BackendHealthProbe`'s current path is **valid, not to fix**.
- **API key `deckle-mcp` minted** (`auth apikey create`), bearer proven via `GET /v1/spaces` → 200 (one space, the bot's empty default). Repeatable command (`apikey list`/`revoke` exist). The bearer was **not** persisted — proof only; re-mint when a consumer (MCP host / wizard) exists to call `ISecretVault.Set`. Next: `space join` (owner invite from Desktop), then wire the supervisor.

## 2026-06-19 — Backend lifecycle built + anytype-cli measured (implementation session)

Implemented point 1 of the 06-18/06-19 wizard design. Code on branch `feat/anytype-backend-lifecycle` (dormant: compiles, 7/7 unit tests, no consumer wired, no real process spec).

Built: a triggerless on-demand scheduled task (`BackendScheduledTask` — no `<Triggers>`, `LeastPrivilege` + `InteractiveToken` + `ExecutionTimeLimit=PT0S` + `AllowStartOnDemand`), an HTTP `BackendHealthProbe`, a `BackendSupervisor` (probe → `schtasks /Run` → bounded readiness poll), a `BackendProcessSpec` seam to provisioning, lifecycle EventSource ids, `BackendTaskDocumentTests`. Mirrors `ElevatedStartupService` minus trigger, minus elevation; the `Escape`/XML builder kept module-local, not shared.

Proved on the machine (stand-in `ping` task, since cleaned up) — the two 06-19 residuals:
- `schtasks /Create` + `/Run` of a LeastPrivilege triggerless task run with **no UAC** from a non-elevated shell.
- The spawned process is parented to the Task Scheduler service (svchost), not the launcher shell, and **outlives the caller**.

anytype-cli measured (v0.3.6, released 2026-06-17):
- Downloaded `anytype-cli-v0.3.6-windows-amd64.zip` into `%LOCALAPPDATA%\Programs\Deckle\anytype\`, sha256 `3aa8db0a02f9349164c1dacf5ede32e8a0b0cf966ced59cb37ff82e2605ab1be` verified. The release publishes **no checksum manifest** (no SHA256SUMS, install.sh does no verification) → pin the GitHub per-asset digest ourselves. CLI is MIT; it embeds anytype-heart (different, source-available license — verify before bundling).
- **REST 31012 is account-gated.** Bare `anytype serve` opens only gRPC 31010 + gRPC-web 31011; the REST gateway does not bind. serve log: `No stored account key found, skipping auto-login`. A stored account (via `auth create` / `auth login`, then auto-login) is the precondition for 31012 — confirms the 06-18 "REST only after login" note and refutes the community "REST never in the CLI" report for v0.3.6.
- **Correction to the 06-18 design:** `/docs/openapi.json` is the hosted Developer-Portal path, not a local route — no local health endpoint is documented. Readiness must be an authed `GET /v1/spaces` (Bearer + `Anytype-Version`). `BackendHealthProbe` currently probes the wrong path — to fix.
- CLI auth = bot-account commands (`auth create <name>` prints the account key once; `auth apikey create` prints the bearer once), distinct from the Desktop/31009 4-digit challenge. The REST base URL must be vehicle-aware: 31009 (Desktop) vs 31012 (CLI), same request contract.

Open (Louis): the final proof that an authed `GET /v1/spaces` answers on 31012 needs a real bot account — the apikey step Louis supervises — deferred to next session. Then: account key → `Deckle.Security` vault (to build), apikey, space join, fix the health probe, wire the supervisor, end-to-end.

## 2026-06-19 — Provisioning wizard: lifecycle, vault, per-client config (grilling session)

Resolved the three TBD points left open on 06-18. Architecture (ADR-0001) untouched. Client config facts verified against openai/codex Rust source + Claude Code docs.

Decisions (Louis):
- **Backend lifecycle = a triggerless, on-demand scheduled task** (`InteractiveToken` + `LeastPrivilege` + `ExecutionTimeLimit=PT0S`), started by Deckle's health-check via `schtasks /Run`. Survives Deckle (the task process is parented to the Task Scheduler service, not the caller), runs non-elevated, lives in the interactive session, and honours the autostart toggle by construction (no logon trigger → never starts unless Deckle asks). Reuses `ElevatedStartupService.BuildTaskXml` minus the trigger, minus the elevation. Service rejected: Credential Manager generic creds + DPAPI are bound to the interactive logon session (`CredRead` = "credential set of the logon session of the current token"; `CryptUnprotectData` needs matching logon creds) — a Session-0 service logon is a different session, so a service couldn't reliably read what the interactive wizard wrote.
- **`Deckle.Security` storage = homegrown DPAPI CurrentUser sealed single-file vault** under `%LOCALAPPDATA%\Deckle`, behind an `ISecretVault` interface (the `HarvestStore` pattern). Not the Windows Credential Manager — it would fragment the curated inspectable surface and add a dependency, for the same DPAPI floor underneath. The `anytype-cli` account key stays in the Credential Manager (go-keyring owns it): a two-store boundary by subsystem ownership, not an inconsistency.
- **Inspectable surface = a single predicate engine; the wizard is its resident face** (entry points: first-run + Settings). V1: the wizard *is* the surface — provisioning and key entry, relaunchable; the General page shows the supervisor's last-known state; the cross-service vault inspector is deferred.
- **Per-client config = delegate to the client CLIs** (`claude mcp add` / `codex mcp add`) as the primary path; a typed file-write fallback only for a CLI-less client (none today). Idempotency by probe then `remove`+`add`. The per-client bearer is materialised as a **per-client user env var** (`DECKLE_MCP_TOKEN_<CLIENT>`), a plaintext projection of the vault; rotation/revocation = rewrite/delete the var. Consistent with DPAPI CurrentUser already assuming same-user trust.

Verified facts (June 2026):
- **Codex streamable-HTTP MCP is stable, not experimental** (Rust enum `McpServerTransportConfig::StreamableHttp`). `~/.codex/config.toml`, flat table `[mcp_servers.<name>]` with `url` + `bearer_token_env_var` (an env-var *name*, never the literal). `deny_unknown_fields` → a stale/unknown key fails parsing (an argument *for* delegating to the CLI). `codex mcp add <name> --url … --bearer-token-env-var …`.
- **Claude Code**: `~/.claude.json` / project `.mcp.json`, `mcpServers.<name>` with `type:"http"` + `url` + `headers.Authorization`, `${VAR}` interpolation at runtime. `claude mcp add --transport http --header …`.
- `install-anytype-mcp.ps1` (stdio publish + `current` junction + regex repoint) is obsolete under HTTP — to retire.

Deferred / next: a security+robustness hardening pass after V1 works (the bearer's plaintext env-var exposure first); the cross-service vault-inspector surface in `Deckle.Security`. A lifecycle ADR is a candidate once the point (1) mechanism is implemented. Empirical residuals to prove at build: `schtasks /Run` of a LeastPrivilege user task without UAC, the spawned process outliving its caller, `codex mcp add` idempotency on an existing server, client version pinning.

## 2026-06-18 — Provisioning wizard (grilling session, branch E)

Designing the wizard that installs the headless backend and wires the MCP clients. Architecture (ADR-0001) untouched. CLI/sync facts verified against anyproto/anytype-cli + any-sync source.

Verified facts:
- **`anytype-cli` is a real official binary** (`anytype`, repo anyproto/anytype-cli), embeds heart; ports gRPC 31010 / web 31011 / **REST 31012** (the default). REST only comes up **after an account login** (the listen-addr is propagated at auth), not on bare `serve` → health = `GET /docs/openapi.json` 200 (unauth, also carries the API version), never the PID.
- **Distribution** = GitHub Releases asset `anytype-cli-vX.Y.Z-windows-amd64.zip`; ~3-10 day cadence, each release bumps embedded heart. Three version layers: CLI semver / heart / REST API date (`2025-11-08`). The server does **not** validate the incoming `Anytype-Version` — compat is implicit, not negotiated.
- **Secrets print once on stdout.** `auth create <name>` is non-interactive, prints the account key once (stored in Windows Credential Manager via go-keyring, plaintext-file fallback); **not idempotent** — rerun makes a new account and overwrites. `apikey create <name>` prints the bearer once (`list` truncates to 8 chars). Authorship: the API key is a local app linked to the single account → all writes authored by the one bot.
- **Space membership = invite-link handshake.** Owner generates a link (CID+key) in Anytype, bot runs `space join <link>` = a *join request*, owner *approves* (async), bot confirms via `space list` (status Active). CLI has no approve / pending-detection command. Owner-side generate+approve exist **only in private gRPC** (`RpcSpaceInviteGenerate` / `RpcSpaceRequestApprove`), not REST (REST exposes list-spaces + read-only list-members), and the Desktop gRPC port is non-discoverable → owner-side automation rejected.
- **any-sync**: local-first; objects are signed change-DAGs (CRDT, field-grain merge — the REST whole-body PATCH is the only lossy layer); sync relayed via sync nodes (not pure P2P); E2E-encrypted per space via an ACL (read key resealed per member). A non-member account cannot enumerate or read a space (unguessable CID ids, no registry). Account = keys; node = instance; the same account can run on several nodes if its key is transported.

Decisions (Louis):
- **Wizard = resumable, predicate-driven state machine** — each step a verifiable predicate on the real world; reopening re-probes, no stored progress counter. Absorbs both the from-zero install and Louis's migration on the same machine.
- **Inventory**: backend trunk (binary → service → bot account → space membership → API key → end-to-end auth health) provisioned once, then a per-client branch (host up → client token → client config). The client token is ours (the host validates it), distinct from the Anytype API key the host presents to the backend.
- **Space scope = the invitation itself** — no separate space-selector; the bot sees only the spaces it is invited into.
- **Server deployment = noted horizon, not pursued.** Windows-first wizard; the creds-storage boundary kept isolatable (a Linux host would need a non-DPAPI vault).
- **Version policy = pin a known-good version + signal newer, never auto-update**; prove compat via `openapi.json` at start, not just a port ping. Moving the pin is a maintainer act.
- **Binary delivery = download the pinned asset at first provisioning** into `%LOCALAPPDATA%\Programs\Deckle`, integrity-checked before extract; not bundled in the installer (Anytype needs the network anyway).
- **Backend lifecycle**: starts with Deckle, runs **detached in the user context** (for Credential Manager access), **persists across Deckle crash/rebuild**, stopped **only on explicit quit**; Deckle supervises (health-check + relaunch) without owning the process. Exact Windows mechanism (scheduled task vs per-user service) + user-context keyring access **TBD via Microsoft docs**.
- **`Deckle.Security` = general Deckle credential vault** for all secrets (Anytype API key, client tokens, future third-party API keys for transcription/rewrite), with an inspectable surface later. Storage mechanism (DPAPI vs Windows Credential Manager) + management UI **still to grill**.

Still open / next: the keyring-context Windows mechanism (Microsoft-docs check), `Deckle.Security` storage mechanism + UI, robust per-client config writing (Claude JSON `type:http`+`headers`; Codex TOML `url`+`bearer_token_env_var`/`http_headers` — young, validate empirically), retiring `install-anytype-mcp.ps1`. A lifecycle ADR is a candidate once the Windows mechanism is frozen.

## 2026-06-18 — Headless runtime + single HTTP MCP host (grilling session)

Founding architecture for the Anytype/MCP/Deckle integration, decided with Louis. Runtime/API facts verified against anytype-cli + anytype-heart source.

- **Backend = headless `anytype-cli`** (embeds `heart`), run as a Deckle-supervised Windows user service — not a Deckle child (survives rebuilds), not Desktop. Same REST `/v1` on fixed `127.0.0.1:31012` (Desktop = 31007-31009). Bot account via `anytype auth create` (account key → Windows Credential Manager, no GUI challenge); API key via `anytype auth apikey create`; space join is CLI/gRPC (`anytype space join`), not REST. Deckle orchestrates lifecycle + access, never owns/reimplements the data.
- **Transport = HTTP, one host in Deckle's resident core.** Clients connect by URL + per-client bearer. Kills the exe-lock + `current` junction (no client-spawned binary) and the Claude-only config script. stdio reduced to a deferred thin gateway for stdio-only clients (Claude Desktop chat). Verified: Claude Code + Codex CLI both consume HTTP MCP by URL (Codex rmcp client still experimental). Internal Deckle tools bypass the MCP entirely (lib in-process); the MCP serves external clients only.
- **Surfaces split by capability, not space** (`space_id` is a call parameter): PM (exists), Dialogue (exists), Cartography (to build, low priority, create/update/read modules+deps in the Deckle space, AI-co-written) — all endpoints of the one host.
- **One bot to start.** Verified: one headless = one account = one author; API keys are wallet-sharing "link apps", not identities, so N authors need N headless instances = N independent syncs (rejected — Louis won't duplicate the Dev-space sync). Codex/Claude told apart by speaker label / actor field in content; per-client access differences live in host token-scoping, not separate accounts. Extra bots + a dedicated Deckle-internal bot deferred.
- **Layout:** executables + libs + models → `%LOCALAPPDATA%\Programs\Deckle` (per the installer's `InstallPaths`); user data + credentials → `%LOCALAPPDATA%\Deckle`. `native\` currently misplaced in the data root → to move. Heavy-asset relocation (models off a saturated C:) becomes an install/after-the-fact disk chooser. Credentials (bot API key + client tokens) encrypted via a new `Deckle.Security` module (DPAPI); the CLI's own account key is already OS-protected.
- **Concurrency → stay HTTP.** REST has no optimistic concurrency (no ETag/If-Match); body PATCH = full delete-all + paste, so concurrent read-modify-write loses updates; the CRDT does not merge it; write rate-limit 1 rps / burst 60 (429, writes only), reads free; gin serves requests in parallel. Transport is not the crux — stdio only removes the single coordination point. `SpaceWriteLock` (cross-process OS file lock held over the whole GET→PATCH) already serializes; under one HTTP host it becomes an in-process single-consumer queue, the file lock kept as backstop. Rule: never raw-replace a body — always go through `replace_section`.
- **Open:** provisioning wizard, robust per-client config writing, same-target conflict feedback in the write queue, the official-Anytype-MCP relation, and an ADR (proposed, pending Louis) for the headless-service + single-HTTP-host + serialization cluster.

## 2026-06-16 — Management + lifecycle layer; schema resync

- Lifecycle split into two verbs, not one generic command. A naming pass found a single "set a lifecycle checkbox" tool (done + archived behind one param) carries an inherent collision with the « état » select for a small model — any lifecycle name slides toward "Terminé". Degrouped into `complete` (task `done`, set/clear) and `archive` (transversal `archive`, archive/restore; refused on rapport, which has no such checkbox). `task_done` removed — folded into `complete`. Base: 15 tools.
- `delete` → restorable bin, supervised + two-step, pinned by id. Lives in a separate `ManagementToolCatalog`, mounted only behind a launch flag (`--management` arg or `DECKLE_ANYTYPE_MANAGEMENT` env); a default consumer is served no destructive tool. Stateless two-step: first call previews the target (name/type/id), a second call with that id and `confirm:true` commits. No server token (reserved for the deferred batch). Added `DeleteObjectAsync` (DELETE /objects/{id}).
- Schema resync (DevSpace): `tag` unmapped from every type table (auto-transversal residue, unused); `Charge estimée/réelle` mapped onto Task; `État` removed from Idée. Consequence: `update` now refuses `tag`, and `LiveTagResolver` (free-vocabulary live resolution) is no longer reached by any mapped property — kept as dormant infra.

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
