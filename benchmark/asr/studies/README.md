---
name: readme-studies
description: "Index of frozen benchmark studies — completed or abandoned ASR/TTS spikes kept for reference. Each subfolder has its own README; findings live in the benchmark JOURNAL."
type: module-readme
module: benchmark/asr/studies
---

# `studies/` — frozen benchmark spikes

Completed or abandoned evaluation spikes, kept as reference. Each is parked, not maintained; its durable findings are in [`../JOURNAL.md`](../JOURNAL.md). The live ASR-specific harness stays at the [ASR root](../README.md) (`lib/`, `prompts/`, `build_corpus.py`), while shared viewers and infrastructure live at `benchmark/`.

| Study | What it explored | Outcome |
|---|---|---|
| [`voxtral-poc/`](voxtral-poc/) | Voxtral Mini 3B (DirectML) vs Whisper, 5 prompt regimes, LLM judge | Dropped — kept as the bench **template** |
| [`voxtral-validation/`](voxtral-validation/) | Voxtral 24B Q4_K_M (llama.cpp Vulkan) vs Gemini ground truth, 30 samples | Dropped |
| [`voxtral-transformers/`](voxtral-transformers/) | Voxtral Mini 3B BF16 (Transformers + torch-ROCm) — the no-Q4 path | Dropped — works, no decisive win |
| [`voxtral-onnx-poc/`](voxtral-onnx-poc/) | Voxtral 3B via ONNX Runtime + DirectML | Blocked on KV-cache decode — ONNX-local reference |
| [`tts-audition/`](tts-audition/) | Local French TTS audition (Chatterbox / Supertonic / Orpheus / F5 / sherpa) | Chatterbox kept |
| [`PhiBench/`](PhiBench/) | Phi-4 multimodal via OGA (C#) | Blocked upstream (#1455) |
| [`perf-cap/`](perf-cap/) | Voxtral GGUF perf characterization (llama.cpp Vulkan) | Frozen reference |

Why these are kept and not deleted: each carries a **reusable pattern** — a `Source` implementation, a measurement method, or the ONNX-local inference plumbing — or a worked reference for a path that might reopen. The pure one-off diagnostic scripts were removed; their findings are in the JOURNAL.
