---
name: audits-readme
description: "Explains the root audits workspace: recurring agent reviews, shared prompts, schema, runs, and index."
type: module-readme
---

# Deckle Audits

This folder hosts recurring project audits performed by Codex, Claude, or a future local agent runner. An audit is not an automated test: it reads the repository, compares the current state against Deckle doctrine, and writes a short structured report.

The source of truth for audit instructions is [`CLAUDE.md`](CLAUDE.md). The report contract lives in [`schema.md`](schema.md). Agent-specific entry prompts live under [`prompts/`](prompts/). Dated outputs live under [`runs/`](runs/).

## Boundaries

Audits MUST stay lightweight. They MAY run safe local commands such as `git status`, `git diff`, `rg`, `dotnet build`, or `dotnet test` when useful, but they MUST NOT publish, push, release, edit production code, or create commits.

Audits MUST produce observations, risks, and follow-ups. They MUST NOT silently rewrite doctrine. If a stable decision emerges, it is routed through the normal Deckle habitats: module `CLAUDE.md`, `CONTEXT.md`, `JOURNAL.md`, ADR, reference, or research note.

Reports MUST be short enough to compare week over week. A section that has nothing meaningful to say records `Status: not-reviewed` or `Status: stable` rather than filling space.

## Layout

```text
audits/
  README.md
  CLAUDE.md
  schema.md
  index.csv
  prompts/
    codex.md
    claude.md
  runs/
    2026/
      .gitkeep
  templates/
    weekly-audit.md
```

## Cadence

The default cadence is weekly. A missed week is acceptable: the next run compares against the latest available run, not against an imagined calendar state.
