---
name: readme-study-voxtral-transformers
description: "Completed study — Voxtral Mini 3B BF16 via Transformers + torch-ROCm on Windows (the high-precision, no-Q4 path). Diagnostic scripts, no bench.py."
module: benchmark/asr/studies/voxtral-transformers
---

# `studies/voxtral-transformers/` — Voxtral Mini 3B BF16 (Transformers + torch-ROCm)

A **completed study** of Voxtral Mini 3B run in **BF16** through Hugging Face Transformers + `torch …+rocm` on Windows — the high-precision path the no-Q4 doctrine points to. Unlike `voxtral-poc` / `voxtral-validation`, this folder is a collection of **diagnostic scripts, not a `bench.py`**.

> **Status — completed study.** Q4 quantization costs French fidelity vs BF16, hence this path. `transformers` must stay `>=4.56,<5.0` on the AMD ROCm wheel: 5.x imports `torch.distributed.tensor`, which that wheel, built with `USE_DISTRIBUTED=0`, lacks. The long-audio "truncation" seen on Voxtral was the bench's token cap, not the model.

## Scripts (reusable patterns kept)

| Script | What it measures |
|---|---|
| `sanity_check.py` | Does Voxtral Mini 3B BF16 load and transcribe on Transformers + torch-ROCm? (1 short sample, VRAM + time.) |
| `smoke_chat_regimes.py` | Does each prompt regime (T1–T7) produce qualitatively coherent output? (1 sample × 7 regimes.) |
| `perf_rtf.py` | Steady-state RTF (warm-up + 3 samples of growing length, preprocess / generate / decode split). |

The one-off analysis scripts (output inspection, sampling sandbox, run summaries that read private run IDs) were removed. The scripts read/write runs under `RUNS_DIR` (`%LOCALAPPDATA%`). Run with a venv carrying `torch …+rocm` and `transformers >=4.56,<5.0` (5.x does not import on that wheel).
