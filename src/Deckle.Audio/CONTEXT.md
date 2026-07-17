---
name: context-deckle-audio
description: "Audio vocabulary — display level vs transcription pre-processing, the two notions of « level » that must never be conflated. Read before touching level mapping or the DSP chain."
type: agent-instructions
---

# Deckle.Audio — Context

Two distinct notions carry the word "level" and must never be conflated: one drives the real-time visual, the other drives the signal actually handed to the transcription engine. They are decoupled by design — display is computed live during capture, signal processing is a terminal transform applied after Stop.

## Display level vs signal pre-processing

**Display level** :
The perceptual dBFS → [0, 1] mapping produced by `AudioLevelMapper`, calibrated over recent sessions, that drives the intensity of the recording outline while speaking. Concerns the visual render only; never alters the audio. Its calibration lives independently and stays outside the pre-processing scope.
_Avoid_ : gain, volume (those are signal operations, not display).

**Transcription pre-processing** :
A transform of the captured signal (filtering, compression, gain) applied to the `float[]` buffer between `MicrophoneCapture.Record()` and the ASR backend, for the sole purpose of maximizing machine intelligibility — not listening quality. Operates on the samples themselves, downstream of capture and upstream of transcription. Distinct from display level, and independent of how the buffer is windowed for the backend. Implemented as a post-capture two-pass DSP chain in `Deckle.Audio.Preprocessing` (`TranscriptionPreprocessor`); off by default and user-toggled, with a mic level check on the Recording page that advises whether it helps.
_Avoid_ : AGC (it is not real-time automatic gain — it runs once, post-capture), normalization (it is a dynamics chain, not a single peak/RMS scale).
