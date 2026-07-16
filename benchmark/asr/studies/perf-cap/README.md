---
name: readme-study-perf-cap
description: "Frozen Voxtral GGUF performance-characterization session (2026-05-26) over llama.cpp Vulkan on the RX 7900 XT. Reference scripts; findings live in the benchmark JOURNAL."
type: study
module: benchmark/asr/studies/perf-cap
---

# `studies/perf-cap/` — Voxtral perf characterization (2026-05-26)

A dated, **frozen** session that characterized the throughput of Voxtral GGUF variants through `llama-mtmd-cli` on the Vulkan backend (RX 7900 XT), to decide which quantization fit the VRAM/latency budget.

> **Status — frozen study.** Kept for the method and the numbers; not maintained as a live bench. Findings (Voxtral 24B Q4_K_M validated via llama.cpp Vulkan; the Q4 fidelity cost; the Vulkan timing breakdown) are recorded in [`../../JOURNAL.md`](../../JOURNAL.md).

## Pipeline (reusable scripts kept)

1. **`download-models.ps1`** — fetches the GGUFs (curl with resume, serial to avoid HF rate-limits).
2. **`run-all.ps1`** — orchestrator: loops `profile-config.ps1` + `parse_vulkan_log.py` over every config in the model cache.
3. **`profile-config.ps1`** / **`profile-server-text.ps1`** — profile one config (model GGUF + mmproj), CLI and server-text paths.
4. **`parse_vulkan_log.py`** — parses a `llama-mtmd-cli` log captured with `GGML_VK_PERF_LOGGER=1`, classifies the Vulkan timing blocks (warmup / audio_prefill / lm_prefill / gen) and aggregates `gen` into throughput metrics.

The dated session/debug capture scripts were removed — their numbers are in the JOURNAL.

## Layout note

These scripts predate the code/data split and write to worktree-local `..\models-cache\` and `..\runs\perf-cap\` — paths now relative to `studies/perf-cap/`, not the ASR root, and therefore **stale**. The folder is a frozen capture; if the characterization ever resumes, port it onto [`benchmark/lib/paths.py`](../../../lib/paths.py) first.
