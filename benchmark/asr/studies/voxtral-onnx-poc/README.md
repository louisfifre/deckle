---
name: readme-study-voxtral-onnx
description: "Completed POC — Voxtral Mini 3B via ONNX Runtime + DirectML. Smoke pipeline kept as the ONNX-local inference reference; blocked on KV-cache decode. Findings in the benchmark JOURNAL."
type: study
module: benchmark/asr/studies/voxtral-onnx-poc
---

# `studies/voxtral-onnx-poc/` — Voxtral 3B ONNX / DirectML (route 2)

A POC that ran Voxtral Mini 3B 2507 through **ONNX Runtime + DirectML** on the RX 7900 XT — the "Microsoft ecosystem on AMD" route. Ported from the `onnx-community/Voxtral-Mini-3B-2507-ONNX` reference, adapted to the canonical Voxtral transcription prompt.

> **Status — completed study, blocked.** Transcription is clean in **FP16** with the canonical prompt; **Q4F16** showed a visible hallucination and a grammar slip on a single sample (one data point, consistent with the no-Q4 doctrine). The blocker is **KV-cache decode**: it degenerates at step 1 while the no-KV `O(N²)` path stays correct — the lead is `position_ids` from a masked cumsum rather than a plain arange. Full findings in [`../../JOURNAL.md`](../../JOURNAL.md) (2026-05-28).

`smoke_test.py` is kept as the **ONNX-local inference reference** (see [`../../CLAUDE.md`](../../CLAUDE.md) → *ONNX-local inference*): it confirms the ONNX pipeline transcribes coherently across decoder quantizations (FP16, Q4F16) and audio durations (short / long-form). The earlier KV-cache and tokenizer debug one-offs were removed — their findings live in the JOURNAL.

Run with the DirectML ONNX venv; weights come from the `onnx-community` export. Runs are written under `RUNS_DIR` (`%LOCALAPPDATA%`) per the code/data split.
