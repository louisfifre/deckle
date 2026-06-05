---
description: Audio module — the home for capturing and analyzing sound: microphone capture, RMS telemetry/calibration, and the pre-processing DSP for transcription.
type: agent-instructions
---

# CLAUDE.md — Deckle.Audio

The home for everything sound in Deckle — both capturing it (microphone WASAPI `waveIn`) and analyzing or shaping it (real-time RMS, session telemetry, level calibration, the pre-processing DSP stage). A module that needs audio reaches here and finds capture and treatment together. The scope is meant to grow — loopback, audio output, deeper analysis — and to split into sub-modules when that pays, the way `Deckle.Vision` pairs capture with analysis on the visual side.

Capture doesn't know *why* it runs (transcription, a future Ask-Ollama, anything) — only how to capture cleanly and surface the telemetry to calibrate the experience. Consumers implement `IAudioRecordingHost` (device id, duration cap, telemetry toggle) and get a `CaptureResult` back. The capture format is fixed and non-parameterizable: 16 kHz mono PCM16 is what Whisper expects, and what audio SLMs like Voxtral use too.

`AudioLevelMapper`'s dBFS→level curve lives in mutable statics by design, so the Playground can recalibrate it live and the HUD reads it every vsync. Tail RMS over the last 600 ms at Stop flags an unplugged or too-quiet mic; the telemetry is computed even when the "Log microphone" toggle is off, because auto-calibration still needs it.

## Transcription pre-processing (`Preprocessing/`)

A post-capture DSP stage that homogenizes the signal before the ASR backend — high-pass, optional gate, gentle compressor, two-pass makeup gain, limiter. It is **two-pass (measure then apply), never a real-time adaptive AGC**, and a pure `float[] → float[]` transform the orchestrator inserts before `TranscribeAsync`. The two-pass makeup self-normalizes every buffer to the same target level whatever it came in at.

**Why the defaults are gentle (central guardrail):** compressing hard lifts the inter-word noise floor, and a lifted noise floor feeds Whisper's silence hallucinations (the spurious « Sous-titres réalisés par… » boilerplate). Hence the ~2:1 ratio, gate **off by default**, conservative target. The parameters are conservative starting points, not asserted optima.

Two invariants: the sub-module is **pure — it emits nothing** (no EventSource dependency; the orchestrator emits the processed-take telemetry on its own provider), and the **corpus keeps the raw buffer** — when the stage is active the backend gets a separate processed buffer while the untouched `audio` is written to the corpus WAV, so a processed variant can always be re-derived.

**Activation is the user's call** — the `Enabled` toggle is the whole control: no auto-gate, no calibration delay (the stage is a near no-op on a mic already at target). A Mic level check records a sample, runs the real DSP on it, and advises *recommended / marginal / not needed* — it proposes, the toggle decides.

## Observability

All emissions go through `DeckleAudioSource.Log` (AUDIO tag).
