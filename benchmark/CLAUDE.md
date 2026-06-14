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

## Model precision — no Q4 for ASR/TTS

Do not quantize speech models to 4-bit (Q4_K_M, Q4F16, INT4) for Deckle. On the private French corpus, Q4 costs a fidelity that automatic metrics barely register but a reader feels — pronoun and suffix slips, occasional hallucinations, far higher WER variance — while BF16/FP16 stay clean (JOURNAL 2026-05-27, 2026-05-28). Consistent with Cohere [arXiv 2407.03211] (−16.6% human-perceived FR degradation FP16→4-bit vs −0.3% on automatic metrics). Use BF16 or FP16; treat anything 4-bit as a smoke-test convenience, never a shipping candidate. The same caution carried into the TTS audition.

## Structure — live harness vs frozen studies

- **Live and reusable** (root): `lib/` (sources, judges, metrics, monitor, paths), `prompts/`, `viewers/`, the corpus builders. This is the recurring harness — extend it, never fork it.
- **Frozen** (`studies/`): completed or abandoned spikes, one folder per topic, each with a README and its findings already in `JOURNAL.md`. Reference material, not maintained. A new active bench is created fresh under `benches/` (recreated when needed), using `studies/voxtral-poc/bench.py` as the template.

## ONNX-local inference

The ONNX Runtime + DirectML path is kept beyond ASR: it's the foundation for future **local inference inside Deckle** — running models on the GPU, no cloud, no `transformers` at inference. `studies/voxtral-onnx-poc/smoke_test.py` and `studies/tts-audition/` are the worked references. One AMD pitfall to carry forward: ConvTranspose-bearing graphs (vocoders, upsamplers) crash on DirectML (`80070057`) with no auto-fallback — pin them to the CPU EP while the big transformer graphs ride DML (`studies/tts-audition/_harness.py` → `providers()`).

## The four concepts

A bench assembles them; each is swappable by adding one file under its `lib/` folder.

- **Source** — a transcription backend (`transcribe(audio, prompt, …) → Transcription`). Loads its model once, transcribes in a loop.
- **Judge** — an LLM scorer: light model per-row, heavy model once at run end.
- **Corpus** — a folder of WAVs plus a `corpus.jsonl` of Deckle telemetry payloads.
- **Bench** — a concrete scenario wiring corpus + sources + prompt regimes + metrics + judge into a run.

## Run naming

`<model>-<phase>-<NNNN>`. `phase ∈ {poc, debug, testing, integration}` — the bench is a recurring harness, not one-shot: poc (first eval of a candidate), debug (isolate a problem), testing (systematic pre-integration), integration (non-regression after shipping). Sort is model, then phase, then chronology. Phases stay bounded — a `poc-0050` means the bench is mis-scoped, not the model.
