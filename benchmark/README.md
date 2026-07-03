---
name: readme-benchmark
description: "Index for Deckle benchmark workspaces — routes ASR evaluation and generic autoresearch instead of mixing them at the root."
type: module-readme
module: benchmark
---

# `benchmark/` — Deckle benchmark workspaces

Top-level container for experimental harnesses that are useful to keep near Deckle but are not product code.

## Layout

| Folder | Responsibility |
|---|---|
| [`asr/`](asr/) | Speech-recognition and local speech/TTS evaluation: ASR corpora, transcription sources, prompt regimes, ASR judges, WER/looping/leak metrics, frozen Voxtral/Phi/TTS studies. |
| [`autoresearch/`](autoresearch/) | Generic Karpathy-style autoresearch workspace: define a goal, generate or edit candidates, measure them with explicit criteria, keep/discard, and iterate. |
| [`lib/`](lib/) | Cross-benchmark infrastructure only: path resolution, env loading, event logs, resource monitoring. |
| [`viewers/`](viewers/) | Generic benchmark result viewers. |

Keep reusable code inside the workspace that owns the concept. ASR-specific judges, prompts, sources, metrics and corpus loaders stay under `asr/`. Shared plumbing that could serve another benchmark stays under `lib/` or `viewers/`. Do not park a Voxtral bench in `autoresearch/` unless the point of that folder is the autoresearch loop itself rather than ASR evaluation.
