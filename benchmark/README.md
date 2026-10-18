---
name: readme-benchmark
description: "Index for Deckle benchmark workspaces — routes autoresearch, paragraph-rewrite evaluation and shared plumbing instead of mixing them at the root."
module: benchmark
---

# `benchmark/` — Deckle benchmark workspaces

Top-level container for experimental harnesses that are useful to keep near Deckle but are not product code.

## Layout

| Folder | Responsibility |
|---|---|
| [`autoresearch/`](autoresearch/) | Generic Karpathy-style autoresearch workspace: define a goal, generate or edit candidates, measure them with explicit criteria, keep/discard, and iterate. |
| [`lib/`](lib/) | Cross-benchmark infrastructure only: path resolution, env loading, event logs, resource monitoring. |
| [`rewrite/`](rewrite/) | Evaluation of the paragraph rewrite; today the paragraph-gate study ([`rewrite/paragraph-gate`](rewrite/paragraph-gate/)) running the production prompt, engine and diff gate on a local model. |
| [`viewers/`](viewers/) | Generic benchmark result viewers. |

Keep reusable code inside the workspace that owns the concept. Shared plumbing that could serve another benchmark stays under `lib/` or `viewers/`.
