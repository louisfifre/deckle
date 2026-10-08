---
description: Read-aloud (TTS) output — ISpeechBackend boundary and the Chatterbox port.
type: agent-instructions
---

# AGENTS.md — Deckle.Speech

- This module is a dormant skeleton: no trigger is wired yet.
- Synthesis runs off the UI thread. A second request interrupts the one in flight; there is no queue.
- `ISpeechBackend` mirrors `IAsrBackend`. The real Chatterbox decode moves to a `Deckle.Speech.Chatterbox` child module when it pulls `Microsoft.ML.OnnxRuntime`: that public dependency is what earns the split.

## Chatterbox port constraints

- It runs as four pure-ONNX graphs, no PyTorch. The recipe and the weights location live in `benchmark/asr/studies/tts-audition/chatterbox_synth.py`.
- fp16, never Q4: see `benchmark/asr/AGENTS.md`.
- `conditional_decoder` runs on CPU: it is ConvTranspose-heavy and fails on AMD DirectML with no automatic fallback.
- A voice is a reference clip encoded into a speaker embedding, not a model voice.
- Output is fixed at 24 kHz mono, Chatterbox's rate.
