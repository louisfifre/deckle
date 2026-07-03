---
description: ASR-specific benchmark workspace — sources, judges, corpora, metrics, and frozen speech studies.
type: agent-instructions
---

# AGENTS.md — `benchmark/asr/`

The `benchmark/asr/` directory owns only the ASR-specific side of benchmark work: speech corpora, transcription sources, prompt regimes, ASR judges, ASR metrics, and frozen speech studies.

Keep the split clear:

- ASR-specific code lives here under `lib/`, `prompts/`, and `studies/`.
- Cross-benchmark infrastructure lives in `benchmark/lib/`.
- Generic result viewers live in `benchmark/viewers/`.
- Autoresearch loops live in `benchmark/autoresearch/`.

## Non-negotiables

- **Reuse by concept.** A new backend is a new file under `lib/sources/` implementing the existing contract, not a fork of a bench.
- **Privacy.** Corpora hold user audio and are never versioned. Each machine brings its own samples, extracted from Deckle telemetry or captured fresh.
- **Code vs data split.** Code is versioned in the worktree; data (corpora, runs) lives outside it under `%LOCALAPPDATA%`, resolved via `../lib/paths.py`.

## Model precision — no Q4 for ASR/TTS

Do not quantize speech models to 4-bit (Q4_K_M, Q4F16, INT4) for Deckle. On the private French corpus, Q4 costs fidelity that automatic metrics barely register but a reader feels — pronoun and suffix slips, occasional hallucinations, far higher WER variance — while BF16/FP16 stay clean. Use BF16 or FP16; treat anything 4-bit as a smoke-test convenience, never a shipping candidate.

## Concepts

- **Source** — a transcription backend (`transcribe(audio, prompt, ...) -> Transcription`). Loads its model once, transcribes in a loop.
- **Judge** — an LLM scorer for ASR output.
- **Corpus** — a folder of WAVs plus a `corpus.jsonl` of Deckle telemetry payloads.
- **Bench** — a concrete scenario wiring corpus + sources + prompt regimes + metrics + judge into a run.
