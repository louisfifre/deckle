---
name: readme-bench-tts-audition
description: "Local French TTS audition — a by-ear comparison of ONNX-local TTS engines on the same sentences. Completed spike; Chatterbox kept. Read before running or extending the audition."
type: bench-scenario
module: benchmark/asr/studies/tts-audition
---

# `studies/tts-audition/` — local French TTS audition

A by-ear comparison of **ONNX-local** French text-to-speech engines, all speaking the same sentence set so timbre and prosody can be judged side by side. On-doctrine throughout: no online inference, no `transformers` at inference, weights pulled once and run on plain ONNX Runtime.

> **Status — completed spike (verdict in [`../../JOURNAL.md`](../../JOURNAL.md), 2026-06-14).** Kept: **Chatterbox-Multilingual** (natural French via a reference voice clip, expressivity driven by sampling **temperature**). Also validated: **Supertonic** M1 (male) voice. Dropped: **Orpheus FR** (output stayed incoherent). F5 and the sherpa bundles remain as also-ran baselines. The scripts stay runnable for re-auditioning.

## Engines

| Script | Engine | Role | venv |
|---|---|---|---|
| `chatterbox_synth.py` | Chatterbox-Multilingual (Resemble AI, MIT) | **Keeper** — fp16 LM, voice cloned from a reference clip | `.venv-chatterbox` |
| `supertonic_synth.py` | Supertonic-3 (Supertone) | Validated M1 voice; no expression-tag channel | `.venv-tts-onnx` |
| `synth_onnx.py` | sherpa-onnx bundles (Piper VITS, Kokoro, …) | Baseline providers; produces the Piper ref clip F5 clones | `.venv-tts-onnx` |
| `orpheus_synth.py` | Orpheus FR (canopylabs, ONNX-genai + SNAC) | Dropped — incoherent output | `.venv-orpheus` |
| `f5_synth.py` | F5-TTS French (RASPIAUDIO, ONNX) | Also-ran; voice-clones the Piper ref clip | `.venv-f5` |
| `build_player.py` | — | Builds `ecouter.html`, the listening page, from the run dir | any |
| `_harness.py` | — | Shared: sentence set, EP policy, run dir, stats | — |

## Run order

1. **`synth_onnx.py`** first — its Piper baseline writes `onnx_piper_siwis_01_neutre.wav` into the run dir, which `f5_synth.py` reuses as its voice-clone reference.
2. The other engines, each in its own venv (they're independent).
3. **`build_player.py`** last — scans the run dir for `onnx_*.wav` and renders `ecouter.html` with an auto-generated stats panel.

Every script writes into the shared run dir and is re-runnable after a partial batch (it skips clips already present).

## Shared harness

`_harness.py` centralizes the three things every script must agree on:

- **Sentences** — a small public hand-written set (`PUBLIC_SENTENCES`, versionable) plus `corpus_sentences()`, real French utterances read at **runtime** from the private corpus (`reference_text_gemini`), never inlined into a tracked file.
- **Execution provider** — one toggle, `DECKLE_TTS_EP=cpu|dml`, drives every session. ConvTranspose-bearing graphs (vocoders/decoders) are **always CPU-pinned** — the AMD DirectML ConvTranspose wall (`80070057`) has no auto-fallback; only the big transformer/LM graphs ride DML. CPU vs GPU only moves latency, never the voice.
- **Run dir** — `RUN_DIR = RUNS_DIR / "tts-audition-poc-0002"` under `%LOCALAPPDATA%` (the clean serial rebuild; `-0001` was parallel-contaminated). Plus `stats_record()`, appended per run so the player's stats panel self-documents freshness.

## Models and outputs

- **Weights** live outside the repo under `D:\models\tts\<engine>\`, downloaded once.
- **Outputs** land in the gitignored run dir under `%LOCALAPPDATA%\Deckle\benchmark\runs\tts-audition-poc-0002\`: `onnx_*.wav`, `_stats.jsonl`, and `ecouter.html`.
