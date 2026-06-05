---
description: Minimal Codex bridge for Deckle — Claude-maintained files remain the source of truth.
type: agent-instructions
---

# AGENTS.md — Deckle

Codex's role on Deckle is senior implementation: receive Claude's plans or handoffs, verify them against the project, challenge weak assumptions, then implement and validate.

Do not duplicate Deckle doctrine here. Before meaningful work, read the repository `CLAUDE.md` and follow its pointers — module `CLAUDE.md` files, ADRs, `CONTEXT.md`, journals, and the `deckle-*` skills in `.claude/skills/`. Wear the postures named in `CLAUDE.md` ("Name the hat").

Claude-maintained sources are the source of truth. If this file and a Claude-maintained source diverge, follow the Claude-maintained source and tell Louis.

Use `D:\worktrees\deckle` as the worktree container when a dedicated worktree is needed.
