---
name: readme-benchmark
description: "Human-facing entry point for the benchmark/ suite — what it is, how a bench is laid out, where to look next. Read first when browsing the folder; for agent-facing doctrine (contracts, conventions, security) read CLAUDE.md."
type: module-readme
module: benchmark
---

# `benchmark/` — Deckle ASR evaluation suite

Autonomous suite for measuring quality and performance of ASR backends (Whisper, Voxtral, future) on private audio corpora extracted from Deckle telemetry. Lives in the Deckle repo for now to stay close to the data; will be split into its own repo later.

The folder is **agent-orchestrated**: an LLM agent (typically Claude Code) is the primary reader of the code and runner of the benches. Most files carry rich docstrings written for that consumer. Humans browsing the folder land here first.

> **For agent-facing doctrine** — contracts, code conventions, security, environments — read [`CLAUDE.md`](CLAUDE.md). This README only gives the lay of the land.

## How to run a bench

Each bench lives under [`benches/<scenario>/`](benches/) with its own `bench.py` and `README.md`. The single bench currently in tree:

- [`benches/voxtral-poc/`](benches/voxtral-poc/) — Voxtral Mini 3B vs Whisper reference on a private corpus, 5 prompt regimes, Claude Haiku per-row judge.

To run it, follow the steps in its README — they assume a `.venv-voxtral-dml` venv, a `corpora/voxtral-poc/` corpus, and an `ANTHROPIC_API_KEY` in `.env`.

## Layout at a glance

```
benchmark/
├── CLAUDE.md         agent-facing doctrine (read first if you're an agent)
├── README.md         this file
├── .env.example      template for ANTHROPIC_API_KEY (.env is gitignored)
├── lib/              reusable building blocks — sources, judges, metrics, monitor
├── prompts/          versioned prompt material (sources + judges + Whisper initial)
├── benches/          one subfolder per benched scenario
├── corpora/          GITIGNORED — per-machine audio + reference text
├── runs/             GITIGNORED — disposable bench outputs
└── models-cache/     GITIGNORED — local model artifacts
```

## Adding things

- **A new source (ASR backend)** — drop a file under `lib/sources/` implementing the `Source` contract from `lib/sources/_base.py`. See `lib/sources/voxtral_transcribe.py` as the reference.
- **A new judge** — drop a file under `lib/judges/` implementing at least `score_row` from `lib/judges/_base.py`. See `lib/judges/claude.py`.
- **A new bench scenario** — create `benches/<name>/` with a `bench.py` that wires a corpus, one or more sources, prompt regimes, metrics, and a judge. Use `benches/voxtral-poc/bench.py` as the template, and add a `README.md` describing what and why.

The contracts and conventions for each of these are spelled out in [`CLAUDE.md`](CLAUDE.md).

## Dependencies

- Python 3.12+ on Windows (PowerShell 7).
- Per-bench venv (e.g. `.venv-voxtral-dml/` for Voxtral via Transformers + torch-directml). Bootstrap recipe in [`CLAUDE.md`](CLAUDE.md) → *Python environments*.
- Optional `whisper.cpp` build at `whisper.cpp/build/bin/whisper-cli.exe` for the `whisper-cpp` source.
- `ANTHROPIC_API_KEY` in `benchmark/.env` for the Claude judge (per-row Haiku, macro Opus).
