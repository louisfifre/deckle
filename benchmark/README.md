---
name: readme-benchmark
description: "Human-facing entry point for the benchmark/ suite — the live harness, the frozen studies, where to look next. Read first when browsing the folder; for agent-facing doctrine read CLAUDE.md."
type: module-readme
module: benchmark
---

# `benchmark/` — Deckle ASR evaluation suite

Autonomous suite for measuring quality and performance of speech backends on private audio corpora extracted from Deckle telemetry. Primarily an **ASR** (transcription) evaluation harness; it also hosted a one-off local-**TTS** audition. Lives in the Deckle repo for now to stay close to the data; will be split into its own repo later.

The folder is **agent-orchestrated**: an LLM agent (typically Claude Code) is the primary reader of the code and runner of the benches. Most files carry rich docstrings written for that consumer. Humans browsing the folder land here first.

> **For agent-facing doctrine** — contracts, code conventions, security, run naming, the no-Q4 rule — read [`CLAUDE.md`](CLAUDE.md). This README only gives the lay of the land.

## Current focus

Whisper streaming (large-v3 via whisper.cpp) is the shipped transcription path and meets the daily need. The **Voxtral** and **Phi-4** alternatives explored here are **dropped** — none was clearly better than Whisper at an accessible cost (see [`JOURNAL.md`](JOURNAL.md), 2026-06-15).

What stays live is the **harness**: `lib/` (sources, judges, metrics, paths), `prompts/`, `viewers/`, and the corpus builders. It's a recurring evaluation tool, ready to pick up when a candidate backend is worth re-measuring. The completed spikes are frozen under [`studies/`](studies/) for reference — the ONNX/DirectML know-how in particular is kept for future local inference inside Deckle.

## Layout at a glance

```
benchmark/
├── CLAUDE.md            agent-facing doctrine (read first if you're an agent)
├── README.md            this file
├── .env.example         template for ANTHROPIC_API_KEY / GEMINI_API_KEY (.env is gitignored)
├── lib/                 reusable building blocks — sources, judges, metrics, monitor, paths
├── prompts/             versioned prompt material (transcription + judges + whisper initial)
├── viewers/             HTML result viewer (build_html.py)
├── build_corpus_voxtral_val_30.py     corpus builder (Deckle telemetry → corpus)
├── pregenerate_groundtruth_gemini.py  ground-truth transcriber (Gemini)
└── studies/            frozen completed/abandoned spikes (see table)
```

**Code vs data split.** Corpora, runs and model caches do **not** live in the worktree — they're resolved under `%LOCALAPPDATA%\Deckle\benchmark\` via [`lib/paths.py`](lib/paths.py), so they survive `git worktree` churn and rebases. Always read/write data paths through `paths.py` (`CORPORA_DIR`, `RUNS_DIR`), never from the code dir.

## Studies (frozen)

Each is a completed or abandoned spike, kept for reference; findings are in [`JOURNAL.md`](JOURNAL.md), the per-folder index in [`studies/README.md`](studies/README.md).

| Study | What | Outcome |
|---|---|---|
| [`studies/voxtral-poc/`](studies/voxtral-poc/) | Voxtral Mini 3B (DirectML) vs Whisper, 5 regimes, LLM judge | Dropped — also the bench **template** |
| [`studies/voxtral-validation/`](studies/voxtral-validation/) | Voxtral 24B Q4_K_M (llama.cpp Vulkan) vs Gemini ground truth | Dropped |
| [`studies/voxtral-transformers/`](studies/voxtral-transformers/) | Voxtral Mini 3B BF16 (Transformers + torch-ROCm) | Dropped — no decisive win |
| [`studies/voxtral-onnx-poc/`](studies/voxtral-onnx-poc/) | Voxtral 3B ONNX/DirectML | Blocked (KV cache) — ONNX reference |
| [`studies/tts-audition/`](studies/tts-audition/) | Local French TTS audition | Chatterbox kept |
| [`studies/PhiBench/`](studies/PhiBench/) | Phi-4 multimodal OGA bench (C#) | Blocked upstream |
| [`studies/perf-cap/`](studies/perf-cap/) | Voxtral GGUF perf characterization | Frozen reference |

## Running and extending

The harness building blocks live under `lib/`; a bench wires them into a scenario. The frozen [`studies/voxtral-poc/bench.py`](studies/voxtral-poc/bench.py) is the canonical template. A bench needs a corpus under `corpora/<slug>/` (resolved via `paths.py`, under `%LOCALAPPDATA%` — never versioned, each machine brings its own samples) and an `.env` at the `benchmark/` root with the judge keys (copy [`.env.example`](.env.example)).

To start a **new** bench, create `benches/<name>/` with a `bench.py` and `README.md`, copying the template. To add a new **source** (ASR backend), drop a file under `lib/sources/` implementing the `Source` contract from `lib/sources/_base.py`. A new **judge** goes under `lib/judges/` (`score_row` from `lib/judges/_base.py`). Contracts are spelled out in [`CLAUDE.md`](CLAUDE.md).

## Dependencies

- Python 3.12+ on Windows (PowerShell 7).
- Per-bench venv (the spike scripts name theirs in their docstring). Caches and venvs are gitignored.
- Optional `whisper.cpp` build at `whisper.cpp/build/bin/whisper-cli.exe` for the `whisper-cpp` source.
- `ANTHROPIC_API_KEY` and/or `GEMINI_API_KEY` in `benchmark/.env` for the LLM judges. Without a key, a bench skips the judge with a loud warning and still computes the objective metrics.
