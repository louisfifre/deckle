---
description: Dated project notes for Deckle — cross-cutting findings too dated for a CLAUDE.md, too light for an ADR.
type: project-journal
---

# Journal — Deckle

Project-level dated notes: a finding, a milestone, a usage observation — worth recording with a date, but not heavy enough for an ADR nor timeless enough for a CLAUDE.md. Most recent on top.

## 2026-07-01 — Settings-UX composer doctrine graved; composer gaps against it

Graved the settings composer doctrine into `deckle-settings-ux` (rewrote the skill, added `references/controls-and-behaviour.md`).

The composer in `Deckle.Catalog` does not yet meet all of it — known gaps, so do not read `SettingsComposer.cs` as conformant: fold and page resets fire unconfirmed (`ConfirmationService` is called only from hand-authored pages, never the composer); Slider and Number stay two separate kinds — no paired slider+number magnitude, no fineness→grain derivation, step/ladder still hand-set; `EnabledWhen` still greys, with no transient-busy vs not-applicable distinction. Path Editable mode is built (`FolderPickerEditableCard`). Building each is tracked under the Anytype "Refonte Settings" task.

## 2026-06-13 — Audit reconciliation; CmdPal direction

Reconciled the Anytype task tree against `main` by a code audit (the tree had drifted: e.g. the Playground scission was logged as "nothing extracted yet" while it had long since landed). CmdPal direction settled: a PowerToys Command Palette extension is de-prioritized for Deckle; within Deckle the kept scope is the MCP Anytype server (to be exercised end to end). A standalone PowerToys-CmdPal ↔ Ollama ↔ Anytype showcase bridge is an idea only, not committed, and would live outside Deckle.

## 2026-06-13 — Codex dialogue workflow shape

Chose the first workflow split for Claude↔Codex mediation: `codex-start`, `codex-challenge`, and `codex-dialogue` create Anytype chats so Louis can watch and intervene; `codex-review` and `codex-integrate` stay direct Claude-facing calls by default. Anytype CLI/headless remains a later endpoint option, not part of the first integration.

## 2026-05-27 — Canonical frontmatter for agent artifacts

Adopted a uniform frontmatter (`type` + `description`) across the agent-facing artifacts — CLAUDE.md files, skills, ADRs, journals. The `update-tree.ps1` hook scrapes it into `TREE.md`, so frontmatter conformance is what keeps the tree readable. The closed `type` list gained `module-journal` and `project-journal` — conventions that already held organically before the format named them.
