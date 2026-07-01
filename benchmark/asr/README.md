---
name: readme-benchmark-asr
description: "Human-facing entry point for benchmark/asr — ASR-specific harness pieces and frozen speech studies."
type: module-readme
module: benchmark/asr
---

# `benchmark/asr/` — Deckle ASR evaluation

Workspace for ASR-specific benchmark material: private corpus loaders, speech
sources, transcription prompts, ASR judges, ASR metrics, and frozen speech
studies. Generic benchmark plumbing lives one level up in [`../lib/`](../lib/),
and generic result viewers live in [`../viewers/`](../viewers/).

## What lives here

Whisper (whisper.cpp) is Deckle's shipped transcription path; this workspace is
where alternatives get measured against it. The live, ASR-specific harness —
`lib/` (corpus loader, transcription sources, ASR judges, ASR metrics),
`prompts/`, and `build_corpus.py` — stays here. Shared viewers and
infrastructure live at `benchmark/`; completed or abandoned model spikes are
frozen under [`studies/`](studies/), with their findings in
[`JOURNAL.md`](JOURNAL.md).

## Layout

```
benchmark/asr/
├── CLAUDE.md        agent-facing ASR doctrine
├── README.md        this file
├── build_corpus.py  Deckle telemetry -> ASR corpus
├── lib/             ASR corpus loader, sources, judges, metrics
├── prompts/         transcription, judge, and Whisper initial prompts
└── studies/         frozen completed/abandoned speech spikes
```

**Code vs data split.** Corpora, runs and model caches do **not** live in the
worktree. They are resolved under `%LOCALAPPDATA%\Deckle\benchmark\` via
[`../lib/paths.py`](../lib/paths.py), so they survive `git worktree` churn and
rebases.

## Extending

To add a new ASR backend, drop a file under `lib/sources/` implementing the
`Source` contract from `lib/sources/_base.py`. A new ASR judge goes under
`lib/judges/`; a new ASR metric goes under `lib/metrics/`. A generic viewer,
logger, monitor, or path helper belongs in `../viewers/` or `../lib/`.

Judge keys, when needed, live in `benchmark/.env`.
