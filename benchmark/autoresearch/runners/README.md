---
name: readme-autoresearch-runners
description: "Runner helpers for autoresearch campaigns."
type: module-readme
module: benchmark/autoresearch/runners
---

# `runners/`

Store orchestration helpers for candidate generation, metric execution,
baseline comparison, and keep/discard logging. Keep them generic; a runner that
knows about Voxtral, Whisper, or Deckle ASR corpora belongs in `benchmark/asr/`.
