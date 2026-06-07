---
description: ONNX Runtime inference home — today the Silero VAD v5 speech detector and the get_speech_timestamps port.
type: agent-instructions
---

# CLAUDE.md — Deckle.Inference.Onnx

Support module: runs ONNX models on the CPU execution provider, isolating the `Microsoft.ML.OnnxRuntime` dependency from the rest of the app. It owns no domain state and depends on almost nothing — callers hand it a model path and audio, it returns results. Dependencies point one way, toward it.

Today its single inhabitant is **Silero VAD v5**, an external voice-activity detector. Two deliberately separate pieces:

- `SileroVad` — the model-bound part: a long-lived `InferenceSession`, the recurrent state Silero threads window-to-window, and the 64-sample context prefix the v5 wrapper prepends. Runs at 16 kHz over 512-sample windows. Cannot be exercised without the model file.
- `SileroSpeechTimestamps` — a pure, model-free port of the reference `get_speech_timestamps` hysteresis state machine (snakers4/silero-vad v5, the default `max_speech_duration_s = inf` path). Per-window probabilities in, padded speech `[start,end)` sample ranges out. Pure so it is unit-tested directly.

The model file (`silero_vad.onnx`, MIT, ~2.31 MB) is not in the repo; `SileroVadModel` carries its identity and a tag-pinned download URL so the hosting module provisions it through the shared `Downloader`.

Plain `Microsoft.ML.OnnxRuntime` (CPU) by design — the VAD model is tiny and must stay off the GPU that whisper holds. Never pull the `.DirectML` / `.Gpu` / genai variants here.
