---
description: Audio module — the home for capturing and analyzing sound: microphone capture, RMS telemetry/calibration, and the pre-processing DSP for transcription.
type: agent-instructions
---

# AGENTS.md — Deckle.Audio

The home for everything sound in Deckle — both capturing it (microphone WASAPI `waveIn`) and analyzing or shaping it (real-time RMS, session telemetry, level calibration, the pre-processing DSP stage). A module that needs audio reaches here and finds capture and treatment together. The scope is meant to grow — loopback, audio output, deeper analysis — and to split into sub-modules when that pays, the way `Deckle.Vision` pairs capture with analysis on the visual side.

Capture doesn't know *why* it runs (transcription, a future Ask-Ollama, anything) — only how to capture cleanly and surface the telemetry to calibrate the experience. Consumers implement `IAudioRecordingHost` (device id, duration cap, telemetry toggle) and get a `CaptureResult` back. The capture format is fixed and non-parameterizable: 16 kHz mono PCM16 is what Whisper expects, and what audio SLMs like Voxtral use too.

`AudioLevelMapper`'s dBFS→level curve lives in mutable statics by design, so the Playground can recalibrate it live and the HUD reads it every vsync. Tail RMS over the last 600 ms at Stop flags an unplugged or too-quiet mic; the telemetry is computed even when the "Log microphone" toggle is off, because auto-calibration still needs it.

## Transcription pre-processing (`Preprocessing/`)

An optional, user-toggled DSP stage (high-pass, optional gate, gentle compressor, two-pass makeup, limiter) that conditions the captured signal for the ASR backend — a pure `float[] → float[]` transform, two-pass (measure then apply), never a real-time AGC, emitting nothing of its own. Defaults are deliberately gentle: hard compression lifts the inter-word noise floor, which feeds Whisper's silence hallucinations (the spurious « Sous-titres réalisés par… »), so the gate is off by default and targets stay conservative — starting points, not asserted optima. Whether the stage earns its place is still open.

## Render output (`SpeakerOutput`)

The first output primitive — the symmetric counterpart of `MicrophoneCapture`: a single-clip, blocking `waveOut` render of a finished mono float buffer to the default device. Format is carried per call (TTS is 24 kHz, not the 16 kHz capture is fixed to). It returns whether the clip reached the driver, so the caller can tell a silent device-open failure from success; `Deckle.Speech` drives it.

## Observability

All emissions go through `DeckleAudioSource.Log` (AUDIO tag).
