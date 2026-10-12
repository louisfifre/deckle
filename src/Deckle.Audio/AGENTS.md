---
description: Sound capture, decoding, analysis and transcription pre-processing.
type: agent-instructions
---

# AGENTS.md — Deckle.Audio

- Capture does not know why it runs. Consumers implement `IAudioRecordingHost`.
- The capture format is fixed at 16 kHz mono PCM16, not a parameter: Whisper expects it, and so do audio SLMs such as Voxtral.
- `AudioLevelMapper` keeps its curve in mutable statics by design: the Recording settings sliders and the engine's auto-calibration write it live, and the HUD reads it every vsync. The Playground only reads it: its HUD tab changes previews, never what the live HUD reads.
- Tail RMS is computed even when the "Log microphone" toggle is off, because auto-calibration needs it.
- Pre-processing is a pure two-pass transform (measure, then apply), never a real-time AGC. The gate is off by default and targets stay gentle: hard compression lifts the inter-word noise floor, which feeds Whisper's silence hallucinations. Whether the stage earns its place is still open.
- `SpeakerOutput` takes the format per call, because TTS does not run at the capture rate. It returns whether the clip reached the driver, so the caller can tell a silent device-open failure from success.
- `AudioFileDecoder` reports failures as statuses, not exceptions. The caller localizes them.
- COM interop stays internal and follows the `Deckle.Vision` vtable style: no `[ComImport]`.
