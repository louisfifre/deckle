---
description: Dated project notes for Deckle — cross-cutting findings too dated for a CLAUDE.md, too light for an ADR.
type: project-journal
---

# Journal — Deckle

Project-level dated notes: a finding, a milestone, a usage observation — worth recording with a date, but not heavy enough for an ADR nor timeless enough for a CLAUDE.md. Most recent on top.

## 2026-08-12 — Welcome page at provisioning, noted for the sharing theme

Louis's idea, same reboot conversation: a provisioned space should open on a hand-authored welcome/guide page — what this space is, how it reads — with inline views. Authoring stays in-app (inline views are app-only, verified 2026-08-10) and the content would be exported once and reused at provisioning; the mechanism belongs with the sharing/i18n theme above. Deferred with it; the Home instance (the « mode d'emploi » page) becomes a task in the rebooted space itself.

## 2026-08-12 — Manifest internationalization deferred, target shape sketched

During the Home space reboot (the clean-keys manifest rewrite, home repo), Louis named sharing Deckle's provisioning mechanism as a near-term goal: a user should provision their space in their own language, and translating should mean contributing one file.

Chose to defer. Today the manifest keeps key and label side by side (`"key": "estimated_budget", "name": "Budget estimé (€)"`) — one artifact, one truth, one language served.

Sketched the target for when sharing lands: the manifest keeps structure and canonical English keys only; every label (type names, property names, select options) moves to per-language token→label tables (`names.fr.json`, `names.en.json`, …); the provisioning language becomes a schema_apply-time parameter defaulting to the app locale. Two properties of the design worth keeping: extraction from the inline form is mechanical, since the keys already are the tokens — nothing written today is wasted; and because keys are immutable while labels rename freely, re-labelling an existing space into another language is a safe batch rename (a possible `schema_relabel` verb), never a migration.

The tracking gesture — a dev-space task — belongs to a session with the dev guichet mounted; this entry records the reasoning until then.

## 2026-08-05 — Context-authoring doctrine delivered as a skill

Delivered the context-artifact authoring doctrine as the personal skill `context-authoring` (`C:\Users\Louis\.claude\skills\context-authoring\`), which is now its single home: a six-paragraph conceptual core plus one `testing.md` annex. This entry supersedes the 2026-08-03 entry below; the principles recorded there were reworked during the final grill.

Chose the founding principle: a context element earns its place by changing what the model notices and weighs, not by walking it through the work. The earlier "design around what it should make possible" wording survives as the core's closing clause, no longer as the principle itself.

Chose the reception criterion: an element is ready when a cold reader lands on the intended meaning needing nothing private. Testing stays static for now: fresh readers receive the element verbatim and nothing else, light models in numbers for reception, few session-level models for quality judgment; live scenario probing was considered and dropped as too heavy.

Chose to keep prohibition of enumeration in the core: name a category by its criterion, not its members, because a list evicts what it leaves out.

## 2026-08-03 — Context-artifact authoring doctrine

Chose to make the planned authoring doctrine apply to reusable context artifacts generally, with skills as its first use case. The core stays short, conceptual, durable, and model-neutral; format rules, templates, and artifact-specific guidance belong only in optional references when they prove necessary.

Chose the first principle: “Design a context element around what it should make possible. Let that purpose shape its form.” Rejected turning the core into an interview process, invocation contract, or general restriction on execution detail.

Chose plain but exact English for the doctrine: complex ideas may take several simple words, while precision comes from naming the ideas that future work should notice and value rather than from adding procedural detail.

## 2026-08-03 — Context-document review paused at the skill-creation method

Started a cross-project review of Deckle's agent context. The repository currently has fourteen module/root `CONTEXT.md` files plus `CONTEXT-MAP.md`; the Travel context was absent from the map, the root summary omitted exposables, and the test summary counted two out-of-scope categories while naming three. Those safe corrections are present but uncommitted in the `docs/context-review` worktree, together with a first removal of implementation detail from the Autocorrect vocabulary.

Established the target boundary: `CONTEXT.md` carries resolved project terminology and enough prose to know what the terms denote, not precise implementation behavior. A behavioral specification is a distinct normative artifact that code and tests must satisfy; its exact convention and lifecycle remain to be designed and validated before technical material is migrated. Anytype already exposes generic document creation and editing, but a repository-document workflow still needs stable source identity and idempotent one-way publication if Anytype is to index these files without becoming a competing source of truth.

The next session starts with the highest-priority question: define a reusable method for deciding when a repeated need deserves a new skill, an extension of an existing skill, or another home. The method must cover evidence of recurrence, boundaries against context/reference/template/automation, minimum structure, trigger design, validation, and lifecycle; the proposed specification skill is its first test case. The related context-management work stays in the same priority band: define a skill's role, audit global and Deckle skills, audit context documents, review every skill description and trigger (not only `session-*`), then judge a main Deckle routing skill. Anytype project `Revue du système de travail de Deckle` records this order.

## 2026-07-22 — Frontière entre utilités Anytype et MCP personnalisés

Chose to separate reusable, precise Anytype utilities from custom MCP surfaces. Deckle.Anytype owns cross-domain capabilities for creating, inspecting and acting on Anytype structures; each custom MCP owns only its bounded domain gestures and composes the reusable utilities it needs.

Chose that selecting the Anytype module in the installer must expose an independent choice of one or more reusable Anytype utilities and custom MCP surfaces. Each utility and each domain surface remains optional rather than being enabled as one indivisible Anytype package.

## 2026-07-18 — Motion policy and LogWindow decisions

Found that Deckle exposes `SPI_GETCLIENTAREAANIMATION` but never calls it. The HUD helper named `AnimationSystemSetting` reads only the Dictation overlay setting. Chose to gate every Deckle-authored simple animation explicitly on the Windows animation setting while leaving control-internal motion to WinUI.

Chose to observe Windows animation changes at runtime. When Windows disables animations, finite simple transitions cancel and settle immediately in their final state; re-enabling affects only future transitions and never replays one.

Chose to keep LogWindow row insertion, repositioning and auto-scroll instant in every system state. The empty `ListView.ItemContainerTransitions` added by `d7f62dd6` is the existing anti-flash measure and must remain.

Chose one Dictation setting for all functional HUD motion: message recompact, processing-outline and digit-material motion, state blends, and microphone-level response. Turning it off settles finite motion, freezes continuous motion in a stable pose, and keeps the complete visual present. The chrono remains live data and proximity transparency keeps its separate setting. Simple HUD fades follow Windows instead. The existing preference value is preserved when its scope expands. Chose to remove the unreachable legacy swipe/reveal animation path.

Chose an off-by-default `Input activity` operational-log gate in Diagnostics. It stops Raw Input rollups and Trackpad gesture detail at their producers, but not lifecycle, device presence, incidents, warnings, or keyboard detail already owned by Autocorrect.

Found that Playground's `Record wheel events` is a persistent raw JSONL capture, not an operational-log gate. Chose to move it to Settings > Diagnostics > Telemetry, independent from `Input activity`.

Chose a three-column LogWindow row: fixed 12-character time, fixed 13-character source, and remaining-width message. Current source labels fit; any future overflow is visually ellipsized without truncating copy or export. The scroll tail reserves five measured row heights below the final entry. Both scrollbar rails and their shared corner use an opaque window-background mask while preserving native thumbs and behavior.

Chose WinUI `SplitButton`s with primary actions `Copy all` and `Save all`; their menus offer `Copy selection` / `Copy filtered` and `Save selection` / `Save filtered`. `All` ignores filters, while `Filtered` means every matching entry rather than the viewport. `Ctrl+C` copies only a selection and does nothing without one.

Chose contextual copy labels by scope: `Copy` for one selected row, `Copy selection` for several, then `Copy filtered` or `Copy all` with no selection according to whether filtering is active. Right-clicking an unselected row makes it the sole selection; right-clicking empty space preserves an existing selection.

Chose a single immediate `Clear` action for the whole in-memory journal, without confirmation and without touching `app.jsonl`. Save suggestions use `deckle-logs-YYYYMMDD-HHMMSS.txt` regardless of scope.

## 2026-07-17 — PII guard model candidates

Found Rampart is the closest reference for a Deckle privacy guard: a small ONNX token-classification model for PII spans, paired with deterministic detectors, default-deny redaction, stable placeholders and local rehydration. The published Space is static/browser-first; native Windows integration would either host its JS/Web runtime or rebuild the pipeline around ONNX Runtime.

Found more permissive ONNX candidates to benchmark before choosing a dependency: `gravitee-io/bert-small-pii-detection` (Apache-2.0, ONNX, English-focused), `onnx-community/multilang-pii-ner-ONNX` (MIT, ONNX, multilingual including French), `Anonym-IA/V2-camembert-ner-pii-onnx-int8` (MIT, ONNX INT8, French), and `okasi/gliner2-privacy-filter-pii-multi-onnx` (Apache-2.0, ONNX, multilingual GLiNER2).

Rampart-style PII protection belongs around inference/rewrite calls, not inside the live autocorrect loop: autocorrect repairs bounded text locally; the privacy guard masks text before it leaves for a model and rehydrates the response locally.

## 2026-07-13 — WinAppSDK TitleBar control scales its caption reserve wrong

Measured through the new TitleBar layout probe at 200 % display scale: the native `TitleBar` control stamps its caption padding column with `AppWindowTitleBar.RightInset`'s raw physical pixels (upstream `UpdatePadding` in microsoft-ui-xaml `TitleBar.cpp`, no scale division — their TODO 50724421), so the bar reserves scale× the room the caption buttons take. Same px/DIP confusion on `OverlappedPresenter.PreferredMinimum*`, which are physical pixels. Corrected for the Settings window in `SettingsWindow.CaptionInset.cs` (re-stamps the columns in DIPs; delete when the SDK fixes) and by scaling the presenter minimums; every other window using the `TitleBar` control or presenter minimums (LogWindow, Playground) has the same defect — generalization spun off as its own workstream.

Superseded later that day by `80c7444b`: the correction moved to `src/Deckle.Shell.WindowChrome/CaptionInsetCorrection.cs` and was generalized across the affected windows.

## 2026-07-01 — Settings-UX composer doctrine graved; composer gaps against it

Graved the settings composer doctrine into `deckle-settings-ux` (rewrote the skill, added `references/controls-and-behaviour.md`).

The composer in `Deckle.Catalog` does not yet meet all of it — known gaps, so do not read `SettingsComposer.cs` as conformant: fold and page resets fire unconfirmed (`ConfirmationService` is called only from hand-authored pages, never the composer); Slider and Number stay two separate kinds — no paired slider+number magnitude, no fineness→grain derivation, step/ladder still hand-set; `EnabledWhen` still greys, with no transient-busy vs not-applicable distinction. Path Editable mode is built (`FolderPickerEditableCard`). Building each is tracked under the Anytype "Refonte Settings" task.

## 2026-06-13 — Audit reconciliation; CmdPal direction

Reconciled the Anytype task tree against `main` by a code audit (the tree had drifted: e.g. the Playground scission was logged as "nothing extracted yet" while it had long since landed). CmdPal direction settled: a PowerToys Command Palette extension is de-prioritized for Deckle; within Deckle the kept scope is the MCP Anytype server (to be exercised end to end). A standalone PowerToys-CmdPal ↔ Ollama ↔ Anytype showcase bridge is an idea only, not committed, and would live outside Deckle.

## 2026-06-13 — Codex dialogue workflow shape

Chose the first workflow split for Claude↔Codex mediation: `codex-start`, `codex-challenge`, and `codex-dialogue` create Anytype chats so Louis can watch and intervene; `codex-review` and `codex-integrate` stay direct Claude-facing calls by default. Anytype CLI/headless remains a later endpoint option, not part of the first integration.

Superseded on 2026-07-02 by ADR-0001: Deckle now supervises the headless Anytype backend and exposes one resident HTTP MCP host.

## 2026-05-27 — Canonical frontmatter for agent artifacts

Adopted a uniform frontmatter (`type` + `description`) across the agent-facing artifacts — CLAUDE.md files, skills, ADRs, journals. The `update-tree.ps1` hook scrapes it into `TREE.md`, so frontmatter conformance is what keeps the tree readable. The closed `type` list gained `module-journal` and `project-journal` — conventions that already held organically before the format named them.
