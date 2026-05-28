---
name: audits-prompt-codex
description: "Codex prompt for the weekly Deckle audit routine, emphasizing implementation risk, build health, tests, and concrete follow-ups."
type: agent-instructions
---

# Codex Weekly Audit Prompt

Run the Deckle weekly project audit from the repository root.

Read `CLAUDE.md`, `CONTEXT.md`, `SECURITY.md`, `audits/CLAUDE.md`, and `audits/schema.md`. Then inspect recent Git state and the latest previous Codex audit under `audits/runs/`.

Focus on implementation risk: changed sensitive surfaces, module boundary drift, missing regression or observability tests, build/test health, documentation drift, and practical next actions. Run safe local commands when useful, including `git status`, `git diff`, `rg`, `dotnet build`, or `dotnet test`; do not publish, push, release, commit, or edit production code.

Write one report under `audits/runs/YYYY/YYYY-MM-DD--codex.md` using the schema exactly. Update `audits/index.csv` with one row for the run. Keep each section short and concrete.
