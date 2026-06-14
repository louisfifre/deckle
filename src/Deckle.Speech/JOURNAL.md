---
description: Diagnosis notes and kept decisions for Deckle.Speech — read on demand when chasing why something is the way it is, not on every visit.
type: module-journal
---

# JOURNAL — Deckle.Speech

Not read by default. Come here when you need the *why* behind a choice the code no longer shows.

## 2026-06-14 — The dormant read-aloud skeleton, and what it is really for

The module landed as a skeleton with a placeholder backend (a 440 Hz tone), deliberately — the heavy Chatterbox ONNX port is a later palier. A clipboard-read hotkey (`Alt+Win+\``) was built as a first end-to-end demonstrator and then **removed**: reading the clipboard was never the intended gesture. The real purpose is the output leg of a local voice-assistant loop — the hotkey records, Whisper transcribes, the local LLM answers (ideally with Anytype/web access), and this module speaks the answer. So the module ships as dormant plumbing — `SpeechEngine`, `SpeakerOutput`, the `ISpeechBackend` boundary — with `SpeechEngine.Speak(text)` as the entry point awaiting that trigger.

The audition that chose Chatterbox is journaled in `benchmark/JOURNAL.md` (Chatterbox in, Orpheus out, pure ONNX proven). The build plan and its frozen constraints live in the module `CLAUDE.md` and the Anytype task « Synthèse vocale — portage ONNX ».
