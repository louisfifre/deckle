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

The folder splits along two axes : **code** versioned in Git and bound to the worktree, **data** living outside the worktree under `%LOCALAPPDATA%\Deckle\benchmark\` (resolved via `lib/paths.py`) so it survives `git worktree remove`, rebases, and project cleanups. Corpora ground-truthed with Gemini and runs with judge LLM verdicts are non-trivially expensive to regenerate — keeping them in worktree-local gitignored dirs (the old layout) led to repeated silent loss across worktrees. The new layout fixes that.

```
benchmark/                          # CODE — worktree, versioned
├── CLAUDE.md             # this file (agent-facing doctrine)
├── README.md             # human-facing summary
├── .env.example          # template for the keys (.env is gitignored)
│
├── lib/                  # reusable building blocks, shared across benches
│   ├── paths.py          #   code dir vs %LOCALAPPDATA% data dir, run naming
│   ├── corpus.py         #   corpus.jsonl → list[Sample] loader
│   ├── env.py            #   minimal load_dotenv() without external dep
│   ├── event_log.py      #   structured event logger shared by all benches
│   ├── _base_compat.py   #   utilities (force UTF-8 stdout on Windows)
│   ├── sources/          #   ASR drivers (one file = one backend variant)
│   │   ├── _base.py             #   Source.transcribe() → Transcription contract
│   │   ├── voxtral_llamacpp.py  #   Voxtral via llama-mtmd-cli (Vulkan)
│   │   ├── gemini_audio.py      #   Gemini multimodal as ground-truth source
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
├── viewers/              # HTML viewers for runs
│   └── build_html.py     #   generic comparison viewer (auto-discovery)
│
├── prompts/              # versioned, immutable
│   ├── transcription/    #   prompts passed to sources, one TOML per variant
│   └── judges/           #   system prompts for judges
│
├── benches/              # one subfolder = one benched scenario
│   ├── voxtral-poc/                #     legacy POC bench (transformers stack)
│   └── voxtral-validation/         #     current bench (llama-mtmd-cli stack)
│
├── build_corpus_<slug>.py          # top-level builders pulling from telemetry
├── pregenerate_groundtruth_*.py    # ground-truth passes (Gemini, future)
│
├── models-cache/         # GITIGNORED — local GGUF, safetensors (huge)
├── perf-cap/             # GITIGNORED — perf capture scripts ad-hoc
└── .venv-*/              # GITIGNORED — Python environments

%LOCALAPPDATA%\Deckle\benchmark\    # DATA — outside worktree, persistent
├── corpora/
│   └── <slug>/
│       ├── corpus.jsonl                 # v2 payload + reference_text_*
│       ├── groundtruth-*-audit-*.jsonl  # ground-truth API call audit logs
│       └── <audio_file>                 # WAVs referenced by corpus.jsonl
└── runs/
    └── <model>-<phase>-<NNNN>/
        ├── results.jsonl                # one row per (audio_id, regime)
        ├── events.jsonl                 # structured bench events
        ├── notes-louis.json             # exported user notes (if any)
        └── comparison.html              # viewer output (regenerable)
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

A **bench** is a concrete scenario under `benches/<scenario>/bench.py`. It assembles a corpus, one or more sources, prompt regimes, metrics, a judge. Output: `RUNS_DIR/<model>-<phase>-<NNNN>/results.jsonl`.

To add a bench: create `benches/<name>/` with a `bench.py` that imports the `lib/*` blocks and orchestrates them. See `benches/voxtral-validation/` as the current canonical reference (`voxtral-poc/` is the legacy variant pre-pivot).

### Run naming

Canonical pattern : `<model>-<phase>-<NNNN>` where :

- `model` — the slug of the engine under test : `voxtral`, `whisper`, `gemma3`, `ollama-rewrite`, etc.
- `phase` ∈ `{poc, debug, testing, integration}` — bench is a **recurring** harness, not a one-shot :
  - **poc** : first evaluation of a candidate engine.
  - **debug** : narrow tests to isolate a problem (often reduced corpus, no judge LLM).
  - **testing** : systematic passes pre-integration.
  - **integration** : non-regression after integration into Deckle.
- `NNNN` — 4-digit counter per `(model, phase)` pair. Computed automatically by `paths.next_run_id()` / `paths.make_run_dir()`.

Examples : `voxtral-poc-0001`, `voxtral-debug-0003`, `whisper-testing-0001`. Natural sort = model first (categorization), phase second, chronology last. Phases stay bounded — if you find yourself running `poc-0050`, the bench is poorly scoped, not the model.

### Paths

`lib/paths.py` exposes `CORPORA_DIR`, `RUNS_DIR`, and `make_run_dir(model, phase)` / `corpus_dir(slug)`. Use these instead of computing paths from `BENCHMARK_DIR / "runs" / ...` — that style is **deprecated** because it ties data to the worktree.

Override the data root via `DECKLE_BENCHMARK_DIR=path` for testing, sandbox CI, or alternate machines.

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

- `benchmark/.env` carries `ANTHROPIC_API_KEY=...` and/or `GEMINI_API_KEY=...`. **Never commit it** (pattern `*.env` in the root .gitignore).
- To copy the key onto a portable machine: USB drive or password manager, never Git.
- On leak: revoke via the provider console and generate a new one.
- `.env` lives per worktree (gitignored). If a workspace shows both main repo and worktrees side-by-side in VSCodium, the file is easy to create in the wrong folder — check absolute paths if a script complains the key is missing.

## Voxtral specificity — finding 2026-05-27

`llama-mtmd-cli` **has no pure transcription mode**. All calls go through the chat template inherited from Devstral (community shortcut in PR #14862, not an official Voxtral format). This pushes Voxtral into conversational chat — the model paraphrases instead of transcribing : pronouns flip (`je` → `tu`), French technical terms get smoothed into standard conversational style, content gets reformulated.

The official Voxtral transcription format (paper [arXiv 2507.13264](https://arxiv.org/html/2507.13264v1), `mistral_common`) is :

```
<s> [INST] [BEGIN_AUDIO] [AUDIO]...[AUDIO] [/INST] lang:fr [TRANSCRIBE]
```

The special token `[TRANSCRIBE]` is what tells Voxtral « you do ASR, not chat ». mtmd-cli currently injects `[BEGIN_AUDIO]` (fix from #17868 integrated) but **not** `[TRANSCRIBE]`. Test in `voxtral-debug-XXXX` : pass `--prompt "lang:fr [TRANSCRIBE]"` instead of a verbatim instruction, verify the token exists in the GGUF Tekken vocab beforehand.

English (T3 translate régime) remains excellent because chat mode is well-trained for clean instruction-following. The bug only affects verbatim multilingual transcription.

Cohere's quantization study ([arXiv 2407.03211](https://arxiv.org/abs/2407.03211)) shows automatic metrics under-report French degradation by 16× compared to human evaluation on FP16 → 4-bit transitions. On Voxtral, the **community-recommended sweet spot for French quality is Voxtral Mini 3B Q6_K** (we have it cached), not Voxtral Small 24B Q4_K_M which trades capacity for catastrophic French nuance loss.

## Pointers

- [ADR-0010](../docs/adr/0010-backend-asr-pluggable-via-iasrbackend.md) — `IAsrBackend` côté Deckle.
- [ADR-0011](../docs/adr/0011-corpus-normalise-comme-dataset-ml.md) — corpus normalisé comme dataset ML.
- [ADR-0014](../docs/adr/0014-poc-evaluation-voxtral.md) — POC Voxtral, pivot stack transformers → llama.cpp.
- [ADR-0015](../docs/adr/0015-attendre-le-merge-mmvq-vulkan-q3-k-q6-k.md) — MMVQ Vulkan Q3_K/Q6_K en veille passive.
- Skill `deckle-commits` — vocabulaire de scopes : `bench` est le scope canonique pour les commits sous `benchmark/`.
