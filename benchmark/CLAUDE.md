---
description: Autonomous box measuring ASR backend quality and performance on private corpora — sources, judges, corpora, benches.
type: agent-instructions
---

# CLAUDE.md — `benchmark/`

The `benchmark/` directory is an **autonomous box** measuring quality and performance of ASR backends (Whisper, Voxtral, future) on private corpora. Meant to be extracted into its own repo later; for now it lives in the Deckle repo to stay close to the telemetry that feeds the corpora. It is agent-orchestrated, not browsed by hand — hence rich docstrings, stable vocabulary, reuse by concept over duplication.

Three non-negotiables:

- **Reuse by concept** (source, judge, corpus, prompt, metric). A new backend is a new file under `lib/sources/` implementing the existing contract — never a fork.
- **Privacy.** Corpora hold user audio and are NEVER versioned. Each machine brings its own samples (extracted from Deckle telemetry, or captured fresh).
- **Code vs data split.** Code is versioned in the worktree; data (corpora, runs) lives outside it under `%LOCALAPPDATA%`, resolved via `lib/paths.py`. Deliberate: ground-truthed corpora and judged runs are expensive to regenerate, and the old worktree-local layout lost them silently across `git worktree remove` and rebases. Always resolve data paths through `paths.py`, never from the code dir.

## The four concepts

A bench assembles them; each is swappable by adding one file under its `lib/` folder.

- **Source** — a transcription backend (`transcribe(audio, prompt, …) → Transcription`). Loads its model once, transcribes in a loop.
- **Judge** — an LLM scorer: light model per-row, heavy model once at run end.
- **Corpus** — a folder of WAVs plus a `corpus.jsonl` of Deckle telemetry payloads.
- **Bench** — a concrete scenario wiring corpus + sources + prompt regimes + metrics + judge into a run.

## Run naming

`<model>-<phase>-<NNNN>`. `phase ∈ {poc, debug, testing, integration}` — the bench is a recurring harness, not one-shot: poc (first eval of a candidate), debug (isolate a problem), testing (systematic pre-integration), integration (non-regression after shipping). Sort is model, then phase, then chronology. Phases stay bounded — a `poc-0050` means the bench is mis-scoped, not the model.
