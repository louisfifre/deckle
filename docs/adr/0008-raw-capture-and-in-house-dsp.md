---
name: adr-0008-raw-capture-and-in-house-dsp
description: "Records that Deckle keeps raw mic capture (waveInOpen) and conditions the signal with an in-house DSP post-capture, ahead of the ASR backend, rather than switching to MediaCategory.Communications to inherit the Windows voice chain (AEC, noise suppression, AGC). Read before touching audio capture or proposing to enable system voice processing."
type: adr
---

# ADR-0008 — Raw mic capture + in-house DSP over Windows AGC

**Status** — accepted 2026-06-01

## Context

Deckle's mic capture is raw: `MicrophoneCapture` opens the device via `waveInOpen` and returns a `float[]` PCM 16 kHz mono (cf. [`src/Deckle.Audio/CLAUDE.md`](../../src/Deckle.Audio/CLAUDE.md)). No signal conditioning is applied before handing it to the ASR backend.

The problem motivating the decision: a low-level or high-dynamic microphone (quiet voice, whisper) is poorly transcribed, because ASR models are trained on normalized audio. Signal conditioning ahead of transcription is needed.

Windows offers a "free" path. Opening the mic via `Windows.Media.Capture.MediaCapture` with `MediaCategory.Communications` activates the system voice-processing chain — echo cancellation (AEC), noise suppression, automatic gain control (AGC) — tuned by Microsoft, used by Teams and Discord, and tracking the audio driver's improvements. It is the option to discard or retain explicitly before investing in an in-house DSP.

Two constraints weigh against the system path. The target profile includes professional audio interfaces (a Steinberg UR22C with a hardware gate) where the Windows voice chain on top of an already-conditioned signal would be redundant and uncontrolled. And the current `waveInOpen` capture is low-level, independent of the `MediaCapture` graph — switching would mean reworking capture to inherit a processing we do not control.

## Options considered

- **A. Switch capture to `MediaCapture` / `MediaCategory.Communications`.** Inherits AEC, noise suppression and AGC for free, tuned by Microsoft, adapted to any mic, tracking the driver. But: loss of control (documented risks like AGC pumping on long silences, noise suppression eroding phonemes), non-deterministic processing varying by driver and OS version, unwanted cumulation with the hardware processing of pro interfaces, and a capture rework (abandoning the low-level `waveInOpen` path).
- **B. Keep raw capture + condition with an in-house DSP post-capture.** Total, deterministic control of the signal, independent of the driver, tunable, with no interference with pro hardware. Cost: reimplementing what Windows provides, and owning the conditioning quality.
- **C. Selectable hybrid** — `Communications` for consumer mics, raw + DSP for pro interfaces. Two capture paths to maintain and test, fragile detection of the mic "type". Disproportionate at this stage.

## Decision

Option B. Deckle keeps raw capture via `waveInOpen` and conditions the signal with an in-house DSP applied post-capture, in `Deckle.Audio`, ahead of the ASR backend. No switch to `MediaCategory.Communications`.

The decision is one of **posture**: it fixes *where* the processing happens (in our hands, on a controlled raw signal), not *how* (the stage chain — high-pass, compression, makeup, limiter — lives as a recommendation to validate in the project memory, not in this ADR). It articulates with the vocabulary set in [`CONTEXT.md`](../../CONTEXT.md) (the *display level* / *transcription pre-processing* distinction).

## Consequences

Signal conditioning becomes deterministic and independent of the driver and OS version, and does not interfere with the hardware processing of professional audio interfaces. Deckle gains a single source of truth for the processed signal, reusable by any audio consumer.

In exchange, Deckle owns the conditioning quality — what the system path would have provided turn-key. The real gain remains **to be validated by measurement** (WER with and without, across the mic diversity); the DSP is not built yet.

Assumed edge case: if a user has enabled Windows AGC at OS or driver level, the in-house DSP would cumulate with it. Retained posture — **warn** the user rather than force-disable a system setting (nothing unequivocal about their signal). Detecting that state from the `waveInOpen` path remains to be investigated; feasibility unconfirmed.

This decision is **reversible**: a future `MediaCapture` capture backend stays possible behind the same `float[]` boundary if the in-house DSP proves insufficient across the real mic diversity. Re-evaluation condition: an in-house conditioning that fails to hold measured quality would justify reconsidering the system path, or a hybrid mode — recorded then by a new ADR superseding this one.
