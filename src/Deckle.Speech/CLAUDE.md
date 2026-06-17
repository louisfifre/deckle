---
description: Read-aloud (TTS) output module — the ISpeechBackend boundary, the placeholder skeleton, and the doctrine for the coming Chatterbox ONNX port.
type: agent-instructions
---

# CLAUDE.md — Deckle.Speech

The speaking counterpart of `Deckle.Transcription`: turn text into spoken audio on the default render device. Today it is a **dormant skeleton** — no trigger is wired yet. Its reason to exist is the output leg of a local voice-assistant loop (hotkey → Whisper → local LLM → spoken answer); the engine is constructed idle at boot, and `SpeechEngine.Speak(text)` is the entry point a future trigger will call. Synthesis runs off the UI thread; a second request interrupts the one in flight (no queue). The render primitive itself lives in `Deckle.Audio` (`SpeakerOutput`), reached one-way the same way capture is.

## Backend boundary

One `ISpeechBackend` drives synthesis — the mirror of `IAsrBackend`. The skeleton ships a single in-module placeholder (`ChatterboxSpeechBackend`, a 440 Hz tone that ignores voice/temperature). The real Chatterbox-Multilingual decode moves to its own `Deckle.Speech.Chatterbox` child module when it pulls `Microsoft.ML.OnnxRuntime` — that public-dependency change is what earns the split, the way `Deckle.Transcription.Whisper` sits behind `IAsrBackend`.

## The coming ONNX port — frozen constraints

The audition is closed: Chatterbox-Multilingual (MIT), retained, runs as **four pure-ONNX graphs** (`speech_encoder`, `embed_tokens`, `language_model_fp16`, `conditional_decoder`) — no PyTorch at inference. The proven recipe — and the on-disk location of the weights, provisioned outside the repo — lives in `benchmark/asr/studies/tts-audition/chatterbox_synth.py`.

- **fp16, never Q4/INT4** — the project no-Q4 ASR/TTS doctrine. fp16 is the high-precision variant, not a quantization.
- **`conditional_decoder` pinned to CPU** — it is ConvTranspose-heavy and hits the AMD DirectML wall (error 80070057, no auto-fallback). The three transformer/encoder graphs may ride DirectML; CPU vs GPU only moves latency, never the voice.
- **Voice is a reference clip, not a model voice** — Pierre/Jessica resolve to reference WAVs encoded once into speaker embeddings. The `SpeechVoice` enum is the user-facing selector; resolving it to a clip belongs inside the future backend.

Output is fixed at 24 kHz mono (Chatterbox's S3Gen rate).

## Observability

All emissions go through `DeckleSpeechSource.Log` (SPEECH tag). The read-aloud flow is bracketed — `ReadAloudRequested` → `ReadAloudComplete` — so success is legible at Informational level, not only failures.
