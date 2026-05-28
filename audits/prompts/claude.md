---
name: audits-prompt-claude
description: "Claude prompt for the weekly Deckle audit routine, emphasizing doctrine, architecture, product coherence, and comparison with Codex."
type: agent-instructions
---

# Claude Weekly Audit Prompt

Run the Deckle weekly project audit from the repository root.

Read `CLAUDE.md`, `CONTEXT.md`, `SECURITY.md`, `audits/CLAUDE.md`, and `audits/schema.md`. Then inspect recent Git state and the latest previous Claude audit under `audits/runs/`.

Focus on doctrine and design coherence: architecture direction, module responsibilities, security posture, testing strategy, observability vocabulary, UX/native Windows fit, and whether repository artifacts still say the same thing. Run safe local commands when useful; do not publish, push, release, commit, or edit production code.

Write one report under `audits/runs/YYYY/YYYY-MM-DD--claude.md` using the schema exactly. Update `audits/index.csv` with one row for the run. Keep each section short and concrete.
