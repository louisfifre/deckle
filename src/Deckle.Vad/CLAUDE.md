# CLAUDE.md — Deckle.Vad

Voice activity detection as a standalone capability: loads a VAD model, detects the
speech in a 16 kHz mono buffer, and trims a buffer to its speech. Callers (today
Transcription) depend on it; it never depends on them.

Inference runs through `Deckle.Inference.Onnx`; observation emits on the `Deckle-Vad`
provider. The model-specific code lives here — the inference module stays generic.
