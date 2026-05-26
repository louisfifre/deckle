---
name: claude-benchmark
description: "Doctrine for the benchmark/ suite — an autonomous box that measures quality and performance of ASR backends (Whisper, Voxtral, future) on private corpora. Read before adding a source, a judge, a corpus, or a bench scenario, and before changing the lib/ contracts or the runs/ output schema."
type: agent-instructions
module: benchmark
---

# CLAUDE.md — `benchmark/`

Instructions for any agent working on the Voxtral / Whisper / future ASR benchmark.

## Folder identity

The `benchmark/` directory is an **autonomous box** dedicated to measuring quality and performance of ASR backends (Whisper, Voxtral, future) on private corpora. It will be extracted into its own repo later; for now it lives inside the Deckle repo to stay close to the telemetry data that feeds the corpora.

Three principles guide every choice here.

- **Agent readability before human readability.** The user does not browse files directly — an agent orchestrates. So rich docstrings, explicit headers, stable vocabulary.
- **Reuse by concept** (sources, judges, corpora, prompts, metrics). Adding a new backend MUST mean adding a file under `lib/sources/` that implements the contract. No duplication.
- **Privacy.** Corpora contain user audio and MUST NEVER be versioned. Each machine brings its own samples.

## Layout

```
benchmark/
├── CLAUDE.md             # this file (agent-facing doctrine)
├── README.md             # human-facing summary
├── .env.example          # template for the Anthropic key (.env is gitignored)
│
├── lib/                  # reusable building blocks, shared across benches
│   ├── corpus.py         #   corpus.jsonl → list[Sample] loader
│   ├── env.py            #   minimal load_dotenv() without external dep
│   ├── event_log.py      #   structured event logger shared by all benches
│   ├── _base_compat.py   #   utilities (force UTF-8 stdout on Windows)
│   ├── sources/          #   ASR drivers (one file = one backend variant)
│   │   ├── _base.py             #   Source.transcribe() → Transcription contract
│   │   ├── _voxtral_common.py   #   shared model loading + DML setup
│   │   ├── voxtral_chat.py      #   Voxtral chat-mode variant (ablation baseline)
│   │   ├── voxtral_transcribe.py #  Voxtral via apply_transcription_request (Phase 3)
│   │   └── whisper_cpp.py       #   Whisper.cpp via whisper-cli.exe
│   ├── judges/           #   LLM evaluators
│   │   ├── _base.py      #     Judge.score_row() / score_macro() contract
│   │   ├── claude.py     #     Anthropic API, Haiku per-row + Opus macro
│   │   └── gemini.py     #     Gemini API, per-row alternative
│   ├── metrics/          #   objective rules, no LLM call
│   │   ├── wer.py        #     WER + CER via jiwer
│   │   ├── looping.py    #     n-gram looping detection
│   │   └── leak.py       #     known hallucinations + custom leaks
│   └── monitor/
│       ├── gpu_monitor.ps1   # PowerShell GPU/RAM sampler (manual launch)
│       └── joiner.py         # joins gpu_monitor output with a bench run
│
├── corpora/              # GITIGNORED — each machine brings its own
│   └── voxtral-poc/      #   example: corpus.jsonl + *.wav
│
├── prompts/              # versioned, immutable
│   ├── transcription/    #   prompts passed to sources, one TOML per variant
│   │   ├── voxtral_chat.toml         # regimes for voxtral_chat
│   │   └── voxtral_transcribe.toml   # regimes for voxtral_transcribe (V1..V5)
│   ├── judges/           #   system prompts for judges
│   │   ├── claude_per_row.md
│   │   ├── gemini_per_row.md
│   │   └── legacy_ollama_judge.md
│   └── whisper_initial.txt           # Deckle Whisper initial prompt
│
├── benches/              # one subfolder = one benched scenario
│   └── voxtral-poc/
│       ├── bench.py      #     orchestrator
│       └── README.md     #     scenario description
│
├── runs/                 # GITIGNORED — disposable outputs
└── models-cache/         # GITIGNORED — local GGUF, safetensors
```

## Concepts

### Source

A **source** is a transcription backend. It exposes `transcribe(audio_path, prompt, max_new_tokens) → Transcription`. The contract is minimal so a bench can swap sources freely.

To add a source:

1. Create `lib/sources/<name>.py` defining a class that implements (duck-typed OK) `lib.sources._base.Source`.
2. The class loads the model in `__init__` (costly load, paid once). `transcribe()` is called in a loop and MUST stay fast.
3. Update the `--source` flag of any bench that should expose it.

### Judge

A **judge** scores transcriptions. Two modes.

- `score_row(hypothesis, reference, regime, source) → JudgeScore` — per-row, light model (Claude Haiku), called in the loop.
- `score_macro(run_summary, examples) → JudgeScore` — macro, heavyweight model (Claude Opus), called once at the end of a run with a curated summary plus examples selected by the per-row pass.

To add a judge: create `lib/judges/<name>.py` and implement at least `score_row`.

### Corpus

A corpus lives under `corpora/<slug>/` with:

- `corpus.jsonl` — one line per sample, Deckle telemetry payload (`transcription_id`, `audio_file`, `text` = Whisper large-v3 reference, `duration_seconds`, `tier`).
- `<audio_file>` — the WAVs referenced in `corpus.jsonl`.

**Corpora MUST NEVER be versioned.** To get one on your machine, either extract from `%LOCALAPPDATA%\Deckle\telemetry\`, or capture a fresh one via Deckle in telemetry mode.

### Bench

A **bench** is a concrete scenario under `benches/<scenario>/bench.py`. It assembles a corpus, one or more sources, prompt regimes, metrics, a judge. Output: `runs/<run-id>/results.jsonl`.

To add a bench: create `benches/<name>/` with a `bench.py` that imports the `lib/*` blocks and orchestrates them. See `benches/voxtral-poc/` as the canonical reference.

## Code conventions

- **stdout encoding.** Force UTF-8 at the start of every script via `lib._base_compat._ensure_stdout_utf8()`. PowerShell is cp1252 by default; without this, accents and box drawing chars (`─`) crash with `UnicodeEncodeError`.
- **Lazy imports** of heavy dependencies (torch, anthropic, …) at instantiation, not at top-level. Lets a bench instantiate source A without paying the import cost of source B's library.
- **JSONL serialization.** One line per row, written and flushed on the fly. If the bench crashes, the rows already processed are persisted. No global buffering.
- **Errors vs exceptions.** A failed transcription returns `Transcription(ok=False, error="...")`, **not** an exception. The bench writes the row and continues. An exception bubbles up the whole crash.
- **Docstrings.** Prose in short paragraphs, the *why* dominates the *what*. Consistent with the `deckle-workflow` doctrine (*Code comments* section) of the parent repo. No docstring-CV ("This function does X.").

## Python environments

- `.venv-voxtral-dml/` — primary venv for Voxtral via Transformers + torch-directml. Bootstrap: `python312 -m venv .venv-voxtral-dml` then `pip install torch torch-directml "transformers>=4.55,<5.0" mistral-common[audio] soundfile librosa jiwer anthropic`.
- `.venv-voxtral/` — legacy venv for the llama.cpp stack (Phase 1/2), archivable.

Both are gitignored (pattern `.venv*/`).

## Security

- `benchmark/.env` carries `ANTHROPIC_API_KEY=...`. **Never commit it** (pattern `*.env` in the root .gitignore).
- To copy the key onto a portable machine: USB drive or password manager, never Git.
- On leak: revoke via https://console.anthropic.com/settings/keys and generate a new one.
